namespace Lumo.Core;

/// <summary>
/// v2.1 (DEV_PLAN Task 1.3) — per-query web-engine quick-switch.
///
/// The first word of a W/ query may name a provider: "W/github dotnet",
/// "W/youtube cats", "W/ddg lumo launcher"… The rest of the query is searched
/// on THAT provider while the global default engine stays untouched.
///
/// Adding a provider = one dictionary entry:
///     ["gnews"] = "https://news.google.com/search?q={0}",
/// ({0} receives the URL-escaped remainder of the query.)
/// User-defined providers from Settings.CustomWebProviders win over built-ins.
/// </summary>
public static class WebProviders
{
    public static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["github"]  = "https://github.com/search?q={0}",
        ["youtube"] = "https://www.youtube.com/results?search_query={0}",
        ["ddg"]     = "https://duckduckgo.com/?q={0}",
        ["duckduckgo"] = "https://duckduckgo.com/?q={0}",
        ["bing"]    = "https://www.bing.com/search?q={0}",
        ["google"]  = "https://www.google.com/search?q={0}",
        ["maps"]    = "https://www.google.com/maps/search/{0}",
        ["wiki"]    = "https://en.wikipedia.org/w/index.php?search={0}",
        ["images"]  = "https://www.google.com/search?tbm=isch&q={0}",
        ["scholar"] = "https://scholar.google.com/scholar?q={0}",
        ["news"]    = "https://news.google.com/search?q={0}",
        ["amazon"]  = "https://www.amazon.com/s?k={0}",
        ["npm"]     = "https://www.npmjs.com/search?q={0}",
        ["nuget"]   = "https://www.nuget.org/packages?q={0}",
        ["so"]      = "https://stackoverflow.com/search?q={0}",
        ["stackoverflow"] = "https://stackoverflow.com/search?q={0}",
    };

    /// <summary>
    /// Splits "github dotnet 8" → ("github", "dotnet 8") and builds the provider URL.
    /// Returns false when the first token is not a provider keyword (then the caller
    /// falls back to the configured default engine). Custom providers win.
    /// </summary>
    public static bool TryResolve(string query, IReadOnlyDictionary<string, string>? custom, out string url, out string keyword, out string rest)
    {
        url = ""; keyword = ""; rest = query;
        try
        {
            if (string.IsNullOrWhiteSpace(query)) return false;
            int sp = query.IndexOf(' ');
            string first = sp < 0 ? query : query[..sp];
            string remainder = sp < 0 ? "" : query[(sp + 1)..].Trim();

            string? template = null;
            if (custom is { Count: > 0 } &&
                custom.TryGetValue(first, out var customTemplate))
                template = customTemplate;
            else if (Map.TryGetValue(first, out var builtIn))
                template = builtIn;

            if (template is null) return false;
            if (remainder.Length == 0) return false;   // "W/github" alone → default engine hint path

            url = string.Format(template, Uri.EscapeDataString(remainder));
            keyword = first;
            rest = remainder;
            return true;
        }
        catch { return false; }
    }
}
