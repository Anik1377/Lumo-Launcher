using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>
/// Auto-update service (v2.6 — DEV_PLAN Task 5.1): checks GitHub Releases for a
/// newer Lumo build and stages the zip download locally. STAGED on purpose —
/// Lumo is a portable single exe, so "install" means the user extracts the new
/// Lumo.exe over the old one; we never self-replace a running exe.
///
/// Every Lumo release is a prerelease (ALPHA policy), so the /releases list is
/// polled (NOT /latest, which excludes prereleases) and the pure
/// UpdateCheck.SelectNewest picks the winner. The check is bounded and quiet:
/// at most once per 24 h automatically, always logged, never throws.
/// </summary>
public sealed class UpdateService
{
    private readonly Settings _settings;
    private static readonly HttpClient Http = CreateClient();

    /// <summary>Raised after a successful check that found a newer version.
    /// May fire from a background thread — marshal before touching UI.</summary>
    public event Action<UpdateInfo>? UpdateAvailable;

    /// <summary>Most recent newer release found by any successful check this
    /// session (null = none yet) — Settings → About reads this on open.</summary>
    public UpdateInfo? Latest { get; private set; }

    public UpdateService(Settings settings) => _settings = settings;

    // ---------------------------------------------------------------- check

    /// <summary>True when an automatic check is due: enabled by the user and the
    /// last successful check (if any) is ≥ 24 h old. Pure — testable.</summary>
    public static bool AutoCheckDue(Settings s, DateTimeOffset nowUtc)
    {
        if (!s.UpdatesEnabled) return false;
        if (string.IsNullOrWhiteSpace(s.LastUpdateCheckUtc)) return true;
        try
        {
            var last = DateTimeOffset.Parse(
                s.LastUpdateCheckUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            return nowUtc - last >= TimeSpan.FromHours(24);
        }
        catch { return true; }   // unparsable stamp → check again, then overwrite it
    }

    /// <summary>
    /// One live check against GitHub. Persists the check timestamp (success or
    /// HTTP failure alike — a 403 rate-limit should not re-strike every launch),
    /// raises UpdateAvailable when something newer exists, and returns what it
    /// found (null = up to date / unreachable — the log tells those apart).
    /// </summary>
    public async Task<UpdateInfo?> CheckNowAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, UpdateCheck.ReleasesApiUrl);
            req.Headers.UserAgent.ParseAdd($"Lumo-Launcher/{AppVersion.Label}");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            DiagnosticLogger.Log("Updates", $"Check: HTTP {(int)resp.StatusCode}, {body.Length} B");
            if (!resp.IsSuccessStatusCode) return null;

            var found = UpdateCheck.SelectNewest(body, AppVersion.Label);
            StampCheck();
            if (found is not null)
            {
                Latest = found;
                DiagnosticLogger.Log("Updates", $"Update available: {found.Version} ({found.ZipBytes} B zip)");
                try { UpdateAvailable?.Invoke(found); } catch (Exception ex) { DiagnosticLogger.LogException("Updates.Event", ex); }
            }
            else DiagnosticLogger.Log("Updates", $"Up to date (running {AppVersion.Label})");
            return found;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Updates.CheckNow", ex);
            StampCheck();
            return null;
        }
    }

    private void StampCheck()
    {
        try
        {
            _settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            _settings.Save();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Updates.Stamp", ex); }
    }

    // ---------------------------------------------------------------- download

    /// <summary>
    /// Stage the update zip into <see cref="AppPaths.UpdatesDir"/> ("Lumo-launcher-&lt;ver&gt;.zip").
    /// Streams to a uniquely-named temp file first (a killed download must never
    /// leave a plausible-looking half zip), enforces the 80 MB sanity cap, then
    /// moves into place. Returns the final path, or null on failure (logged).
    /// </summary>
    public async Task<string?> DownloadAsync(UpdateInfo info, Action<double>? progress, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.UpdatesDir);
            var finalPath = Path.Combine(AppPaths.UpdatesDir, $"Lumo-launcher-v{info.Version}.zip");
            var tmpPath = Path.Combine(AppPaths.UpdatesDir, $"download-{Guid.NewGuid():N}.tmp");

            using (var req = new HttpRequestMessage(HttpMethod.Get, info.ZipUrl))
            {
                req.Headers.UserAgent.ParseAdd($"Lumo-Launcher/{AppVersion.Label}");
                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();

                long? total = info.ZipBytes > 0 ? info.ZipBytes : resp.Content.Headers.ContentLength;
                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);

                var buffer = new byte[64 * 1024];
                long written = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > UpdateCheck.MaxZipBytes)
                        throw new IOException($"Update zip exceeds the {UpdateCheck.MaxZipBytes / (1024 * 1024)} MB sanity cap");
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    if (total is > 0) progress?.Invoke(Math.Clamp(written * 100.0 / total.Value, 0, 100));
                }
            }

            File.Move(tmpPath, finalPath, overwrite: true);
            DiagnosticLogger.Log("Updates", $"Staged {finalPath}");
            progress?.Invoke(100);
            return finalPath;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Updates.Download", ex);
            progress?.Invoke(-1);
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        })
        { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return c;
    }
}
