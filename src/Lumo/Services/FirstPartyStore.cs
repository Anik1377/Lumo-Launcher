using System.IO;
using System.Net.Http;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>
/// v2.6.0-alpha.2 — downloads the FIRST-PARTY plugin catalog and installs
/// plugins from it.
///
/// · FetchCatalogAsync()  → GET plugins/registry.json from the Lumo repo
///   (raw.githubusercontent.com), parsed by the pure Core/FirstParty model.
/// · InstallAsync(entry)  → GET the entry's plugin.json, VALIDATE it through
///   the same Plugins.TryParse the scanner uses (never write an unparseable
///   manifest), then write it atomically into
///   %APPDATA%\Lumo\plugins\<id>\plugin.json and rescan the store.
///
/// Everything is on-demand — nothing here runs at startup or on a keystroke —
/// and every failure comes back as a string, never a throw. One shared static
/// HttpClient (same doctrine as UpdateService); the UA carries the app version.
/// </summary>
public sealed class FirstPartyStore
{
    private static readonly HttpClient Http = CreateClient();

    /// <summary>One shared client for the whole app lifetime (same doctrine as UpdateService) —
    /// a honest User-Agent so GitHub's raw CDN never throttles us as a bot.</summary>
    private static HttpClient CreateClient()
    {
        var c = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        })
        { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Lumo-Launcher/" + AppVersion.Label);
        return c;
    }

    private readonly PluginStore _plugins;
    private readonly string _dir;

    public FirstPartyStore(PluginStore plugins, string? dir = null)
    {
        _plugins = plugins;
        _dir = dir ?? AppPaths.PluginsDir;
    }

    /// <summary>True when the catalog already lists this id AND its manifest parses locally.</summary>
    public bool IsInstalled(string id) => FindInstalled(id) is not null;

    /// <summary>The local PluginDefinition for an installed first-party id (null when absent).</summary>
    public PluginDefinition? FindInstalled(string id)
    {
        string safe = Plugins.SanitizeId(id);
        if (safe.Length == 0) return null;
        return _plugins.All().FirstOrDefault(p => p.Id.Equals(safe, StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ fetch

    public sealed record CatalogResult(bool Ok, List<FirstPartyEntry> Entries, string? Error)
    {
        public static CatalogResult Fail(string error) => new(false, new List<FirstPartyEntry>(), error);
    }

    /// <summary>Fetches + parses the official catalog. Never throws.</summary>
    public async Task<CatalogResult> FetchCatalogAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.GetAsync(FirstParty.RegistryUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return CatalogResult.Fail($"GitHub answered {(int)resp.StatusCode} — the catalog may be temporarily unreachable");

            byte[] bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length > FirstParty.MaxCatalogBytes)
                return CatalogResult.Fail($"catalog over {FirstParty.MaxCatalogBytes / 1024} KB — refusing to parse");

            string json = System.Text.Encoding.UTF8.GetString(bytes);
            if (!FirstParty.TryParseCatalog(json, out var entries, out var error))
                return CatalogResult.Fail(error ?? "catalog could not be parsed");

            DiagnosticLogger.Log("FirstParty", $"Catalog fetched: {entries.Count} plugin(s)");
            return new CatalogResult(true, entries, null);
        }
        catch (OperationCanceledException) { return CatalogResult.Fail("catalog fetch cancelled"); }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("FirstPartyStore.FetchCatalog", ex);
            return CatalogResult.Fail("could not reach GitHub — check your connection and try again");
        }
    }

    // ------------------------------------------------------------------ install

    public sealed record InstallResult(bool Ok, string? Error)
    {
        public static InstallResult Ok_() => new(true, null);
        public static InstallResult Fail(string error) => new(false, error);
    }

    /// <summary>
    /// Downloads one entry's plugin.json, validates it, writes it into the
    /// plugins folder and rescans. Re-installing over an existing plugin is
    /// the intended update path (same folder id → same manifest path).
    /// </summary>
    public async Task<InstallResult> InstallAsync(FirstPartyEntry entry, CancellationToken ct = default)
    {
        string id = Plugins.SanitizeId(entry.Id);
        if (id.Length == 0) return InstallResult.Fail($"'{entry.Id}' is not a usable plugin id");

        try
        {
            using var resp = await Http.GetAsync(entry.Url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return InstallResult.Fail($"download answered {(int)resp.StatusCode}");

            byte[] bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length == 0) return InstallResult.Fail("downloaded manifest is empty");
            if (bytes.Length > Plugins.MaxJsonBytes)
                return InstallResult.Fail($"manifest over {Plugins.MaxJsonBytes / 1024} KB — refusing to install");

            string json = System.Text.Encoding.UTF8.GetString(bytes);
            // validate BEFORE touching the disk — a half-working plugin row that
            // fails on Enter is worse than a refused install (same doctrine as scan)
            if (!Plugins.TryParse(json, id, out var parsed, out var error) || parsed is null)
                return InstallResult.Fail($"the manifest is not a valid plugin — {error}");

            WritePluginManifest(_dir, id, json);
            DiagnosticLogger.Log("FirstParty", $"Installed '{id}' v{parsed.Version} ({parsed.Commands.Count} command(s))");
            _plugins.Rescan();
            return InstallResult.Ok_();
        }
        catch (OperationCanceledException) { return InstallResult.Fail("install cancelled"); }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("FirstPartyStore.Install", ex);
            return InstallResult.Fail("download failed — check your connection and try again");
        }
    }

    /// <summary>
    /// Atomic-ish manifest write: unique tmp name in the SAME folder, then a
    /// File.Move with overwrite (the same contract Settings/ChatStore use so a
    /// crash mid-write can never leave a truncated plugin.json behind).
    /// Internal + static so the test harness can exercise the disk path.
    /// </summary>
    internal static void WritePluginManifest(string pluginsDir, string id, string json)
    {
        string folder = Path.Combine(pluginsDir, id);
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, Plugins.ManifestFile);
        string tmp = Path.Combine(folder, Plugins.ManifestFile + "." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(tmp, json);
        File.Move(tmp, target, overwrite: true);
    }
}
