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
/// v2.6.0-alpha.5 — offline voice typing for the AI chat: <b>record, then
/// transcribe, then show</b>, now with a two-tier engine.
///
///   1. RECORD   — Start() opens WaveRecorder and captures the complete clip
///                 (16 kHz/16-bit/mono) while the user speaks, no recognition.
///                 v2.6.0-alpha.5 adds Pause()/Resume() (the paused stretch is
///                 really discarded from the clip) and a 10 Hz Level feed for
///                 the live waveform.
///   2. TRANSCRIBE — Stop() ends the clip, cuts the room tone off both edges
///                 (VoiceAudio.TrimSilence) and hands the WHOLE utterance to
///                 the configured engine on a background thread:
///                   · whisper  (default) — OpenAI's Whisper via whisper.cpp
///                     (Services/WhisperEngine): dramatically more accurate
///                     than the Windows desktop recognizer, still fully
///                     offline. The model is downloaded once on demand; when
///                     it is missing, ModelNeeded fires BEFORE any recording
///                     starts and the UI offers the one-time setup.
///                   · windows — the SAPI fallback, one blocking Recognize()
///                     over the whole clip (the v2.6.0-alpha.4 path, kept for
///                     machines that skip the download).
///   3. SHOW     — the finished text is marshalled to the calling Dispatcher
///                 and raised as Final exactly once; nothing touches the prompt
///                 box until recognition is done (WYSIWYG: what appears is what
///                 a second Enter sends).
///
/// A generation counter makes results from a cancelled session land silently —
/// Cancel() during recording discards the clip, during transcribing discards
/// the pending text. Every failure — no mic, no engine, no speech — surfaces as
/// Failed(reason), never an exception crossing the API.
/// </summary>
public sealed class VoiceInputService : IDisposable
{
    /// <summary>Drops obvious SAPI noise: below this confidence the whole clip is treated as a no-match.</summary>
    private const float MinConfidence = 0.25f;

    /// <summary>A clip shorter than this after trimming is treated as "didn't hear anything".</summary>
    private const int MinClipMs = 250;

    public const string EngineWhisper = "whisper";
    public const string EngineWindows = "windows";

    private const string NoSpeechMessage =
        "Didn't hear anything — speak after clicking the mic, then click again to finish.";
    private const string NoMatchMessage =
        "Couldn't make out the words — try again a little closer to the mic.";

    private WaveRecorder? _recorder;
    private Dispatcher? _dispatcher;
    private string? _preferred;
    private string _engine = EngineWhisper;
    private string _modelId = VoiceWhisper.DefaultModelId;
    private int _generation;

    public VoiceStage Stage { get; private set; } = VoiceStage.Idle;

    /// <summary>True in either active stage — kept for the window's Esc / focus-ring checks.</summary>
    public bool IsListening => Stage != VoiceStage.Idle;
    public bool IsRecording => Stage == VoiceStage.Recording;
    public bool IsTranscribing => Stage == VoiceStage.Transcribing;

    /// <summary>v2.6.0-alpha.5 — paused capture: the mic is open but audio is being discarded.</summary>
    public bool IsPaused => _recorder?.IsPaused ?? false;

    /// <summary>Windows SAPI recognizers installed (the fallback engine's availability).</summary>
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
    public event Action<double>? Level;         // v2.6.0-alpha.5 — 0..1 mic loudness, ~10 Hz
    public event Action<string>? ModelNeeded;   // v2.6.0-alpha.5 — whisper chosen but the model isn't installed

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

    /// <summary>Legacy entry point — defaults to the Whisper engine with the catalog default model.</summary>
    public void Start(string? preferredCulture) =>
        Start(preferredCulture, EngineWhisper, VoiceWhisper.DefaultModelId);

