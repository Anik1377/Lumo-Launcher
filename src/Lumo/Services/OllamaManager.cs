using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>
/// v2.3.0-alpha.2 — one-click local AI setup: detect Ollama, install it, pull
/// lightweight models (Llama 3.2, Qwen, Gemma, Phi, DeepSeek-R1 …) with live
/// progress, and manage what's on disk.
///
/// Design constraints (DEV_PLAN agent rules):
///  · NEVER on the UI thread — every network/process call here is awaited from
///    Task.Run in the settings window or launcher; the synchronous search
///    pipeline only ever reads the immutable <see cref="Current"/> snapshot.
///  · Bounded — one download / one pull at a time (the window enforces this),
///    the status snapshot carries at most 100 models, the catalog is fixed.
///  · Log, don't crash — every failure path is caught and returned as a value;
///    nothing here ever throws to the caller. No API keys are involved (Ollama
///    is local and keyless), so no redaction surface exists.
///  · Portable-exe promise — zero new NuGet/native dependencies: the official
///    OllamaSetup.exe is downloaded from ollama.com on the user's explicit
///    click and Lumo merely orchestrates it.
/// </summary>
public static class OllamaManager
{
    public const string InstallUrl = "https://ollama.com/download/OllamaSetup.exe";
    public const string InstallPage = "https://ollama.com/download";

    private static readonly HttpClient Http = new()
    {
        // Streams (a 2 GB model pull, a ~1 GB installer) must not be cut off by a
        // client timeout — callers own cancellation via CancellationToken instead.
        Timeout = Timeout.InfiniteTimeSpan,
    };

    // ---------------------------------------------------------------- model catalog

    /// <summary>A curated catalog row: id, one-line pitch, approximate download size in GB.</summary>
    public sealed record OllamaModel(string Id, string Blurb, double SizeGb);

    /// <summary>
    /// The "recommended lightweight models" list shown in Settings → AI.
    /// Sizes are approximate download sizes (quantized default tags) and are
    /// labelled "~" in the UI; Ollama reports exact bytes during the pull.
    /// Ordered smallest-first so the top row is always a safe default.
    /// </summary>
    public static readonly IReadOnlyList<OllamaModel> Catalog = new OllamaModel[]
    {
        new("qwen2.5:0.5b",    "Tiny and instant — good for short answers on any PC",        0.4),
        new("qwen2.5:1.5b",    "Strong small multilingual model from Alibaba",               1.0),
        new("deepseek-r1:1.5b","Reasoning model — thinks step by step, great at math",       1.1),
        new("llama3.2:1b",     "Meta's small Llama — the everyday default",                  1.3),
        new("gemma2:2b",       "Google's compact model — balanced and polite",               1.6),
        new("llama3.2:3b",     "Bigger Llama — noticeably smarter, still light",             2.0),
        new("phi3.5",          "Microsoft Phi — punchy reasoning for its size",              2.2),
        new("llama3.1:8b",     "The full Llama — needs ~8 GB RAM to feel good",              4.9),
    };

    // ---------------------------------------------------------------- pure parsers (test surface)

    /// <summary>One decoded NDJSON line from POST /api/pull (stream:true).</summary>
    public sealed record PullLine(string Status, string? Digest, long Total, long Completed, bool Done)
    {
        public static PullLine Empty { get; } = new("", null, 0, 0, false);
        public double Fraction => Total > 0 ? Math.Clamp((double)Completed / Total, 0, 1) : 0;
    }

