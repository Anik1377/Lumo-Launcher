using Lumo.Core;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;

namespace Lumo.Services;

/// <summary>
/// Shared appearance helpers.
///
/// • v2.4 — "Raycast-grade" design system. The Win11 gray ladder (#202020 → #2D2D2D)
///   is gone; every surface now sits on a near-black, faintly blue-tinted ladder
///   modelled on Raycast's product chrome: panel ≈ #0E0F12, elevated fields ≈
///   #17181C, hairline #26282D, ink #F4F4F6, mute #9B9CA1. Selection is a QUIET
///   neutral fill (Raycast's active row) — the accent appears only as punctuation:
///   caret, selection pill, glyphs, primary buttons. Light mode mirrors the same
///   structure with a #FAFAFB canvas and white fields. Typography is embedded Inter.
/// • Rim glow (v2.0.1) — the z.ai chat-box comet: two soft radial light blobs that
///   orbit the TRUE window perimeter via a path animation, clipped to the window so
///   the light only ever exists INSIDE the rim.
/// • v1.3 — "auto" theme reads the Windows personalization setting.
/// </summary>
public static class Appearance
{
    public static readonly string[] BorderStyleNames = { "Aurora", "Sunset", "Ocean", "Ember", "Mint", "Solid" };

    /// <summary>
    /// v2.4 — Raycast red leads the palette (the brand accent of the design system
    /// we now mirror); the older Fluent hues stay as secondary choices.
    /// </summary>
    public static readonly string[] AccentPresets =
    {
        "#FF6363", // Raycast red (v2.4 default)
        "#7C6CFF", // violet (classic Lumo)
        "#57C1FF", // sky (Raycast info blue)
        "#59D499", // emerald (Raycast green)
        "#FFC533", // amber (Raycast yellow)
        "#C239B3", // magenta
        "#0099BC", // teal
        "#CA5010", // burnt orange
        "#5C2E91", // purple
    };

