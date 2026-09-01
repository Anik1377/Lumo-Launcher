using System.Text.Json;

namespace Lumo.Core;

/// <summary>
/// v3.0 — a Lumo theme file: the import/export + preset format of the theme system.
/// Pure data + validation (no WPF types) so the test harness can exercise every rule.
///
/// JSON shape:
/// <code>
/// {
///   "schema": "lumo.theme/1",
///   "name": "Claude Dusk",
///   "mode": "dark",                  // "dark" | "light" (missing = dark)
///   "accent": "#D97757",             // #RRGGBB or #AARRGGBB (missing = Raycast red)
///   "colors": { "panel": "#1D1C1A", "field": "#2A2927", "border": "#35332D" }
/// }
/// </code>
///
/// Tolerance doctrine (same as Settings.ApplyJson): structural garbage (not JSON,
/// wrong schema) fails with an error the UI can show; field-level junk (bad hex,
/// unknown color keys) is silently dropped and the theme still loads with the
/// remaining tokens. Every theme always produces a usable palette because the
/// overrides sit ON TOP of the built-in ladder, never instead of it.
/// </summary>
public sealed record ThemeFile(string Name, bool Dark, string Accent, IReadOnlyDictionary<string, string> Colors)
{
    public const string Schema = "lumo.theme/1";
    public const int MaxNameChars = 40;
    public const int MaxColors = 24;

    /// <summary>The override keys a theme may set — exactly the tokens ThemeService
    /// paints. ALL LOWERCASE on purpose: the parser folds incoming JSON keys with
    /// ToLowerInvariant before this check, so the two sides must match byte-for-byte
    /// ("selStroke" in this list would silently never match a parsed "selstroke").</summary>
    public static readonly string[] ColorKeys =
    {
        "panel", "field", "card", "sidebar", "border", "separator",
        "title", "subtitle", "caption", "placeholder",
        "hover", "selected", "selstroke", "glyphbox", "chip",
        "accent", "warn", "code", "userbubble", "userbubbletext",
    };

    // ---------------------------------------------------------------- parse

    public static bool TryParse(string? json, out ThemeFile? theme, out string? error)
    {
        theme = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The file is empty.";
            return false;
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { error = "The file is not valid JSON."; return false; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Not a Lumo theme file (expected an object).";
                return false;
            }

            var root = doc.RootElement;
            if (!root.TryGetProperty("schema", out var schemaEl) || schemaEl.GetString() != Schema)
            {
                error = $"Not a Lumo theme file (schema must be \"{Schema}\").";
                return false;
            }

            string name = "Imported theme";
            if (root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
            {
                var n = (nameEl.GetString() ?? "").Trim();
                if (n.Length > 0) name = n.Length <= MaxNameChars ? n : n[..MaxNameChars];
            }

            bool dark = true;
            if (root.TryGetProperty("mode", out var modeEl) && modeEl.ValueKind == JsonValueKind.String)
                dark = !string.Equals(modeEl.GetString(), "light", StringComparison.OrdinalIgnoreCase);

            string accent = "#FF6363";
            if (root.TryGetProperty("accent", out var accentEl) && accentEl.ValueKind == JsonValueKind.String
                && IsValidHex(accentEl.GetString()))
                accent = NormalizeHex(accentEl.GetString()!);

            var colors = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("colors", out var colorsEl) && colorsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in colorsEl.EnumerateObject())
                {
                    if (colors.Count >= MaxColors) break;
                    if (prop.Value.ValueKind != JsonValueKind.String) continue;
                    var key = prop.Name.Trim().ToLowerInvariant();
                    if (!ColorKeys.Contains(key)) continue;
                    if (!IsValidHex(prop.Value.GetString())) continue;
                    colors[key] = NormalizeHex(prop.Value.GetString()!);
                }
            }

            theme = new ThemeFile(name, dark, accent, colors);
            return true;
        }
    }

    public static ThemeFile? LoadFile(string path)
    {
        try { return TryParse(File.ReadAllText(path), out var t, out _) ? t : null; }
        catch { return null; }
    }

    // ---------------------------------------------------------------- serialize

    public string Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            writer.WriteString("name", Name);
            writer.WriteString("mode", Dark ? "dark" : "light");
            writer.WriteString("accent", Accent);
            if (Colors.Count > 0)
            {
                writer.WriteStartObject("colors");
                foreach (var key in ColorKeys)
                    if (Colors.TryGetValue(key, out var hex))
                        writer.WriteString(key, hex);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, Serialize());
    }

    // ---------------------------------------------------------------- hex helpers

    /// <summary>Accepts strictly #RRGGBB or #AARRGGBB (palette tokens are painted opaque).</summary>
    public static bool IsValidHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.Trim();
        if (s.Length is not (7 or 9) || s[0] != '#') return false;
        foreach (var c in s[1..])
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    public static string NormalizeHex(string hex)
    {
        var s = hex.Trim().ToUpperInvariant();
        // 6-digit form stays as-is; 8-digit keeps its alpha (the painter honors it).
        return s.Length == 9 ? s[..1] + s[3..] : s;   // fold AARRGGBB → RRGGBB (alpha ignored: tokens are opaque)
    }

    /// <summary>File-system-safe slug for export/import copies ("Claude Dusk" → "claude-dusk.json").</summary>
    public static string Slug(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in (name ?? "theme").ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c is ' ' or '-' or '_' or '.' && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "theme" : slug.Length > 40 ? slug[..40].Trim('-') : slug;
    }
}
