using System.Net.Http;
using System.Net.Http.Headers;

namespace Lumo.Services;

/// <summary>
/// v2.6.0-alpha.6 — the Whisper (whisper.cpp) transcription engine behind the
/// "better speech recognition" upgrade. Two halves:
///
///   · MODELS  — the ggml checkpoints from the official whisper.cpp Hugging Face
///     repo (catalog in Core/VoiceWhisper). DownloadAsync streams one to
///     DataDir/models with progress + cancellation and RESUMES a broken
///     download: partial data lands in a stable "&lt;model&gt;.bin.part" file and
///     the next attempt continues from where the connection dropped (HTTP
///     Range). Transient failures retry automatically. The finished file is
///     moved into place only after the cached WhisperFactory for that path has
///     been released (the factory holds the ggml file open — without this a
///     re-download fails with "file is in use") and the move itself retries a
///     few times so a concurrently-scanning antivirus can't block it.
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
    private const int MaxAttempts = 3;          // transient network failures retry this many times
    private const int MoveRetries = 4;          // antivirus scans lock fresh files for a moment
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
    /// v2.6.0-alpha.6 — downloads a model with 0..1 progress, cancellation,
    /// RESUME and automatic retries. Returns null on success, otherwise a short
    /// human reason. The flow:
    ///   · partial data lands in a stable "&lt;file&gt;.part" next to the final name,