    // v2.0 "comet" rim presets — stops[0] is the bright core colour, stops[1] the
    // body colour of the orbiting light (z.ai-style head + tail); the rest of the rim
    // stays quiet so the effect reads as light chasing the border, not a rainbow fence.
    private static readonly Dictionary<string, string[]> BorderPresets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Aurora"] = new[] { "#FFBFD0FF", "#FF6366F1", "#0022D3EE", "#00000000" },
        ["Sunset"] = new[] { "#FFFFE3B3", "#FFEC4899", "#00F59E0B", "#00000000" },
        ["Ocean"]  = new[] { "#FFB5ECFA", "#FF0EA5E9", "#006366F1", "#00000000" },
        ["Ember"]  = new[] { "#FFFFD3C2", "#FFEF4444", "#00F97316", "#00000000" },
        ["Mint"]   = new[] { "#FFC8F7E1", "#FF10B981", "#0022D3EE", "#00000000" },
    };

    /// <summary>Parses "#RRGGBB" or "#AARRGGBB"; falls back to the Windows 11 blue on garbage.</summary>
    public static Color ParseAccent(string? hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
                return (Color)ColorConverter.ConvertFromString(hex.Trim());
        }
        catch { /* fall through */ }
        return Color.FromRgb(0xFF, 0x63, 0x63);   // Raycast red — the v2.4 system accent
    }

    /// <summary>True when the style is an animated comet preset (everything but "Solid").</summary>
    public static bool IsAnimatedStyle(string? styleName) => BorderPresets.ContainsKey(styleName ?? "");

    /// <summary>
    /// v2.0.1 — the two radial light blobs of the rim comet. The head is a small bright
    /// core that fades through the body colour to transparent; the tail is a larger,
    /// much fainter blob the caller orbits slightly BEHIND the head, producing the
    /// trailing comet streak. Both are frozen — the caller only moves their positions.
    /// </summary>
    public static void BuildCometBrushes(string? styleName, string? accentHex, out Brush head, out Brush tail)
    {
        Color core, body;
        if (BorderPresets.TryGetValue(styleName ?? "", out var stops))
        {
            core = ParseColor(stops[0]);
            body = ParseColor(stops[1]);
        }
        else
        {
            body = ParseAccent(accentHex);
            core = Lighten(body, 0.55);
        }

        var hb = new RadialGradientBrush();
        hb.GradientStops.Add(new GradientStop(core, 0.0));
        hb.GradientStops.Add(new GradientStop(Color.FromArgb(0xE0, body.R, body.G, body.B), 0.30));
        hb.GradientStops.Add(new GradientStop(Color.FromArgb(0x42, body.R, body.G, body.B), 0.58));
        hb.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
        hb.Freeze();
        head = hb;

        var tb = new RadialGradientBrush();
        tb.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, body.R, body.G, body.B), 0.0));
        tb.GradientStops.Add(new GradientStop(Color.FromArgb(0x1E, body.R, body.G, body.B), 0.5));
        tb.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
        tb.Freeze();
        tail = tb;
    }

    /// <summary>"Solid" style — a static accent-coloured ring, no motion at all.</summary>
    public static Brush BuildStaticRimBrush(string? styleName, string? accentHex)
    {
        var solid = new SolidColorBrush(ParseAccent(accentHex));
        solid.Freeze();
        return solid;
    }

    /// <summary>
    /// Static preview brush for the settings Appearance card — the comet palette laid
    /// out as a diagonal gradient (bright core → body → deep body), no animation, so
    /// the style's colours are readable at a glance.
    /// </summary>
    public static Brush BuildPreviewBrush(string? styleName, string? accentHex)
    {
        Color core, body;
        if (BorderPresets.TryGetValue(styleName ?? "", out var stops))
        {
            core = ParseColor(stops[0]);
            body = ParseColor(stops[1]);
        }
        else
        {
            body = ParseAccent(accentHex);
            core = Lighten(body, 0.55);
        }

        var b = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        b.GradientStops.Add(new GradientStop(core, 0.0));
        b.GradientStops.Add(new GradientStop(body, 0.45));
        b.GradientStops.Add(new GradientStop(Darken(body, 0.45), 1.0));
        b.Freeze();
        return b;
    }

    private static Color Lighten(Color c, double f) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * f),
        (byte)(c.G + (255 - c.G) * f),
        (byte)(c.B + (255 - c.B) * f));

    private static Color Darken(Color c, double f) => Color.FromRgb(
        (byte)(c.R * (1 - f)),
        (byte)(c.G * (1 - f)),
        (byte)(c.B * (1 - f)));

    private static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Gray; }
    }

    // ---------------------------------------------------------------- palette

    /// <summary>
    /// The effective palette. Hover/Selected are translucent tints of the accent colour
    /// (the macOS way) so every accent choice produces a coherent highlight system.
    /// </summary>
    public sealed record Palette(
        Color Panel, Color Field, Color Border, Color Title, Color Subtitle,
        Color Hover, Color Selected, Color GlyphBox, Color Accent, Color Separator);

    /// <summary>
    /// v2.4 — Raycast-grade surface ladder.
    ///
    /// Dark: a near-black, faintly blue-tinted canvas (#0E0F12) with ONE elevation
    /// step up (#17181C fields/tiles) and hairline strokes (#26282D) — the Raycast
    /// "surface ladder" (#07080a → #0d0d0d → #101111 → #121212) adapted for a
    /// launcher card that floats on the desktop. Text: ink #F4F4F6, mute #9B9CA1,
    /// placeholder ash #63646B. Selection is deliberately QUIET — a neutral fill one
    /// step above the panel (Raycast's active row); the accent stays punctuation.
    ///
    /// Light mirrors the structure: #FAFAFB canvas, #FFFFFF fields, #17181C ink,
    /// #71727A mute, #E6E7EA hairlines, #ECEDEF active row.
    /// </summary>
    public static Palette PaletteFor(bool dark, string? accentHex)
    {
        var accent = ParseAccent(accentHex);
        return dark
            ? new Palette(
                Panel:    FromRgb(0x0E, 0x0F, 0x12),   // canvas — Raycast surface tier 1
                Field:    FromRgb(0x17, 0x18, 0x1C),   // elevated fill — tier 3 (inputs, tiles)
                Border:   FromRgb(0x26, 0x28, 0x2D),   // hairline (Raycast #242728, nudged)
                Title:    FromRgb(0xF4, 0xF4, 0xF6),   // ink
                Subtitle: FromRgb(0x9B, 0x9C, 0xA1),   // mute
                Hover:    Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF),   // 6% white — quiet pointer wash
                Selected: FromRgb(0x1F, 0x21, 0x27),   // active row — Raycast's neutral highlight
                GlyphBox: FromRgb(0x1B, 0x1C, 0x22),   // icon tile fill — tier 2
                Accent:   accent,
                Separator: FromRgb(0x1D, 0x1F, 0x24))
            : new Palette(
                Panel:    FromRgb(0xFA, 0xFA, 0xFB),   // canvas
                Field:    Colors.White,                // elevated fill
                Border:   FromRgb(0xE6, 0xE7, 0xEA),   // hairline
                Title:    FromRgb(0x17, 0x18, 0x1C),   // ink
                Subtitle: FromRgb(0x71, 0x72, 0x7A),   // mute
                Hover:    Color.FromArgb(0x0A, 0x00, 0x00, 0x00),   // 4% black
                Selected: FromRgb(0xEC, 0xED, 0xEF),   // active row
                GlyphBox: FromRgb(0xF1, 0xF1, 0xF4),   // icon tile fill
                Accent:   accent,
                Separator: FromRgb(0xE8, 0xE9, 0xEB));
    }

    /// <summary>The muted placeholder tone for the current mode (ash tier of the ladder).</summary>
    public static Color PlaceholderFor(bool dark) => dark
        ? FromRgb(0x63, 0x64, 0x6B)
        : FromRgb(0xA2, 0xA3, 0xA8);

    /// <summary>
    /// The elevated card/surface colour used by settings cards, code blocks and
    /// popovers — tier 2.5 of the ladder, between the icon tile and the field.
    /// </summary>
    public static Color ElevatedFor(bool dark) => dark
        ? FromRgb(0x14, 0x15, 0x1A)
        : Colors.White;

    /// <summary>accent with the given alpha — the base for tinted highlights.</summary>
    public static Color Tint(Color accent, byte alpha) => Color.FromArgb(alpha, accent.R, accent.G, accent.B);

    private static Color FromRgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    // ---------------------------------------------------------------- system theme

    /// <summary>
    /// Reads the Windows personalization setting (Settings → Personalization → Colors →
    /// "Choose your mode"). Returns true when Windows apps are set to dark.
    /// v2.1 — the probe itself now lives in the pure Core/SystemTheme.cs so the
    /// test harness can compile Settings without WPF types; this shim stays for
    /// every existing caller.
    /// </summary>
    public static bool IsSystemDark() => SystemTheme.IsDark();
}
