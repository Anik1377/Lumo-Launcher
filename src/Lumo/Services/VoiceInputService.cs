using System.Globalization;
using System.Speech.Recognition;
using System.Windows.Threading;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>
/// v2.6.0-alpha.3 — offline voice typing for the AI chat, on the Windows desktop
/// speech stack (SAPI) that ships with the OS itself. No cloud call, no API key,
/// no extra install: dictation runs locally on whatever recognizer the machine
/// already has ("en-US" ships with English Windows; more languages come from
/// Windows Settings → Time &amp; Language → Speech). This matches the launcher's
/// privacy doctrine — the AI runs on a local Ollama by default, its voice input
/// never leaves the PC either.
///
/// Session shape: Start() builds a fresh SpeechRecognitionEngine on the calling
/// (UI) thread, loads the plain DictationGrammar and runs RecognizeAsync(Multiple)
/// — SAPI then keeps listening across pauses and finalizes a segment whenever the
/// speaker stops. SpeechHypothesized drives the live partial text, SpeechRecognized
/// the settled segments; both are marshalled onto the Dispatcher that called Start,
/// so window code never sees a worker thread. Stop() cancels, disposes the engine
/// and raises ListeningStopped exactly once. Every failure — no mic, no recognizer,
/// COM error — surfaces as Failed(reason), never an exception crossing the API.
/// </summary>
public sealed class VoiceInputService : IDisposable
{
    /// <summary>Drops obvious SAPI noise: below this confidence a finalized segment is discarded (and logged).</summary>
    private const float MinConfidence = 0.25f;

    /// <summary>How long silence closes a dictation segment — short enough to feel live, long enough for clause breaks.</summary>
    private static readonly TimeSpan EndSilence = TimeSpan.FromMilliseconds(450);

    private SpeechRecognitionEngine? _engine;
    private Dispatcher? _dispatcher;
    private bool _stopAnnounced;

    public bool IsListening { get; private set; }

    public static bool IsSupported
    {
        get
        {
            try { return SpeechRecognitionEngine.InstalledRecognizers().Count > 0; }
            catch { return false; }
        }
    }

    public event Action? ListeningStarted;
    public event Action<string>? Partial;    // live hypothesis — SAPI fires this constantly
    public event Action<string>? Final;      // settled dictation segment
    public event Action? ListeningStopped;
    public event Action<string>? Failed;     // human-readable reason, shown in the UI

    /// <summary>Installed recognizers as (id, culture) pairs — empty on any error. Feeds the picker and the Settings status line.</summary>
    public static IReadOnlyList<(string Id, string Culture)> Installed()
    {
        try
        {
            var list = new List<(string, string)>();
            foreach (var r in SpeechRecognitionEngine.InstalledRecognizers())
                list.Add((r.Id, r.Culture?.Name ?? ""));
            return list;
        }
        catch { return Array.Empty<(string, string)>(); }
    }

    /// <summary>
    /// Starts a dictation session. Safe to call when already listening (no-op) and
    /// on machines without speech support (Failed fires with the reason). Must be
    /// called on the UI thread — its Dispatcher receives every event.
    /// </summary>
    /// <param name="preferredCulture">settings.json "VoiceLanguage" — "" follows the OS UI language.</param>
    public void Start(string? preferredCulture)
    {
        if (IsListening) return;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _stopAnnounced = false;
        try
        {
            var infos = SpeechRecognitionEngine.InstalledRecognizers();
            var pickId = VoiceLanguage.Pick(
                preferredCulture,
                infos.Select(i => (i.Id, i.Culture?.Name ?? "")).ToList(),
                CultureInfo.CurrentUICulture.Name);

            RecognizerInfo? info = null;
            if (pickId is not null)
                info = infos.FirstOrDefault(i => string.Equals(i.Id, pickId, StringComparison.OrdinalIgnoreCase));

            var engine = info is null ? new SpeechRecognitionEngine() : new SpeechRecognitionEngine(info);
            engine.SetInputToDefaultAudioDevice();          // throws (COM) when no capture device — handled below
            engine.LoadGrammar(new DictationGrammar());
            engine.EndSilenceTimeout = EndSilence;
            engine.BabbleTimeout = TimeSpan.Zero;           // async mode: don't kill the session on first silence
            engine.InitialSilenceTimeout = TimeSpan.Zero;

            engine.SpeechHypothesized += (_, e) => RaisePartial(e.Result?.Text ?? "");
            engine.SpeechRecognized += (_, e) =>
            {
                var text = e.Result?.Text ?? "";
                var conf = e.Result?.Confidence ?? 0f;
                if (text.Length > 0 && conf >= MinConfidence) RaiseFinal(text);
                else if (text.Length > 0)
                    DiagnosticLogger.Log("Voice", $"Dropped low-confidence dictation segment ({conf:0.00})");
            };
            engine.SpeechRecognitionRejected += (_, _) => RaisePartial("");   // clear the ghost text
            engine.RecognizeCompleted += (_, _) => StopInternal();            // fires after RecognizeAsyncCancel

            engine.RecognizeAsync(RecognizeMode.Multiple);
            _engine = engine;
            IsListening = true;
            DiagnosticLogger.Log("Voice", $"Dictation started ({info?.Culture?.Name ?? "default recognizer"})");
            ListeningStarted?.Invoke();
        }
        catch (Exception ex)
        {
            try { _engine?.Dispose(); } catch { }
            _engine = null;
            IsListening = false;
            var reason = Friendly(ex);
            DiagnosticLogger.Log("Voice", reason);
            Failed?.Invoke(reason);
        }
    }

    /// <summary>Ends the session; the text already committed to the prompt stays.</summary>
    public void Stop()
    {
        if (!IsListening && _engine is null) return;
        StopInternal();
    }

    private void StopInternal()
    {
        var engine = _engine;
        _engine = null;
        bool wasListening = IsListening;
        IsListening = false;
        if (engine is not null)
        {
            try { engine.RecognizeAsyncCancel(); } catch { }
            try { engine.Dispose(); } catch { }
        }
        if (wasListening && !_stopAnnounced)
        {
            _stopAnnounced = true;
            DiagnosticLogger.Log("Voice", "Dictation stopped");
            ListeningStopped?.Invoke();
        }
    }

    /// <summary>Events from SAPI's audio thread → BeginInvoke onto the session's dispatcher.</summary>
    private void RaisePartial(string text) => _dispatcher?.BeginInvoke(() => { if (IsListening) Partial?.Invoke(text); });
    private void RaiseFinal(string text) => _dispatcher?.BeginInvoke(() => { if (IsListening) Final?.Invoke(text); });

    /// <summary>SAPI errors are COM walls of text — translate the two common ones, pass the rest through.</summary>
    private static string Friendly(Exception ex)
    {
        var msg = ex.Message ?? "";
        if (msg.Contains("audio", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("wave", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("microphone", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("sound", StringComparison.OrdinalIgnoreCase))
            return "No microphone available — check Sound settings, then try again.";
        if (msg.Contains("recognizer", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("grammar", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("language", StringComparison.OrdinalIgnoreCase))
            return "Speech recognition could not start — install it under Windows Settings → Time & Language → Speech.";
        return "Voice input failed to start: " + msg;
    }

    public void Dispose() => Stop();
}