///     so a dropped connection (or a cancelled attempt) continues from where
///     it stopped on the next try instead of starting over;
    ///   · each attempt sends a Range header from the .part's length — a server
    ///     that answers 206 appends, one that ignores ranges restarts cleanly;
    ///   · transient failures (connection reset, 5xx) retry up to 3 times;
    ///   · before the finished file is moved into place the cached WhisperFactory
    ///     is released (it holds the ggml file open — the "file is in use" bug)
    ///     and the move itself retries, riding out a concurrently-scanning
    ///     antivirus.
    /// </summary>
    public static async Task<string?> DownloadAsync(
        Core.VoiceWhisper.WhisperModel model, IProgress<double>? progress, CancellationToken ct)
    {
        string finalPath = ModelPath(model.FileName);
        string partPath = ModelPath(model.FileName + ".part");
        try
        {
            Directory.CreateDirectory(ModelsDir);
            CleanStaleTemps();

            // a corrupt oversized .part (crash mid-write of a previous life) can't be trusted
            try
            {
                var junk = new FileInfo(partPath);
                if (junk.Exists && junk.Length > model.Bytes + model.Bytes / 10)
                {
                    junk.Delete();
                    DiagnosticLogger.Log("Voice", $"Discarded oversized partial download: {partPath}");
                }
            }
            catch { }

            string? err = null;
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                err = await DownloadAttemptAsync(model, partPath, progress, attempt, ct).ConfigureAwait(false);
                if (err is null) break;
                if (err == "cancelled") return "cancelled";
                if (attempt < MaxAttempts)
                {
                    DiagnosticLogger.Log("Voice", $"Whisper download attempt {attempt} failed ({err}) — retrying");
                    await Task.Delay(TimeSpan.FromMilliseconds(900 * attempt), ct).ConfigureAwait(false);
                }
            }
            if (err is not null) return err;

            // finalize: unlock the cached factory (it holds the ggml file open —
            // without this a re-install fails with "file is in use"), then move
            // with retries so a scanning antivirus can't wedge the rename
            ReleaseFactory();
            await Task.Run(() => MoveIntoPlace(partPath, finalPath), CancellationToken.None).ConfigureAwait(false);
            progress?.Report(1.0);
            var fi = new FileInfo(finalPath);
            DiagnosticLogger.Log("Voice", $"Whisper model installed: {model.FileName} ({fi.Length / 1_000_000.0:0} MB)");
            return null;
        }
        catch (OperationCanceledException)
        {
            return "cancelled";   // the .part stays — the next attempt resumes
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Voice.WhisperDownload", ex);
            return "Download failed — " + ex.Message;
        }
    }

    /// <summary>One streaming attempt; appends to the .part when the server honours ranges.</summary>
    private static async Task<string?> DownloadAttemptAsync(
        Core.VoiceWhisper.WhisperModel model, string partPath, IProgress<double>? progress, int attempt, CancellationToken ct)
    {
        long resumeFrom = 0;
        try
        {
            var existing = new FileInfo(partPath);
            if (existing.Exists) resumeFrom = existing.Length;   // every attempt resumes the same .part

            using var req = new HttpRequestMessage(HttpMethod.Get, model.Url);
            req.Headers.UserAgent.ParseAdd("Lumo-Launcher/2.6");
            if (resumeFrom > 0)
                req.Headers.Range = new RangeHeaderValue(resumeFrom, null);

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                // 416 = the .part already covers the whole file (or the server
                // disliked the range) — treat as "nothing to download", let the
                // size guard below decide whether the data is usable
                if (resp.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable && resumeFrom >= model.Bytes)
                    return null;
                return $"HTTP {(int)resp.StatusCode} from the model server";
            }

            bool resumed = resumeFrom > 0 &&
                           resp.StatusCode == System.Net.HttpStatusCode.PartialContent &&
                           resp.Content.Headers.ContentRange?.From == resumeFrom;
            if (!resumed) resumeFrom = 0;   // server ignored the range — start over

            long? total = resp.Content.Headers.ContentLength is { } cl ? cl + resumeFrom : model.Bytes;

            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(partPath, resumed ? FileMode.Append : FileMode.Create,
                                                 FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long written = resumeFrom;
            int read;
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                written += read;
                progress?.Report(total > 0 ? Math.Min(0.995, written / (double)total) : 0);
            }
            await dst.FlushAsync(ct).ConfigureAwait(false);

            // size guard: a truncated download must never look installed (the
            // .part is kept so the next attempt resumes instead of restarting)
            var fi = new FileInfo(partPath);
            if (fi.Length < model.Bytes * 9 / 10)
                return $"the file was incomplete ({fi.Length / 1_000_000:0} of {model.SizeLabel} — connection dropped?)";
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return ex.Message.Length > 120 ? ex.Message[..120] : ex.Message;
        }
    }

    /// <summary>Moves the finished .part over the final name, retrying past transient locks (antivirus).</summary>
    private static void MoveIntoPlace(string partPath, string finalPath)
    {
        for (int i = 0; ; i++)
        {
            try
            {
                File.Move(partPath, finalPath, overwrite: true);
                return;
            }
            catch (IOException) when (i < MoveRetries - 1)
            {
                Thread.Sleep(300);
            }
            catch (UnauthorizedAccessException) when (i < MoveRetries - 1)
            {
                Thread.Sleep(300);
            }
        }
    }

    /// <summary>Sweeps dl-*.tmp leftovers of the pre-resume scheme (alpha.5) after a crash.</summary>
    private static void CleanStaleTemps()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(ModelsDir, "dl-*.tmp"))
            {
                try { File.Delete(f); } catch { }
            }
        }
        catch { }
    }

    // ---------------------------------------------------------------- inference

    private static readonly object FactoryGate = new();
    private static Whisper.net.WhisperFactory? _factory;
    private static string? _factoryPath;

    /// <summary>
    /// v2.6.0-alpha.6 — disposes the cached WhisperFactory so the ggml model file
    /// is no longer held open. Called before a (re)download moves a fresh model
    /// file into place — the factory keeps the weights open for the app lifetime,
    /// which used to make a re-install fail with "file is in use". The next
    /// transcription transparently reloads the (fresh) model.
    /// </summary>
    public static void ReleaseFactory()
    {
        lock (FactoryGate)
        {
            try { _factory?.Dispose(); } catch { }
            _factory = null;
            _factoryPath = null;
        }
    }

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
