using System.Text.Json;

namespace Lumo.Core;

/// <summary>
/// v2.5 (DEV_PLAN Task 4.2) — the pure plugin model.
///
/// A plugin is a FOLDER under %APPDATA%\Lumo\plugins\&lt;id&gt;\ holding a
/// plugin.json. Plugins are declarative JSON only — no code, no DLLs — so the
/// single-portable-exe promise and the "no untrusted code execution" stance
/// both survive intact. A command turns a typed keyword into one of three
/// actions: open a web URL, open a path/URL, or copy text — all with an
/// optional {query} placeholder.
/// </summary>
public sealed class PluginCommand
{
    public string Keyword { get; init; } = "";     // 1–24 chars, a-z 0-9 '-' (normalized)
    public string Name { get; init; } = "";        // row title ("" → keyword shown)
    public string Subtitle { get; init; } = "";    // row subtitle ("" → type description)
    public string Glyph { get; init; } = "";       // row glyph ("" → "P")
    public string Type { get; init; } = "web";     // web | open | copy
    public string Template { get; init; } = "";    // web/open target — {query} placeholder
    public string Text { get; init; } = "";        // copy payload — {query} placeholder
    public bool ArgOptional { get; init; }         // bare "kw" runs instead of asking for a query

    public string TypeName => Type.ToLowerInvariant() switch
    {
        "open" => "Opens",
        "copy" => "Copies",
        _ => "Searches",
    };
}

/// <summary>A record so the store can cheaply re-emit a plugin with deduped commands (with-expression).</summary>
public sealed record PluginDefinition
{
    public string Id { get; init; } = "";          // folder name, sanitized
    public string Name { get; init; } = "";        // display name ("" → id)
    public string Author { get; init; } = "";
    public string Version { get; init; } = "";
    public IReadOnlyList<PluginCommand> Commands { get; init; } = Array.Empty<PluginCommand>();
}

/// <summary>Pure parsing/validation/expansion — no disk I/O, fully unit-testable.</summary>
public static class Plugins
{
    public const int MaxPlugins = 64;
    public const int MaxCommandsPerPlugin = 24;
    public const int MaxJsonBytes = 64 * 1024;
    public const int MaxTemplateLength = 2000;
    public const int MaxTextLength = 4000;

    public const string ManifestFile = "plugin.json";

