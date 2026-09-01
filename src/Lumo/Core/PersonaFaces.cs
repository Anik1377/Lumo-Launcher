namespace Lumo.Core;

/// <summary>
/// v3.0 — the persona face catalog. A persona face is a tiny vector character:
/// a BODY shape, an EYE style and a MOUTH style, drawn in a 24×24 box and tinted
/// with the persona's color. Pure data + normalization (no WPF) so tests can pin
/// the catalog; UI/PersonaFaceView and UI/MascotView share the rendering.
///
/// Geometry strings are WPF StreamGeometry mini-language, viewBox 24×24 — they
/// only ever MEAN something to the WPF renderer, but the ids/normalization live
/// here so both renderers and the store agree on what exists.
/// </summary>
public static class PersonaFaces
{
    public const string DefaultFace = "spark";

    public sealed record Face(string Id, string Name, string Body, string Eyes, string Mouth, string? Extra = null);

    // ---- body shapes (24×24) ------------------------------------------------

    private const string BodySpark =
        "M12,1.5 C12.9,7.6 16.4,11.1 22.5,12 C16.4,12.9 12.9,16.4 12,22.5 C11.1,16.4 7.6,12.9 1.5,12 C7.6,11.1 11.1,7.6 12,1.5 Z";

    private const string BodyBlob =
        "M12,2 C17.5,2 22,6.3 22,11.7 C22,17.4 17.8,22 12,22 C6.2,22 2,17.4 2,11.7 C2,6.3 6.5,2 12,2 Z";

    private const string BodyBot =
        "M12,1 C12.8,1 13.4,1.6 13.4,2.4 L13.4,3.4 L17.5,3.4 C20,3.4 22,5.4 22,7.9 L22,16.5 C22,19 20,21 17.5,21 L6.5,21 C4,21 2,19 2,16.5 L2,7.9 C2,5.4 4,3.4 6.5,3.4 L10.6,3.4 L10.6,2.4 C10.6,1.6 11.2,1 12,1 Z";

    private const string BodyGhost =
        "M12,2 C17,2 21,6 21,11 L21,20.2 C21,21.2 19.9,21.8 19.1,21.2 L17.2,19.8 C16.7,19.4 16,19.4 15.5,19.8 L13.7,21.2 C13.3,21.5 12.7,21.5 12.3,21.2 L10.5,19.8 C10,19.4 9.3,19.4 8.8,19.8 L6.9,21.2 C6.1,21.8 5,21.2 5,20.2 L5,11 C5,6 7,2 12,2 Z";

    private const string BodyStar =
        "M12,1.8 L14.9,8 L21.6,8.9 C22.3,9 22.6,9.9 22.1,10.4 L17.3,15.1 L18.4,21.8 C18.5,22.5 17.8,23.1 17.1,22.7 L12,19.7 L6.9,22.7 C6.2,23.1 5.5,22.5 5.6,21.8 L6.7,15.1 L1.9,10.4 C1.4,9.9 1.7,9 2.4,8.9 L9.1,8 L12,1.8 Z";

    private const string BodyCat =
        "M5.2,2.6 C5.8,2.2 6.6,2.4 6.9,3.1 L8.4,6.2 C9.5,5.8 10.7,5.6 12,5.6 C13.3,5.6 14.5,5.8 15.6,6.2 L17.1,3.1 C17.4,2.4 18.2,2.2 18.8,2.6 C19.3,3 19.5,3.6 19.2,4.2 L18,6.9 C20.3,8.3 22,10.7 22,13.5 C22,18.2 17.5,22 12,22 C6.5,22 2,18.2 2,13.5 C2,10.7 3.7,8.3 6,6.9 L4.8,4.2 C4.5,3.6 4.7,3 5.2,2.6 Z";

    private const string BodyMoon =
        "M14.5,2 C17,3.4 19.5,6.4 19.5,10.5 C19.5,16 15.2,20.5 9.7,20.5 C6.4,20.5 3.7,18.9 2,16.6 C3.2,17.3 4.7,17.7 6.2,17.7 C12.2,17.7 16.5,12.9 16.5,7.2 C16.5,5.3 15.8,3.5 14.5,2 Z";

    private const string BodyLeaf =
        "M12,2 C17.5,2.4 22,6.5 22,12.2 C22,17.8 17.7,22 12.2,22 C6.5,22 2.2,17.7 2,12 C1.9,9.9 2.5,8.4 3.7,7.2 C4.4,6.5 5.4,6.6 6,7.3 L7.8,9.1 C8.5,6 9.9,2.9 12,2 Z";

    // ---- catalog ------------------------------------------------------------

    private static readonly Dictionary<string, Face> Catalog = new(StringComparer.OrdinalIgnoreCase)
    {
        ["spark"] = new("spark", "Spark",   BodySpark, "happy",  "smile"),
        ["blob"]  = new("blob",  "Blob",    BodyBlob,  "round",  "smile"),
        ["bot"]   = new("bot",   "Bot",     BodyBot,   "square", "grin"),
        ["ghost"] = new("ghost", "Ghost",   BodyGhost, "happy",  "open"),
        ["star"]  = new("star",  "Star",    BodyStar,  "round",  "grin"),
        ["cat"]   = new("cat",   "Cat",     BodyCat,   "cat",    "cat"),
        ["moon"]  = new("moon",  "Moon",    BodyMoon,  "sleepy", "smile"),
        ["leaf"]  = new("leaf",  "Sprout",  BodyLeaf,  "happy",  "smile"),
    };

    /// <summary>The whole catalog, display order.</summary>
    public static IReadOnlyList<Face> All { get; } = Catalog.Values.ToList();

    public static Face? Find(string? id) =>
        id is null ? null : Catalog.TryGetValue(id.Trim(), out var f) ? f : null;

    /// <summary>Resolve-or-default — an empty or unknown id falls back to spark.</summary>
    public static Face Resolve(string? id) => Find(id) ?? Catalog[DefaultFace];

    /// <summary>Validate-or-empty for persistence: unknown ids persist as "" (default).</summary>
    public static string NormalizeId(string? id) => Find(id) is null ? "" : id!.Trim().ToLowerInvariant();

    /// <summary>Persona color: validated #RRGGBB hex or "" (= theme accent). 8-digit folds to 6.</summary>
    public static string NormalizeColor(string? hex)
    {
        var s = (hex ?? "").Trim();
        if (s.StartsWith('#'))
        {
            var body = s[1..];
            if (body.Length == 8) s = "#" + body[2..];   // drop alpha
        }
        return ThemeFile.IsValidHex(s) && s.Length == 7 ? s.ToUpperInvariant() : "";
    }
}
