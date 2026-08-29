using System.Windows.Media;

namespace Lumo.UI;

/// <summary>
/// v1.7 — Lumo's vector icon set. Every icon is a 24×24 stroke path in the modern
/// "outline" style (2px-class strokes, round caps and joins — the Fluent/Lucide look),
/// rendered straight from path data so it stays razor sharp at any DPI and can be
/// re-coloured with the accent at runtime (no fonts, no assets, no emoji).
///
/// Result rows previously fell back to a bold letter/emoji glyph; the converter
/// maps that same glyph key to one of these geometries, so search results, hint
/// rows, tools and the footer all share one coherent icon language.
/// </summary>
public static class VectorIcons
{
    /// <summary>Gear used by the footer settings button (exposed for direct XAML use).</summary>
    public const string GearData =
        "M8.8 12 a3.2 3.2 0 1 0 6.4 0 a3.2 3.2 0 1 0 -6.4 0 " +
        "M5.6 12 a6.4 6.4 0 1 0 12.8 0 a6.4 6.4 0 1 0 -12.8 0 " +
        "M18.4 12 h2.4 M12 18.4 v2.4 M5.6 12 h-2.4 M12 5.6 v-2.4 " +
        "M16.53 16.53 l1.7 1.7 M7.47 16.53 l-1.7 1.7 M7.47 7.47 l-1.7 -1.7 M16.53 7.47 l1.7 -1.7";

    /// <summary>Close/clear cross for the search box (exposed for direct XAML use).</summary>
    public const string CloseData = "M6.5 6.5 l11 11 M17.5 6.5 l-11 11";