    /// <summary>
    /// Parses one plugin.json. Tolerant at the file level (returns an error string
    /// instead of throwing) but strict about semantics: a command without a valid
    /// keyword or without the payload its type needs is rejected — a half-working
    /// plugin row that fails on Enter is worse than a logged parse error.
    /// </summary>
    public static bool TryParse(string json, string folderId, out PluginDefinition? definition, out string? error)
    {
        definition = null;
        error = null;

        string id = SanitizeId(folderId);
        if (id.Length == 0) { error = "folder name is not a usable plugin id"; return false; }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { error = $"invalid JSON — {ex.Message}"; return false; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = "plugin.json must be a JSON object"; return false; }

            string name = Str(root, "name") ?? "";
            string author = Str(root, "author") ?? "";
            string version = Str(root, "version") ?? "";

            if (!root.TryGetProperty("commands", out var cmdsEl) || cmdsEl.ValueKind != JsonValueKind.Array)
            { error = "missing \"commands\" array"; return false; }

            var commands = new List<PluginCommand>();
            foreach (var c in cmdsEl.EnumerateArray())
            {
                if (commands.Count >= MaxCommandsPerPlugin) { error = $"more than {MaxCommandsPerPlugin} commands"; return false; }
                if (c.ValueKind != JsonValueKind.Object) { error = "a command entry is not an object"; return false; }

                string rawType = Str(c, "type");
                string type = (rawType.Length == 0 ? "web" : rawType).ToLowerInvariant();   // "type" is optional — default web
                if (type is not ("web" or "open" or "copy")) { error = $"command type '{type}' is not web/open/copy"; return false; }

                string rawKeyword = Str(c, "keyword") ?? "";
                if (!TryNormalizeKeyword(rawKeyword, out string keyword))
                { error = $"command keyword '{rawKeyword}' must be 1–24 chars: a-z 0-9 '-"; return false; }

                string template = Str(c, "template") ?? "";
                string text = Str(c, "text") ?? "";
                if (template.Length > MaxTemplateLength) { error = "template too long (max 2000 chars)"; return false; }
                if (text.Length > MaxTextLength) { error = "text too long (max 4000 chars)"; return false; }

                bool hasPayload = type == "copy" ? text.Length > 0 : template.Length > 0;
                if (!hasPayload) { error = $"command '{keyword}' ({type}) needs a {(type == "copy" ? "text" : "template")}"; return false; }

                commands.Add(new PluginCommand
                {
                    Keyword = keyword,
                    Name = Clip(Str(c, "name") ?? "", 60),
                    Subtitle = Clip(Str(c, "subtitle") ?? "", 120),
                    Glyph = Clip(Str(c, "glyph") ?? "", 4),
                    Type = type,
                    Template = template,
                    Text = text,
                    ArgOptional = c.TryGetProperty("argOptional", out var ao) && ao.ValueKind == JsonValueKind.True,
                });
            }

            if (commands.Count == 0) { error = "no commands defined"; return false; }

            definition = new PluginDefinition
            {
                Id = id,
                Name = Clip(name, 60),
                Author = Clip(author, 60),
                Version = Clip(version, 20),
                Commands = commands,
            };
            return true;
        }
    }

    /// <summary>Folder names become ids: lowercase, spaces→'-', keep a-z 0-9 '-' (max 40 chars).</summary>
    public static string SanitizeId(string folderName)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in (folderName ?? "").Trim().ToLowerInvariant())
        {
            if (c == ' ') sb.Append('-');
            else if (char.IsAsciiLetterOrDigit(c) || c == '-') sb.Append(c);
            if (sb.Length >= 40) break;
        }
        // collapse runs and trim edges, exactly like TryNormalizeKeyword — no '--' ids
        string id = sb.ToString();
        while (id.Contains("--")) id = id.Replace("--", "-");
        return id.Trim('-');
    }

    /// <summary>Keywords: lowercase, 1–24 chars of a-z 0-9 '-'; no leading/trailing/consecutive '-'.</summary>
    public static bool TryNormalizeKeyword(string keyword, out string normalized)
    {
        normalized = (keyword ?? "").Trim().ToLowerInvariant().Replace(' ', '-');
        while (normalized.Contains("--")) normalized = normalized.Replace("--", "-");
        normalized = normalized.Trim('-');
        if (normalized.Length is < 1 or > 24) return false;
        return normalized.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }

    /// <summary>Substitutes {query}; escaping is the caller's job (web rows URL-escape, open/copy stay raw).</summary>
    public static string Expand(string templateOrText, string arg) =>
        (templateOrText ?? "").Replace("{query}", arg ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when this query routes to the keyword: the whole query IS the keyword,
    /// or the keyword followed by a space (an app literally named "gh" keeps winning
    /// for "gh" + more typing — only the full token routes to the plugin).
    /// </summary>
    public static bool KeywordRoutes(string query, string keyword)
    {
        if (query.Length == 0 || keyword.Length == 0) return false;
        return query.Equals(keyword, StringComparison.OrdinalIgnoreCase)
               || (query.Length > keyword.Length
                   && query.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
                   && query[keyword.Length] == ' ');
    }

    public static string TypeDescription(PluginCommand c) => c.Type.ToLowerInvariant() switch
    {
        "open" => "opens " + Clip(c.Template, 80),
        "copy" => "copies text to the clipboard",
        _ => "web search",
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

    /// <summary>The exact JSON the launcher copies for new plugin authors (P/ → "starter").</summary>
    public const string StarterJson = """
        {
          "name": "My first plugin",
          "author": "you",
          "version": "1.0",
          "commands": [
            {
              "keyword": "so",
              "name": "Stack Overflow search",
              "subtitle": "Search stackoverflow.com",
              "type": "web",
              "template": "https://stackoverflow.com/search?q={query}"
            },
            {
              "keyword": "time",
              "name": "What time is it",
              "subtitle": "Opens time.is — no query needed",
              "type": "open",
              "template": "https://time.is",
              "argOptional": true
            }
          ]
        }
        """;
}
