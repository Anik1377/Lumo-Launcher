using System.Text.Json;

namespace Lumo.Core;

/// <summary>
/// v2.6.0-alpha.2 — the FIRST-PARTY plugin catalog.
///
/// The catalog lives in the Lumo repo (plugins/registry.json) and is fetched
/// from raw.githubusercontent.com on demand — never at startup, never in the
/// keystroke path. Each entry names one declarative plugin the app can
/// download (its plugin.json) straight into the user's plugins folder.
///
/// Pure model: parsing, validation and install-state comparison live here so
/// the whole catalog pipeline is unit-testable without any network or disk.
/// </summary>
public sealed record FirstPartyEntry
{
    public string Id { get; init; } = "";          // folder name → plugin id (sanitized before use)
    public string Name { get; init; } = "";        // display name ("" → id)
    public string Description { get; init; } = ""; // one line for the in-app browser
    public string Author { get; init; } = "";
    public string Version { get; init; } = "";
    public string Url { get; init; } = "";         // https URL of the plugin.json payload
}

/// <summary>Install state of one catalog entry against the local plugin folder.</summary>
public enum FirstPartyState
{
    Missing,    // not installed → the row says "Install"
    Same,       // installed at the same version → "Reinstall"
    Older,      // installed version is OLDER than the catalog → "Reinstall" (an update)
    Newer,      // installed version is NEWER than the catalog (local edit) → "Reinstall"
    Different,  // both installed, versions not comparable → "Reinstall"
}

public static class FirstParty
{
    /// <summary>The official catalog — served from the repo's plugins/ folder.</summary>
    public const string RegistryUrl =
        "https://raw.githubusercontent.com/Anik1377/Lumo-Launcher/main/plugins/registry.json";

    /// <summary>The full plugin-authoring guide (linked from Settings → Plugins).</summary>
    public const string DocsUrl =
        "https://github.com/Anik1377/Lumo-Launcher/blob/main/docs/PLUGIN_DEVELOPMENT.md";

    public const int MaxCatalogBytes = 256 * 1024;   // registry.json cap — way past any sane catalog
    public const int MaxEntries = 128;               // first-party entries are curated; 128 is generous

    /// <summary>
    /// Parses a registry.json. Tolerant at the file level (error string, never a
    /// throw) and strict about what becomes a row: every entry needs an id and an
    /// https:// url; duplicate ids keep their FIRST occurrence; everything is
    /// clipped to the same display caps the rest of the UI uses.
    /// </summary>
    public static bool TryParseCatalog(string json, out List<FirstPartyEntry> entries, out string? error)
    {
        entries = new List<FirstPartyEntry>();
        error = null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { error = $"invalid JSON — {ex.Message}"; return false; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("plugins", out var listEl) ||
                listEl.ValueKind != JsonValueKind.Array)
            {
                error = "registry.json must be an object with a \"plugins\" array";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in listEl.EnumerateArray())
            {
                if (entries.Count >= MaxEntries) break;
                if (el.ValueKind != JsonValueKind.Object) continue;   // tolerate junk rows

                // ids become FOLDER names at install time — sanitize exactly the
                // way Plugins.SanitizeId will, and skip ids that sanitize to nothing
                string id = Plugins.SanitizeId(Str(el, "id"));
                if (id.Length == 0) continue;
                string url = Str(el, "url");
                if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;

                if (!seen.Add(id)) continue;                          // first entry owns the id

                entries.Add(new FirstPartyEntry
                {
                    Id = id,
                    Name = Clip(Str(el, "name"), 60),
                    Description = Clip(Str(el, "description"), 160),
                    Author = Clip(Str(el, "author"), 40),
                    Version = Clip(Str(el, "version"), 20),
                    Url = url,
                });
            }

            return true;
        }
    }

    /// <summary>
    /// Compares a catalog entry against the locally installed plugin. Version
    /// strings are compared as System.Version when both parse ("1.0.0" vs
    /// "1.1.0" → Older/Newer); anything else falls back to Same on an exact
    /// match and Different otherwise. Never throws.
    /// </summary>
    public static FirstPartyState StateFor(FirstPartyEntry entry, PluginDefinition? installed)
    {
        if (installed is null) return FirstPartyState.Missing;
        if (string.Equals(entry.Version, installed.Version, StringComparison.OrdinalIgnoreCase))
            return FirstPartyState.Same;

        if (Version.TryParse(entry.Version, out var a) && Version.TryParse(installed.Version, out var b))
        {
            if (a < b) return FirstPartyState.Newer;
            if (a > b) return FirstPartyState.Older;
            return FirstPartyState.Same;
        }
        return FirstPartyState.Different;
    }

    /// <summary>Button label for a row — the only place that knows this policy.</summary>
    public static string ButtonLabel(FirstPartyState state) => state switch
    {
        FirstPartyState.Missing => "Install",
        _ => "Reinstall",
    };

    private static string Str(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            return s ?? "";
        }
        return "";
    }

    private static string Clip(string s, int n) => s.Length <= n ? s : s[..n].TrimEnd() + "…";
}