    /// <summary>
    /// Starts recording a clip with the configured engine. Safe to call when
    /// busy (no-op) and on machines without speech support (Failed fires with
    /// the reason). When the Whisper engine is selected but its model is not
    /// installed yet, ModelNeeded fires and nothing is recorded — the UI shows
    /// the one-time setup card. Must be called on the UI thread — its Dispatcher
    /// receives every event.
    /// </summary>
    /// <param name="preferredCulture">settings.json "VoiceLanguage" — "" follows the OS UI language.</param>
    /// <param name="engine">"whisper" (default) or "windows" (SAPI fallback).</param>
    /// <param name="modelId">settings.json "VoiceModel" — a Core/VoiceWhisper catalog id.</param>
    public void Start(string? preferredCulture, string engine, string modelId)
    {
        if (Stage != VoiceStage.Idle) return;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _preferred = preferredCulture;
        _engine = string.Equals(engine, EngineWindows, StringComparison.OrdinalIgnoreCase) ? EngineWindows : EngineWhisper;
        _modelId = modelId;

        if (_engine == EngineWhisper)
        {
            var model = VoiceWhisper.FromId(_modelId);
            if (!WhisperEngine.IsDownloaded(model))
            {
                DiagnosticLogger.Log("Voice", $"Whisper model missing: {model.Id} — asking the UI to set it up");
                ModelNeeded?.Invoke(model.Id);
                return;
            }
        }

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

            // 10 Hz mic loudness → the waveform visualizer (already on the pump
            // thread; marshal once here so every consumer lands on the UI thread)
            recorder.LevelAvailable += level => _dispatcher?.BeginInvoke(() =>
            {
                if (IsRecording) Level?.Invoke(level);
            });

            _recorder = recorder;
            Stage = VoiceStage.Recording;
            DiagnosticLogger.Log("Voice", $"Recording started (engine: {_engine})");
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

    /// <summary>v2.6.0-alpha.5 — pause capture: the mic stays open, the clip doesn't grow.</summary>
    public void Pause()
    {
        if (Stage == VoiceStage.Recording)
        {
            _recorder?.Pause();
            DiagnosticLogger.Log("Voice", "Recording paused");
        }
    }

    /// <summary>v2.6.0-alpha.5 — resume after Pause().</summary>
    public void Resume()
    {
        if (Stage == VoiceStage.Recording)
        {
            _recorder?.Resume();
            DiagnosticLogger.Log("Voice", "Recording resumed");
        }
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

    /// <summary>Recording → Transcribing: stop the capture, hand the PCM to the configured engine.</summary>
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
        var engine = _engine;
        var modelId = _modelId;
        var dispatcher = _dispatcher;
        DiagnosticLogger.Log("Voice", $"Recording finished ({pcm.Length / 2000.0:0.0#} s) — transcribing with {engine}");
        TranscribingStarted?.Invoke();

        _ = Task.Run(() => TranscribeAsync(pcm, preferred, engine, modelId, gen, dispatcher));
    }

    /// <summary>
    /// The batch pass — background thread: trim edge silence, then run the clip
    /// through the configured engine and marshal the finished text to the UI.
    /// This is where the accuracy comes from: the engine sees the complete
    /// utterance with full acoustic context instead of forced 450 ms segments,
    /// and silence-trimmed audio can't hallucinate edge filler.
    /// </summary>
    private async Task TranscribeAsync(byte[] pcm, string? preferred, string engine, string modelId, int gen, Dispatcher? dispatcher)
    {
        try
        {
            var range = VoiceAudio.TrimSilence(pcm, VoiceAudio.SampleRate);
            if (range is null || range.Value.End - range.Value.Start < VoiceAudio.SampleRate / 1000 * MinClipMs * 2)
                throw new InvalidOperationException(NoSpeechMessage);

            var clip = pcm.AsSpan(range.Value.Start, range.Value.End - range.Value.Start).ToArray();
            string text;

            if (engine == EngineWhisper)
            {
                var model = VoiceWhisper.FromId(modelId);
                string language = VoiceWhisper.ResolveLanguage(model, preferred);
                var raw = await WhisperEngine.TranscribeAsync(clip, model, language, CancellationToken.None).ConfigureAwait(false);
                if (raw is null)
                    throw new InvalidOperationException("Whisper could not run — check the log or pick Windows speech in settings.json (\"VoiceEngine\": \"windows\").");
                text = raw;
                DiagnosticLogger.Log("Voice", $"Whisper ok ({text.Length} chars, lang {language})");
            }
            else
            {
                text = SapiTranscribe(clip, preferred);
            }

            if (text.Length == 0)
            {
                DiagnosticLogger.Log("Voice", "Batch dictation produced no text");
                throw new InvalidOperationException(NoMatchMessage);
            }

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

    /// <summary>
    /// The SAPI fallback (settings "VoiceEngine": "windows"): one blocking
    /// Recognize() over the whole clip with a fresh engine, exactly the
    /// v2.6.0-alpha.4 batch flow.
    /// </summary>
    private static string SapiTranscribe(byte[] clip, string? preferred)
    {
        var wav = VoiceAudio.BuildWav(clip, VoiceAudio.SampleRate, VoiceAudio.Channels, VoiceAudio.BitsPerSample);

        var infos = SpeechRecognitionEngine.InstalledRecognizers();
        if (infos.Count == 0)
            throw new InvalidOperationException("Windows speech is not installed — switch the engine back to Whisper or install it under Windows Settings → Time & Language → Speech.");

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

        using var ms = new MemoryStream(wav);
        engine.SetInputToWaveStream(ms);
        var result = engine.Recognize();              // blocking — the whole clip at once
        string text = result?.Text ?? "";
        float confidence = result?.Confidence ?? 0f;

        if (text.Length == 0 || confidence < MinConfidence)
        {
            DiagnosticLogger.Log("Voice", $"SAPI batch dictation rejected ({confidence:0.00}, \"{text}\")");
            throw new InvalidOperationException(NoMatchMessage);
        }
        return text;
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