    private static readonly Dictionary<string, string> Data = new()
    {
        // dot — the neutral "·" fallback glyph
        ["dot"] = "M10.5 12 a1.5 1.5 0 1 0 3 0 a1.5 1.5 0 1 0 -3 0",

        // app grid — A/ applications, generic app rows
        ["app"] = "M4.5 4.5 h6 v6 h-6 z M13.5 4.5 h6 v6 h-6 z M4.5 13.5 h6 v6 h-6 z M13.5 13.5 h6 v6 h-6 z",

        // file — F/ files, file rows
        ["file"] = "M6.5 3.5 h7.5 l3.5 3.5 v13.5 h-11 z M14 3.5 v3.5 h3.5",

        // percent — C/ calculator
        ["percent"] = "M18.5 5.5 L5.5 18.5 " +
                      "M5 7.5 a2.5 2.5 0 1 0 5 0 a2.5 2.5 0 1 0 -5 0 " +
                      "M14 16.5 a2.5 2.5 0 1 0 5 0 a2.5 2.5 0 1 0 -5 0",

        // globe — W/ web search
        ["globe"] = "M3 12 a9 9 0 1 0 18 0 a9 9 0 1 0 -18 0 " +
                    "M3 12 h18 " +
                    "M12 3 c-3.2 3.2 -3.2 14.8 0 18 M12 3 c3.2 3.2 3.2 14.8 0 18",

        // picture — I/ image search
        ["image"] = "M3.5 5.5 h17 v13 h-17 z " +
                    "M7.2 10 a1.4 1.4 0 1 0 2.8 0 a1.4 1.4 0 1 0 -2.8 0 " +
                    "M4 16.5 l4.5 -4 3.5 3 2.5 -2.5 5.5 4.5",

        // sliders — U/ utilities
        ["sliders"] = "M3.5 7.5 h6.5 M15 7.5 h5.5 M3.5 16.5 h1 M9.5 16.5 h9.5 " +
                      "M10 7.5 a2.5 2.5 0 1 0 5 0 a2.5 2.5 0 1 0 -5 0 " +
                      "M4.5 16.5 a2.5 2.5 0 1 0 5 0 a2.5 2.5 0 1 0 -5 0",

        // clipboard — H/ clipboard history
        ["clipboard"] = "M9 4.5 h-1.5 a2 2 0 0 0 -2 2 v12 a2 2 0 0 0 2 2 h9 a2 2 0 0 0 2 -2 v-12 a2 2 0 0 0 -2 -2 h-1.5 " +
                        "M9 3 h6 a1 1 0 0 1 1 1 v1.5 a1 1 0 0 1 -1 1 h-6 a1 1 0 0 1 -1 -1 v-1.5 a1 1 0 0 1 1 -1 z",

        // window layout — S/ snap window management
        ["window"] = "M3.5 5 h17 v14 h-17 z M3.5 9.5 h17 M12 9.5 v9.5",

        // type — ! snippets (paste-anywhere text)
        ["type"] = "M4.5 7.5 v-2.5 h15 v2.5 M12 5 v14 M9 19 h6",

        // zap — ⚡ shortcuts & macros
        ["zap"] = "M13 3 L5.5 13.5 H11 L9.5 21 L17.5 10.5 H12 L14.5 3 Z",

        // gear — ⚙ settings
        ["gear"] = GearData,

        // plus — create-new rows
        ["plus"] = "M12 5.5 v13 M5.5 12 h13",

        // record — ⏺ macro recorder
        ["record"] = "M5 12 a7 7 0 1 0 14 0 a7 7 0 1 0 -14 0 " +
                     "M9.5 12 a2.5 2.5 0 1 0 5 0 a2.5 2.5 0 1 0 -5 0",

        // equal — "=" calculator result
        ["equal"] = "M5.5 9.5 h13 M5.5 14.5 h13",

        // close — "✕" clear / cancel rows (v1.7.1: the glyph mapped to a "close" key
        // that never existed in this dictionary, so those rows rendered an empty tile)
        ["close"] = CloseData,

        // alert — "!" errors
        ["alert"] = "M12 4.5 v9 M10.7 17.5 a1.3 1.3 0 1 0 2.6 0 a1.3 1.3 0 1 0 -2.6 0",

        // help — "?" no-results rows
        ["help"] = "M3.5 12 a8.5 8.5 0 1 0 17 0 a8.5 8.5 0 1 0 -17 0 " +
                   "M9.3 9.2 a2.7 2.7 0 1 1 3.9 2.4 c-0.85 0.45 -1.2 0.95 -1.2 1.9 " +
                   "M10.9 17 a1.1 1.1 0 1 0 2.2 0 a1.1 1.1 0 1 0 -2.2 0",

        // ellipsis — "…" building-index rows
        ["dots"] = "M5.9 12 a1.1 1.1 0 1 0 2.2 0 a1.1 1.1 0 1 0 -2.2 0 " +
                   "M10.9 12 a1.1 1.1 0 1 0 2.2 0 a1.1 1.1 0 1 0 -2.2 0 " +
                   "M15.9 12 a1.1 1.1 0 1 0 2.2 0 a1.1 1.1 0 1 0 -2.2 0",
    };

    private static readonly Dictionary<string, StreamGeometry?> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Maps a ResultItem glyph key (letter, symbol or emoji the search engine assigns)
    /// to its vector geometry. Returns null when the glyph has no vector counterpart,
    /// in which case the row keeps whatever shell icon it already carries.
    /// </summary>
    public static StreamGeometry? ForGlyph(string? glyph)
    {
        if (string.IsNullOrEmpty(glyph)) return null;
        string key = glyph switch
        {
            "A" => "app",
            "F" => "file",
            "C" => "percent",
            "W" => "globe",
            "I" => "image",
            "U" => "sliders",
            "⧉" => "clipboard",
            "▣" => "window",
            "S" => "type",
            "⚡" => "zap",
            "⚙" => "gear",
            "＋" or "+" => "plus",
            "⏺" => "record",
            "✕" => "close",
            "=" => "equal",
            "!" => "alert",
            "?" => "help",
            "…" => "dots",
            "·" => "dot",
            _ => "",
        };
        if (key.Length == 0) return null;

        if (Cache.TryGetValue(key, out var cached)) return cached;

        StreamGeometry? geo = null;
        try
        {
            if (Data.TryGetValue(key, out var path))
            {
                geo = (StreamGeometry)Geometry.Parse(path);
                geo.Freeze();
            }
        }
        catch { geo = null; }

        Cache[key] = geo;
        return geo;
    }
}
