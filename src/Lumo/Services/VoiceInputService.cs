using System.Globalization;
using System.Speech.Recognition;
using System.Windows.Threading;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>v2.6.0-alpha.4 — the three stages a voice session walks through, exposed for UI state.</summary>
public enum VoiceStage
{
    Idle,
    Recording,
    Transcribing,
}

/// <summary>
/// v2.6.0-alpha.4 — offline voice typing for the AI chat, rebuilt around the
/// accuracy fix the previous live-dictation build needed: <b>record, then
/// transcribe, then show</b>. The old build ran RecognizeAsync(Multiple) and
/// finalized a segment at every 450 ms pause, so half-thoughts were committed
/// mid-sentence with no acoustic context and every "final" word stuck. Now a
/// session is a batch job:
///
///   1. RECORD   — Start() opens WaveRecorder and captures the complete clip
///                 (16 kHz/16-bit/mono) while the user speaks, no recognition.
///   2. TRANSCRIBE — Stop() ends the clip, cuts the room tone off both edges
///                 (VoiceAudio.TrimSilence — silence makes SAPI hallucinate
///                 filler words), wraps the PCM as a WAV stream and runs ONE
///                 blocking Recognize() on a background thread, so the engine
///                 sees the whole utterance with full context.
///   3. SHOW     — the finished text is marshalled to the calling Dispatcher
///                 and raised as Final exactly once; nothing touches the prompt
///                 box until recognition is done (WYSIWYG: what appears is what
///                 a second Enter sends).
///
/// A generation counter makes results from a cancelled session land silently —
/// Cancel() during recording discards the clip, during transcribing discards
/// the pending text. Every failure — no mic, no recognizer, no speech, COM
/// error — surfaces as Failed(reason), never an exception crossing the API.
/// The engine itself stays SAPI (ships with Windows, no cloud, no key); it is
/// created fresh per transcription so a poisoned engine can't linger.
/// </summary>
public sealed class VoiceInputService : IDisposable
{
    /// <summary>Drops obvious SAPI noise: below this confidence the whole clip is treated as a no-match.</summary>
    private const float MinConfidence = 0.25f;

    /// <summary>A clip shorter than this after trimming is treated as "didn't hear anything".</summary>
    private const int MinClipMs = 250;

    private const string NoSpeechMessage =
        "Didn't hear anything — speak after clicking the mic, then click again to finish.";
    private const string NoMatchMessage =
        "Couldn't make out the words — try again a little closer to the mic.";

    private WaveRecorder? _recorder;
    private Dispatcher? _dispatcher;
    private string? _preferred;
    private int _generation;

    public VoiceStage Stage { get; private set; } = VoiceStage.Idle;

    /// <summary>True in either active stage — kept for the window's Esc / focus-ring checks.</summary>
    public bool IsListening => Stage != VoiceStage.Idle;
    public bool IsRecording => Stage == VoiceStage.Recording;
    public bool IsTranscribing => Stage == VoiceStage.Transcribing;

    public static bool IsSupported
    {
        get
        {
            try { return SpeechRecognitionEngine.InstalledRecognizers().Count > 0; }
            catch { return false; }
        }
    }

    public event Action? CaptureStarted;        // recording begins — mic lights up
    public event Action? TranscribingStarted;   // clip finished, recognition running
    public event Action<string>? Final;         // the whole clip transcribed — show it
    public event Action<string>? Failed;        // human-readable reason, shown in the UI

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
    /// Starts recording a clip. Safe to call when busy (no-op) and on machines
    /// without speech support (Failed fires with the reason). Must be called on
    /// the UI thread — its Dispatcher receives every event.
    /// </summary>
    /// <param name="preferredCulture">settings.json "VoiceLanguage" — "" follows the OS UI language.</param>
    public void Start(string? preferredCulture)
    {
        if (Stage != VoiceStage.Idle) return;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _preferred = preferredCulture;
        ++_generation;

        var recorder = new WaveRecorder();
        try
        {
            var err = recorder.Start();
            if (err is not null) throw new InvalidOperationException(err);

            // 60 s hard stop: finish the clip the same way a manual stop would
            recorder.LimitReached += () => _dispatcher?.BeginInvoke(() =>
            {
                if (IsRecording) FinishCapture();
            });

            _recorder = recorder;
            Stage = VoiceStage.Recording;
            DiagnosticLogger.Log("Voice", "Recording started");
            CaptureStarted?.Invoke();
        }
        catch (Exception ex)
        {
            try { recorder.Dispose(); } catch { }
            Stage = VoiceStage.Idle;
            var reason = ex is InvalidOperationException ? ex.Message : Friendly(ex);
            DiagnosticLogger.Log("Voice", reason);
            Failed?.Invoke(reason);
        }
    }

    /// <summary>Finishes the recording and starts transcribing it (mic click #2, Enter, send).</summary>
    public void Stop()
    {
        if (Stage == VoiceStage.Recording) FinishCapture();
    }