    /// <summary>
    /// Parses one /api/pull NDJSON line. Tolerant: anything unexpected (empty,
    /// non-JSON, error payloads, shape changes) yields <see cref="PullLine.Empty"/>
    /// — the stream reader just keeps going. Done fires on status=="success".
    /// </summary>
    public static PullLine ParsePullLine(string? json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return PullLine.Empty;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return PullLine.Empty;

            string status = root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
                ? st.GetString() ?? "" : "";
            string? digest = root.TryGetProperty("digest", out var dg) && dg.ValueKind == JsonValueKind.String
                ? dg.GetString() : null;
            long total = root.TryGetProperty("total", out var tt) && tt.ValueKind == JsonValueKind.Number ? tt.GetInt64() : 0;
            long done = root.TryGetProperty("completed", out var cc) && cc.ValueKind == JsonValueKind.Number ? cc.GetInt64() : 0;

            bool success = status.Equals("success", StringComparison.OrdinalIgnoreCase);
            return new PullLine(status, digest, total, done, success);
        }
        catch { return PullLine.Empty; }
    }

    /// <summary>One installed model as reported by GET /api/tags.</summary>
    public sealed record ModelInfo(string Name, long Bytes);

    /// <summary>
    /// Parses a GET /api/tags response into (name, bytes) pairs. Tolerant:
    /// returns an empty list for anything unexpected. Capped at 100 entries —
    /// nobody has more models than that and a hostile payload can't balloon.
    /// </summary>
    public static List<ModelInfo> ParseTags(string? json)
    {
        var list = new List<ModelInfo>();
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return list;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("models", out var models) ||
                models.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var m in models.EnumerateArray())
            {
                if (list.Count >= 100) break;
                if (m.ValueKind != JsonValueKind.Object) continue;
                if (!m.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String) continue;
                long bytes = m.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
                list.Add(new ModelInfo(n.GetString() ?? "", bytes));
            }
        }
        catch { /* tolerant */ }
        return list;
    }

    /// <summary>
    /// True when the configured endpoint points at this PC (localhost / loopback /
    /// blank), i.e. the machine where a one-click install makes sense. A remote
    /// Ollama gateway must never be offered a local installer.
    /// </summary>
    public static bool IsLocalEndpoint(string? endpoint)
    {
        try
        {
            string s = (endpoint ?? "").Trim();
            if (s.Length == 0) return true;                     // blank = default localhost
            if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                s = "http://" + s;
            string host = new Uri(s).Host.ToLowerInvariant();
            return host is "localhost" or "127.0.0.1" or "::1" or "[::1]" or "0.0.0.0";
        }
        catch { return true; }   // unparseable → assume local, offer help
    }

    // ---------------------------------------------------------------- status snapshot

    /// <summary>Immutable probe result the synchronous pipeline can read freely.</summary>
    public sealed record OllamaStatus(
        bool Probed, bool Installed, bool ServerUp,
        IReadOnlyList<ModelInfo> Models, DateTime At, string Endpoint)
    {
        public static OllamaStatus Initial { get; } = new(false, false, false,
            Array.Empty<ModelInfo>(), DateTime.MinValue, "");
        public bool Stale => (DateTime.UtcNow - At).TotalSeconds > 90;
    }

    /// <summary>The latest probe result — replaced wholesale, never mutated, safe to read from any thread.</summary>
    public static OllamaStatus Current { get; private set; } = OllamaStatus.Initial;

    /// <summary>Forces the next RefreshStatusAsync to actually probe even if the last one is fresh.</summary>
    public static void Invalidate() => Current = Current with { At = DateTime.MinValue };

    /// <summary>
    /// Probes install state + server + model list (all best-effort) and publishes
    /// a fresh <see cref="Current"/>. Never throws; safe to fire-and-forget.
    /// </summary>
    public static async Task<OllamaStatus> RefreshStatusAsync(string? endpoint)
    {
        bool installed = IsInstalled();
        string baseUri = AiProviders.NormalizeBase(endpoint, anthropic: false);
        bool up = false;
        var models = new List<ModelInfo>();
        if (installed)
        {
            up = await ProbeServerUpAsync(baseUri).ConfigureAwait(false);
            if (up)
            {
                var (ok, json, _) = await GetJsonAsync(baseUri + "/api/tags", TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (ok) models = ParseTags(json);
            }
        }
        var status = new OllamaStatus(true, installed, up, models, DateTime.UtcNow, baseUri);
        Current = status;
        DiagnosticLogger.Log("Ollama", $"probed · installed={installed} up={up} models={models.Count}");
        return status;
    }

    /// <summary>GET {base}/api/version with a short timeout — "is the server alive?".</summary>
    public static async Task<bool> ProbeServerUpAsync(string baseUri)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var req = new HttpRequestMessage(HttpMethod.Get, baseUri + "/api/version");
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static async Task<(bool Ok, string Json, string Error)> GetJsonAsync(string url, TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return (resp.IsSuccessStatusCode, body, resp.IsSuccessStatusCode ? "" : $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    // ---------------------------------------------------------------- install state

    /// <summary>
    /// v3.0.0-alpha.5 — where the user asked Lumo to find/install Ollama
    /// (Settings → AI). Empty = the standard roots. Set once at startup from
    /// Settings.OllamaInstallDir and whenever the value changes in Settings;
    /// <see cref="ExePath"/> checks it before every other candidate.
    /// </summary>
    public static string CustomInstallDir { get; set; } = "";

    /// <summary>Where ollama.exe lives, or null. The custom install dir first, then the standard roots + PATH — no registry, no throw.</summary>
    public static string? ExePath
    {
        get
        {
            try
            {
                if (!OperatingSystem.IsWindows()) return null;

                // v3.0.0-alpha.5 — the user-chosen install location wins
                string custom = (CustomInstallDir ?? "").Trim();
                if (custom.Length > 0)
                {
                    string customExe = custom.EndsWith("ollama.exe", StringComparison.OrdinalIgnoreCase)
                        ? custom
                        : Path.Combine(custom, "ollama.exe");
                    if (File.Exists(customExe)) return customExe;
                }

                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var candidates = new[]
                {
                    Path.Combine(local, "Programs", "Ollama", "ollama.exe"),
                    Path.Combine(pf, "Ollama", "ollama.exe"),
                };
                foreach (var c in candidates)
                    if (File.Exists(c)) return c;

                // fall back to PATH (scoop/choco/custom installs)
                var pathEnv = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var dir in pathEnv)
                {
                    try { if (File.Exists(Path.Combine(dir.Trim(), "ollama.exe"))) return Path.Combine(dir.Trim(), "ollama.exe"); }
                    catch { /* skip bad entry */ }
                }
            }
            catch (Exception ex) { DiagnosticLogger.LogException("Ollama.ExePath", ex); }
            return null;
        }
    }

    public static bool IsInstalled() => ExePath is not null;

    // ---------------------------------------------------------------- download + install

    /// <summary>
    /// Downloads the official OllamaSetup.exe to %TEMP%\Lumo\. Reports (doneBytes,
    /// totalBytes) on a worker thread. Returns the file path, or null on failure.
    /// </summary>
    public static async Task<string?> DownloadInstallerAsync(Action<long, long>? progress, CancellationToken ct)
    {
        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "Lumo");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "OllamaSetup.exe");

            DiagnosticLogger.Log("Ollama", "downloading installer from ollama.com");
            using var req = new HttpRequestMessage(HttpMethod.Get, InstallUrl);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                try { progress?.Invoke(done, total); } catch { /* progress must not kill the download */ }
            }

            DiagnosticLogger.Log("Ollama", $"installer downloaded · {done / (1024.0 * 1024.0):F0} MB → {file}");
            return File.Exists(file) && new FileInfo(file).Length > 0 ? file : null;
        }
        catch (OperationCanceledException)
        {
            DiagnosticLogger.Log("Ollama", "installer download cancelled");
            return null;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Ollama.DownloadInstaller", ex);
            return null;
        }
    }

    /// <summary>
    /// v3.0.0-alpha.5 — the silent Inno Setup argument line, extracted pure for
    /// the test harness. An empty/whitespace dir yields the classic flags; a
    /// custom dir appends /DIR="…" (quoted — spaces are legal in paths).
    /// </summary>
    public static string BuildInstallArgs(string? installDir)
    {
        string baseArgs = "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS";
        string dir = (installDir ?? "").Trim();
        return dir.Length == 0 ? baseArgs : $"{baseArgs} /DIR=\"{dir}\"";
    }

    /// <summary>
    /// Runs the downloaded installer silently (Inno Setup flags) and waits for it
    /// to finish. Returns true when the exit code is 0. Can take a couple of
    /// minutes — the caller keeps the UI alive with a status line.
    /// v3.0.0-alpha.5 — <paramref name="installDir"/> (optional) installs Ollama
    /// to that folder via the Inno Setup /DIR flag instead of the default.
    /// </summary>
    public static async Task<bool> RunInstallerAsync(string installerPath, CancellationToken ct, string? installDir = null)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return false;
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = BuildInstallArgs(installDir),
                UseShellExecute = true,
            };
            string dirArg = (installDir ?? "").Trim();
            DiagnosticLogger.Log("Ollama", $"running OllamaSetup.exe (silent){(dirArg.Length > 0 ? $" → {dirArg}" : "")}");
            using var p = Process.Start(psi);
            if (p is null) return false;
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            DiagnosticLogger.Log("Ollama", $"installer exit code {p.ExitCode}");
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Ollama.RunInstaller", ex);
            return false;
        }
    }

    /// <summary>Fire-and-forget "start the local server" for the installed-but-down case.</summary>
    public static bool StartServer()
    {
        try
        {
            string? exe = ExePath;
            if (exe is null) return false;
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "serve",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };
            Process.Start(psi);
            DiagnosticLogger.Log("Ollama", "serve process started");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Ollama.StartServer", ex);
            return false;
        }
    }

    // ---------------------------------------------------------------- pull / delete models

    /// <summary>Live per-layer pull progress aggregated across layers.</summary>
    public sealed record PullProgress(double Fraction, long DoneBytes, long TotalBytes, string Status, bool Finished, bool Ok, string Error);

    /// <summary>
    /// Pulls a model (POST /api/pull, stream:true) and reports GLOBAL progress —
    /// Ollama streams per-layer totals, so lines are bucketed by digest and summed
    /// to give one honest percentage across all layers. Returns Ok=false with a
    /// short human reason on any failure. Never throws.
    /// </summary>
    public static async Task<PullProgress> PullAsync(string? endpoint, string model, Action<PullProgress>? progress, CancellationToken ct)
    {
        string baseUri;
        try { baseUri = AiProviders.NormalizeBase(endpoint, anthropic: false); }
        catch (Exception ex) { return new PullProgress(0, 0, 0, "", true, false, ex.Message); }

        if (string.IsNullOrWhiteSpace(model))
            return new PullProgress(0, 0, 0, "", true, false, "no model name given");

        try
        {
            string body = JsonSerializer.Serialize(new { model = model.Trim(), stream = true });
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUri + "/api/pull");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            DiagnosticLogger.Log("Ollama", $"pull {model} from {baseUri}");

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string err = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                string reason = $"HTTP {(int)resp.StatusCode} · {Shorten(err)}";
                DiagnosticLogger.Log("Ollama", $"pull {model} failed: {reason}");
                return new PullProgress(0, 0, 0, "", true, false, reason);
            }

            // digest → (total, completed): sum across layers for one global fraction
            var layers = new Dictionary<string, (long Total, long Done)>(StringComparer.Ordinal);
            long grandTotal = 0;
            string lastStatus = "";

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                var pl = ParsePullLine(line);
                if (pl.Status.Length > 0) lastStatus = pl.Status;
                if (pl.Done)
                {
                    progress?.Invoke(new PullProgress(1, grandTotal, grandTotal, "success", true, true, ""));
                    DiagnosticLogger.Log("Ollama", $"pull {model} done");
                    return new PullProgress(1, grandTotal, grandTotal, "success", true, true, "");
                }
                if (pl.Digest is not null && pl.Total > 0)
                {
                    layers[pl.Digest] = (pl.Total, Math.Max(pl.Completed, 0));
                    grandTotal = layers.Values.Sum(t => t.Total);
                }
                else if (pl.Digest is not null && layers.TryGetValue(pl.Digest, out var cur) && pl.Completed > cur.Done)
                {
                    layers[pl.Digest] = (cur.Total, pl.Completed);
                }

                long grandDone = layers.Values.Sum(t => Math.Min(t.Done, t.Total));
                double frac = grandTotal > 0 ? Math.Clamp((double)grandDone / grandTotal, 0, 1) : 0;
                try { progress?.Invoke(new PullProgress(frac, grandDone, grandTotal, lastStatus, false, true, "")); }
                catch { /* progress must not kill the pull */ }
            }

            // stream ended without an explicit success line — treat as failure
            string endReason = string.IsNullOrWhiteSpace(lastStatus) ? "the pull ended unexpectedly" : $"pull ended: {lastStatus}";
            DiagnosticLogger.Log("Ollama", $"pull {model}: {endReason}");
            return new PullProgress(0, 0, 0, lastStatus, true, false, endReason);
        }
        catch (OperationCanceledException)
        {
            DiagnosticLogger.Log("Ollama", $"pull {model} cancelled");
            return new PullProgress(0, 0, 0, "", true, false, "cancelled");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Ollama.Pull", ex);
            return new PullProgress(0, 0, 0, "", true, false, ex.Message);
        }
    }

    /// <summary>DELETE /api/delete — removes a model from disk. Never throws.</summary>
    public static async Task<(bool Ok, string Error)> DeleteModelAsync(string? endpoint, string model)
    {
        try
        {
            string baseUri = AiProviders.NormalizeBase(endpoint, anthropic: false);
            using var req = new HttpRequestMessage(HttpMethod.Delete, baseUri + "/api/delete");
            req.Content = new StringContent(JsonSerializer.Serialize(new { model }), Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                DiagnosticLogger.Log("Ollama", $"deleted {model}");
                return (true, "");
            }
            string err = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            string reason = $"HTTP {(int)resp.StatusCode} · {Shorten(err)}";
            DiagnosticLogger.Log("Ollama", $"delete {model} failed: {reason}");
            return (false, reason);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Ollama.Delete", ex);
            return (false, ex.Message);
        }
    }

    // ---------------------------------------------------------------- v3.0.0-alpha.5 — model storage location

    /// <summary>The stock Ollama model folder on Windows (what Ollama uses when OLLAMA_MODELS is unset).</summary>
    public static string DefaultModelsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ollama", "models");

    /// <summary>
    /// The Ollama models folder, resolved the way Ollama itself resolves it:
    /// the OLLAMA_MODELS environment variable wins (user, then machine), the
    /// stock %LOCALAPPDATA%\Ollama\models is the fallback. Pure in its inputs —
    /// the live <see cref="ModelsDir"/> wrapper feeds it the real values, and
    /// the test harness feeds it synthetic ones.
    /// </summary>
    public static string ResolveModelsDir(string? userEnv, string? machineEnv, string localAppData)
    {
        string user = (userEnv ?? "").Trim();
        if (user.Length > 0) return user;
        string machine = (machineEnv ?? "").Trim();
        if (machine.Length > 0) return machine;
        return Path.Combine(localAppData, "Ollama", "models");
    }

    /// <summary>The models folder Ollama is actually serving from right now.</summary>
    public static string ModelsDir =>
        OperatingSystem.IsWindows()
            ? ResolveModelsDir(
                Environment.GetEnvironmentVariable("OLLAMA_MODELS", EnvironmentVariableTarget.User),
                Environment.GetEnvironmentVariable("OLLAMA_MODELS", EnvironmentVariableTarget.Machine),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
            : ResolveModelsDir(Environment.GetEnvironmentVariable("OLLAMA_MODELS"), null,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>
    /// Bounded recursive byte count of a folder. Skips unreadable entries,
    /// stops after <paramref name="maxFiles"/> (a hostile path can't hang the
    /// UI), returns 0 for a missing folder. Never throws.
    /// </summary>
    public static long FolderBytes(string? path, int maxFiles = 20_000)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return 0;
            long total = 0;
            int seen = 0;
            var stack = new Stack<string>();
            stack.Push(path);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir))
                    {
                        if (++seen > maxFiles) return total;
                        try { total += new FileInfo(f).Length; } catch { /* unreadable file — skip */ }
                    }
                    foreach (var d in Directory.EnumerateDirectories(dir)) stack.Push(d);
                }
                catch { /* unreadable dir — skip */ }
            }
            return total;
        }
        catch { return 0; }
    }

    /// <summary>True when OLLAMA_MODELS points somewhere other than the stock folder.</summary>
    public static bool ModelsDirIsCustom =>
        !string.Equals(ModelsDir, DefaultModelsDir, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Points Ollama at a new models folder: validates it (creating is fine),
    /// persists OLLAMA_MODELS as a USER environment variable (the documented
    /// Ollama mechanism — Lumo stores nothing), then restarts the local server
    /// so the change is live without a reboot. Windows only (the env-var target
    /// does not exist on other platforms). Returns (ok, short human error).
    /// </summary>
    public static async Task<(bool Ok, string Error)> SetModelsDirAsync(string? newDir, CancellationToken ct)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return (false, "model storage moves are a Windows-only operation");

            string dir = (newDir ?? "").Trim().TrimEnd(Path.DirectorySeparatorChar);
            if (dir.Length == 0) return (false, "pick a folder first");
            if (!Path.IsPathRooted(dir)) return (false, "the folder must be an absolute path");
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex) { return (false, $"can't create that folder: {ex.Message}"); }

            string previous = ModelsDir;
            Environment.SetEnvironmentVariable("OLLAMA_MODELS", dir, EnvironmentVariableTarget.User);
            DiagnosticLogger.Log("Ollama", $"OLLAMA_MODELS → {dir} (was {previous}); restarting the server");

            bool up = await RestartServerAsync(ct).ConfigureAwait(false);
            return up
                ? (true, "")
                : (false, "the env var is set, but Ollama didn't come back up — start it once manually and press Refresh");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Ollama.SetModelsDir", ex);
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Restarts the local Ollama server: stops every ollama process this install
    /// owns (the tray app spawns the server, so both go down), starts a fresh
    /// `ollama serve`, and polls the port for up to ~12 s. Never throws.
    /// </summary>
    public static async Task<bool> RestartServerAsync(CancellationToken ct)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return false;
            string? exe = ExePath;
            if (exe is null) return false;

            // stop the running instances — "ollama" catches both ollama.exe (the
            // server) and "ollama app.exe" (the tray that spawned it)
            int killed = 0;
            foreach (var p in Process.GetProcessesByName("ollama"))
            {
                try { p.Kill(entireProcessTree: true); killed++; } catch { /* already gone / access denied */ }
                try { p.Dispose(); } catch { }
            }
            if (killed > 0)
            {
                DiagnosticLogger.Log("Ollama", $"stopped {killed} running ollama process(es)");
                await Task.Delay(1200, ct).ConfigureAwait(false);   // let the port actually free up
            }

            StartServer();

            // poll the version endpoint — the server needs a beat to bind
            for (int i = 0; i < 12; i++)
            {
                if (ct.IsCancellationRequested) return false;
                string baseUri = "";
                try { baseUri = AiProviders.NormalizeBase("", anthropic: false); } catch { }
                if (await ProbeServerUpAsync(baseUri).ConfigureAwait(false))
                {
                    DiagnosticLogger.Log("Ollama", "server is back up after restart");
                    return true;
                }
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
            DiagnosticLogger.Log("Ollama", "server did not come back within 12 s");
            return false;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Ollama.RestartServer", ex);
            return false;
        }
    }

    private static string Shorten(string s) => s.Length <= 160 ? s : s[..160] + "…";
}
