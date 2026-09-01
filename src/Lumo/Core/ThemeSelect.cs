namespace Lumo.Core;

/// <summary>
/// v3.0 — the fully resolved visual spec every window paints from, plus the
/// built-in preset catalog and the resolution rules. Pure (no WPF types) so the
/// test harness can pin the catalog and the precedence chain.
///
/// Resolution precedence:
///   1. an imported custom theme (Settings.CustomThemeFile → JSON) wins outright —
///      it carries its own mode + accent + palette overrides;
///   2. a built-in preset (Settings.ThemePreset) — its own mode + accent + colors;
///   3. the legacy v2 pair (Settings.Theme "dark|light|auto" + Settings.AccentColor)
///      on the stock zinc ladder — exactly the pre-v3 look, so upgrades are invisible.
/// </summary>
public static class ThemeSelect
{
    public sealed record Preset(
        string Id, string Name, bool Dark, string Accent, IReadOnlyDictionary<string, string> Colors);

    public sealed record ThemeSpec(
        bool Dark, string AccentHex, IReadOnlyDictionary<string, string> Overrides, string Id, string Name);

    // ------------------------------------------------------------ catalog

    private static readonly Dictionary<string, string> NoColors = new();

    /// <summary>Claude-style warm dark — parchment ink on baked-clay charcoal.</summary>
    private static readonly Dictionary<string, string> DuskColors = new()
    {
        ["panel"] = "#1D1C1A", ["field"] = "#2A2927", ["card"] = "#232220",
        ["sidebar"] = "#161514", ["border"] = "#35332D", ["separator"] = "#2A2925",
        ["title"] = "#F0EEE6", ["subtitle"] = "#A6A29A", ["caption"] = "#171614",
        ["placeholder"] = "#78746B", ["selected"] = "#2A2926", ["selstroke"] = "#413E37",
        ["glyphbox"] = "#262522", ["code"] = "#131211", ["userbubble"] = "#33322E",
        ["userbubbletext"] = "#F5F4EF",
    };

    /// <summary>Claude light — warm paper, terracotta accent.</summary>
    private static readonly Dictionary<string, string> ParchmentColors = new()
    {
        ["panel"] = "#FAF9F5", ["field"] = "#FFFFFF", ["card"] = "#FFFFFF",
        ["sidebar"] = "#F0EEE6", ["border"] = "#E5E2D9", ["separator"] = "#EAE7DE",
        ["title"] = "#3D3929", ["subtitle"] = "#83827D", ["caption"] = "#F1EFE8",
        ["placeholder"] = "#A5A199", ["selected"] = "#EFEBE0", ["selstroke"] = "#D9D4C7",
        ["glyphbox"] = "#F0EEE6", ["code"] = "#F5F3EC", ["userbubble"] = "#EDEAE1",
        ["userbubbletext"] = "#3D3929",
    };

    /// <summary>Nord — the polar night palette with frost-blue accent.</summary>
    private static readonly Dictionary<string, string> NordColors = new()
    {
        ["panel"] = "#2E3440", ["field"] = "#3B4252", ["card"] = "#353C49",
        ["sidebar"] = "#272C36", ["border"] = "#434C5E", ["separator"] = "#3B4252",
        ["title"] = "#ECEFF4", ["subtitle"] = "#A0AABB", ["caption"] = "#272C36",
        ["placeholder"] = "#7B88A1", ["selected"] = "#3B4252", ["selstroke"] = "#4C566A",
        ["glyphbox"] = "#3B4252", ["code"] = "#272C36", ["userbubble"] = "#3B4252",
        ["userbubbletext"] = "#ECEFF4",
    };

    /// <summary>Matcha — a calm green-tinted light theme.</summary>
    private static readonly Dictionary<string, string> MatchaColors = new()
    {
        ["panel"] = "#F6F8F4", ["field"] = "#FFFFFF", ["card"] = "#FFFFFF",
        ["sidebar"] = "#ECF0EA", ["border"] = "#DDE3D8", ["separator"] = "#E4E9E0",
        ["title"] = "#26312A", ["subtitle"] = "#6E7A70", ["caption"] = "#EDF1EB",
        ["placeholder"] = "#9AA69C", ["selected"] = "#E7EDE4", ["selstroke"] = "#CBD6C7",
        ["glyphbox"] = "#EDF1EB", ["code"] = "#F1F5EF", ["userbubble"] = "#E4EBE2",
        ["userbubbletext"] = "#26312A",
    };

    /// <summary>Graphite — an even quieter mono dark with a cool gray-blue accent.</summary>
    private static readonly Dictionary<string, string> GraphiteColors = new()
    {
        ["panel"] = "#111213", ["field"] = "#1B1C1E", ["card"] = "#161718",
        ["sidebar"] = "#0C0D0E", ["border"] = "#2A2B2E", ["separator"] = "#202124",
        ["title"] = "#F2F2F3", ["subtitle"] = "#98999E", ["caption"] = "#0E0F10",
        ["placeholder"] = "#63646A", ["selected"] = "#212225", ["selstroke"] = "#393A3F",
        ["glyphbox"] = "#1A1B1D", ["code"] = "#121314", ["userbubble"] = "#2C2D30",
        ["userbubbletext"] = "#F4F4F5",
    };

    /// <summary>The six built-ins: three dark, three light. Ids are stable API — they
    /// persist in settings.json and tests pin them.</summary>
    public static IReadOnlyList<Preset> Presets { get; } = new[]
    {
        new Preset("lumo-dark",  "Lumo Dark",  true,  "#FF6363", NoColors),
        new Preset("lumo-light", "Lumo Light", false, "#FF6363", NoColors),
        new Preset("dusk",       "Claude Dusk", true,  "#D97757", DuskColors),
        new Preset("parchment",  "Parchment",  false, "#C15F3C", ParchmentColors),
        new Preset("nord",       "Nord",       true,  "#88C0D0", NordColors),
        new Preset("matcha",     "Matcha",     false, "#4F9D69", MatchaColors),
        new Preset("graphite",   "Graphite",   true,  "#8AB4F8", GraphiteColors),
    };

    public static Preset? FindPreset(string? id) =>
        Presets.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------ resolution

    /// <summary>
    /// The one rule every caller shares: custom import &gt; preset &gt; legacy pair.
    /// Never throws — junk input falls through to the legacy ladder.
    /// </summary>
    public static ThemeSpec Resolve(
        string? presetId, string? customThemeJson, string? legacyTheme, string? legacyAccentHex, bool systemDark)
    {
        if (!string.IsNullOrWhiteSpace(customThemeJson)
            && ThemeFile.TryParse(customThemeJson, out var t, out _) && t is not null)
        {
            return new ThemeSpec(t.Dark, t.Accent, t.Colors, "user:" + ThemeFile.Slug(t.Name), t.Name);
        }

        var preset = FindPreset(presetId);
        if (preset is not null)
            return new ThemeSpec(preset.Dark, preset.Accent, preset.Colors, preset.Id, preset.Name);

        bool dark = (legacyTheme ?? "").Trim().ToLowerInvariant() switch
        {
            "light" => false,
            "auto" => systemDark,
            _ => true,
        };
        return new ThemeSpec(dark, string.IsNullOrWhiteSpace(legacyAccentHex) ? "#FF6363" : legacyAccentHex.Trim(),
            NoColors, "legacy", "Lumo");
    }
}