    /// <summary>
    /// Discards the session: during recording the clip is thrown away, during
    /// transcribing the pending result is abandoned (the generation counter
    /// makes it land silently).
    /// </summary>
    public void Cancel()
    {
        ++_generation;
        if (Stage == VoiceStage.Recording)
        {
            try { _recorder?.Dispose(); } catch { }
            _recorder = null;
            Stage = VoiceStage.Idle;
            DiagnosticLogger.Log("Voice", "Recording cancelled");
        }
        else if (Stage == VoiceStage.Transcribing)
        {
            Stage = VoiceStage.Idle;
            DiagnosticLogger.Log("Voice", "Transcription discarded");
        }
    }

    /// <summary>Recording → Transcribing: stop the capture, hand the PCM to the batch recognizer.</summary>
    private void FinishCapture()
    {
        if (Stage != VoiceStage.Recording) return;

        var recorder = _recorder;
        _recorder = null;
        byte[]? pcm = null;
        try { pcm = recorder?.StopAndRead(); }
        catch (Exception ex) { DiagnosticLogger.LogException("Voice.Capture", ex); }
        try { recorder?.Dispose(); } catch { }

        if (pcm is null || pcm.Length < VoiceAudio.SampleRate / 1000 * MinClipMs * 2)
        {
            Stage = VoiceStage.Idle;
            DiagnosticLogger.Log("Voice", "Captured clip empty or too short");
            Failed?.Invoke(NoSpeechMessage);
            return;
        }

        Stage = VoiceStage.Transcribing;
        var gen = ++_generation;
        var preferred = _preferred;
        var dispatcher = _dispatcher;
        DiagnosticLogger.Log("Voice", $"Recording finished ({pcm.Length / 2000.0:0.0#} s) — transcribing");
        TranscribingStarted?.Invoke();

        Task.Run(() => Transcribe(pcm, preferred, gen, dispatcher));
    }

    /// <summary>
    /// The batch pass — background thread: trim edge silence, wrap as WAV, one
    /// blocking Recognize() over the whole clip, then marshal the finished text
    /// to the UI. This is where the accuracy comes from: the engine sees the
    /// complete utterance with full acoustic context instead of forced 450 ms
    /// segments, and silence-trimmed audio can't hallucinate edge filler.
    /// </summary>
    private void Transcribe(byte[] pcm, string? preferred, int gen, Dispatcher? dispatcher)
    {
        try
        {
            var range = VoiceAudio.TrimSilence(pcm, VoiceAudio.SampleRate);
            if (range is null || range.Value.End - range.Value.Start < VoiceAudio.SampleRate / 1000 * MinClipMs * 2)
                throw new InvalidOperationException(NoSpeechMessage);

            var wav = VoiceAudio.BuildWav(
                pcm.AsSpan(range.Value.Start, range.Value.End - range.Value.Start),
                VoiceAudio.SampleRate, VoiceAudio.Channels, VoiceAudio.BitsPerSample);

            // same recognizer the live build picked — settings pinned or OS-following
            var infos = SpeechRecognitionEngine.InstalledRecognizers();
            var pickId = VoiceLanguage.Pick(
                preferred,
                infos.Select(i => (i.Id, i.Culture?.Name ?? "")).ToList(),
                CultureInfo.CurrentUICulture.Name);
            RecognizerInfo? info = pickId is null
                ? null
                : infos.FirstOrDefault(i => string.Equals(i.Id, pickId, StringComparison.OrdinalIgnoreCase));

            using var engine = info is null ? new SpeechRecognitionEngine() : new SpeechRecognitionEngine(info);
            engine.LoadGrammar(new DictationGrammar());
            engine.BabbleTimeout = TimeSpan.Zero;         // batch mode: the clip is finite,
            engine.InitialSilenceTimeout = TimeSpan.Zero; // timeouts only manufacture rejections
            engine.EndSilenceTimeout = TimeSpan.Zero;

            string text;
            float confidence;
            using (var ms = new MemoryStream(wav))
            {
                engine.SetInputToWaveStream(ms);
                var result = engine.Recognize();          // blocking — the whole clip at once
                text = result?.Text ?? "";
                confidence = result?.Confidence ?? 0f;
            }

            if (text.Length == 0 || confidence < MinConfidence)
            {
                DiagnosticLogger.Log("Voice", $"Batch dictation rejected ({confidence:0.00}, \"{text}\")");
                throw new InvalidOperationException(NoMatchMessage);
            }

            DiagnosticLogger.Log("Voice", $"Batch dictation ok ({confidence:0.00}, {text.Length} chars)");
            dispatcher?.BeginInvoke(() =>
            {
                if (gen != _generation) return;           // cancelled while recognizing — land silently
                Stage = VoiceStage.Idle;
                Final?.Invoke(text);
            });
        }
        catch (Exception ex)
        {
            var reason = ex is InvalidOperationException ? ex.Message : Friendly(ex);
            DiagnosticLogger.Log("Voice", reason);
            dispatcher?.BeginInvoke(() =>
            {
                if (gen != _generation) return;
                Stage = VoiceStage.Idle;
                Failed?.Invoke(reason);
            });
        }
    }

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
        return "Voice input failed: " + msg;
    }

    public void Dispose() => Cancel();
}
