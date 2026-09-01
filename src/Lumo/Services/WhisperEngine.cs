using System.Net.Http;
using System.Net.Http.Headers;

namespace Lumo.Services;

/// <summary>
/// v2.6.0-alpha.5 — the Whisper (whisper.cpp) transcription engine behind the
/// "better speech recognition" upgrade. Two halves:
///
///   · MODELS  — the ggml checkpoints from the official whisper.cpp Hugging Face
///     repo (catalog in Core/VoiceWhisper). DownloadAsync streams one to
///     DataDir/models with progress + cancellation, writes through a .tmp file
///     and only then renames, so a crash can never leave a half-written model
///     that looks installed. Sizes are re-verified after download.
///   · INFERENCE — TranscribeAsync converts the raw PCM capture to floats and
///     runs one blocking whisper pass on a worker thread. The WhisperFactory is
///     loaded ONCE per model file and cached for the app lifetime (weights are
///     ~100-500 MB — reloading per clip would add seconds per transcription);
///     each clip gets its own processor, so concurrent sessions can't share
///     state. Segments whisper reports as probable silence-hallucinations
///     (high no-speech probability, low token confidence) are dropped instead
///     of surfacing as phantom words.
///
/// Every failure returns a readable string or false — nothing throws across
/// the API into the voice session.
/// </summary>
public static class WhisperEngine
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(30),   // small-model downloads on slow lines still land
    };

    /// <summary>Directory the models are installed into (portable-mode aware via AppPaths).</summary>
    public static string ModelsDir => Path.Combine(Core.AppPaths.DataDir, "models");

    public static string ModelPath(string fileName) => Path.Combine(ModelsDir, fileName);

    /// <summary>True when the model file exists and is at least 90 % of the advertised size.</summary>
    public static bool IsDownloaded(Core.VoiceWhisper.WhisperModel model)
    {
        try
        {
            var fi = new FileInfo(ModelPath(model.FileName));
            return fi.Exists && fi.Length >= model.Bytes * 9 / 10;
        }
        catch { return false; }
    }

    /// <summary>Readable size caption for the setup card ("147 MB of 147 MB").</summary>
    public static string DownloadedLabel(Core.VoiceWhisper.WhisperModel model)
    {
        try
        {
            var fi = new FileInfo(ModelPath(model.FileName));
            if (!fi.Exists) return $"0 of {model.SizeLabel}";
            return $"{fi.Length / 1_000_000.0:0} of {model.SizeLabel}";
        }
        catch { return $"0 of {model.SizeLabel}"; }
    }

    /// <summary>
    /// Downloads a model with 0..1 progress and cancellation. Returns null on
    /// success, otherwise a short human reason. The file lands atomically:
    /// streamed into a Guid .tmp, size-verified, then renamed over any previous
    /// partial install.
    /// </summary>
    public static async Task<string?> DownloadAsync(
        Core.VoiceWhisper.WhisperModel model, IProgress<double>? progress, CancellationToken ct)
    {
        string finalPath = ModelPath(model.FileName);
        string tmpPath = Path.Combine(ModelsDir, $"dl-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(ModelsDir);

            using var req = new HttpRequestMessage(HttpMethod.Get, model.Url);
            req.Headers.UserAgent.ParseAdd("Lumo-Launcher/2.6");
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return $"Download failed — HTTP {(int)resp.StatusCode}";

            long? total = resp.Content.Headers.ContentLength;

            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                written += read;
                if (progress is not null)
                {
                    double frac = total is { } t && t > 0 ? Math.Min(1.0, written / (double)t) : 0;
                    progress.Report(frac);
                }
            }

            await dst.FlushAsync(ct).ConfigureAwait(false);

            // size guard: a truncated download must never look installed
            var fi = new FileInfo(tmpPath);
            if (fi.Length < model.Bytes * 9 / 10)
            {
                try { fi.Delete(); } catch { }
                return "Download failed — the file was incomplete (connection dropped?)";
            }

            File.Move(tmpPath, finalPath, overwrite: true);
            progress?.Report(1.0);
            DiagnosticLogger.Log("Voice", $"Whisper model installed: {model.FileName} ({fi.Length / 1_000_000.0:0} MB)");
            return null;
        }
        catch (OperationCanceledException)
        {
            try { File.Delete(tmpPath); } catch { }
            return "cancelled";
        }
        catch (Exception ex)
        {
            try { File.Delete(tmpPath); } catch { }
            DiagnosticLogger.LogException("Voice.WhisperDownload", ex);
            return "Download failed — " + ex.Message;
        }
    }

    // ---------------------------------------------------------------- inference

    private static readonly object FactoryGate = new();
    private static Whisper.net.WhisperFactory? _factory;
    private static string? _factoryPath;

    /// <summary>
    /// One blocking whisper pass over a captured clip (16 kHz 16-bit mono PCM).
    /// Returns the concatenated segment text with whitespace collapsed, or null
    /// on failure (the caller surfaces its own didn't-catch message). The clip
    /// gets a fresh processor; the loaded model weights stay cached.
    /// </summary>
    public static async Task<string?> TranscribeAsync(
        byte[] pcm16, Core.VoiceWhisper.WhisperModel model, string language, CancellationToken ct)
    {
        try
        {
            var factory = GetFactory(model.FileName);
            if (factory is null) return null;

            var samples = Core.VoiceAudio.ToFloatSamples(pcm16);
            if (samples.Length == 0) return null;

            await using var processor = factory.CreateBuilder()
                .WithLanguage(language)
                .WithNoContext()          // batch mode: never carry context between clips
                .Build();

            var sb = new System.Text.StringBuilder();
            await foreach (var seg in processor.ProcessAsync(samples, ct).ConfigureAwait(false))
            {
                // the classic whisper hallucination guard: segments where the model
                // itself says "there was no speech here" but tokens came out anyway
                if (seg.NoSpeechProbability > 0.85f && seg.Probability < 0.40f) continue;
                sb.Append(seg.Text);
                sb.Append(' ');
            }

            return Collapse(sb.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;   // the session layer treats cancel as "discard the result"
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Voice.WhisperTranscribe", ex);
            return null;
        }
    }

    /// <summary>Drops the whisper "Thank you."-on-silence class of junk: empty output stays empty.</summary>
    private static string Collapse(string raw)
    {
        var sb = new System.Text.StringBuilder();
        bool space = false;
        foreach (var ch in raw)
        {
            if (char.IsWhiteSpace(ch)) { space = true; continue; }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    /// <summary>The cached factory for a model file — loaded once, reused for every clip.</summary>
    private static Whisper.net.WhisperFactory? GetFactory(string fileName)
    {
        string path = ModelPath(fileName);
        lock (FactoryGate)
        {
            if (_factory is not null && string.Equals(_factoryPath, path, StringComparison.Ordinal))
                return _factory;

            try
            {
                _factory?.Dispose();
            }
            catch { }

            try
            {
                _factory = Whisper.net.WhisperFactory.FromPath(path);
                _factoryPath = path;
                return _factory;
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogException("Voice.WhisperFactory", ex);
                _factory = null;
                _factoryPath = null;
                return null;
            }
        }
    }
}
