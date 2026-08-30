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
/// • v2.0 — Windows 11 "Fluent 2" design tokens: solid surfaces (#202020 / #F3F3F3),
///   4 px-class control geometry, neutral hover fills and accent-tinted selection —
///   the glassmorphism era is over, this is a native-feeling Windows 11 app now.
/// • Rim glow (v2.0.1, rewritten) — the z.ai chat-box comet: two soft radial light
///   blobs (bright head + fainter tail) that orbit the TRUE window perimeter via a
///   path animation, clipped to the window so the light only ever exists INSIDE the
///   rim — nothing bleeds outside, no spinning diagonal gradient. A rotating brush
///   on a rectangle never follows the border (the head slides across edges at uneven
///   speed and stalls at corners); a blob that travels the outline does.
/// • v1.3 — "auto" theme reads the Windows personalization setting.
/// </summary>
public static class Appearance
{
    public static readonly string[] BorderStyleNames = { "Aurora", "Sunset", "Ocean", "Ember", "Mint", "Solid" };

    public static readonly string[] AccentPresets =
    {
        "#0078D4", // Windows 11 system blue (default)
        "#7C6CFF", // violet (classic Lumo)
        "#0099BC", // teal
        "#107C10", // green
        "#C239B3", // magenta
        "#CA5010", // orange
        "#DA3B01", // red
        "#5C2E91", // purple
        "#038387", // cyan-deep
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
        return Color.FromRgb(0x00, 0x78, 0xD4);   // Windows 11 system blue
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
    /// v2.0 — Windows 11 Fluent 2 surface tokens. Solid, no blur dependency:
    /// dark #202020 base (Mica-dark equivalent), light #F3F3F3 base; hairline strokes
    /// (#383838 / #E5E5E5); neutral 5-6% hover fills and accent-tinted selection,
    /// the way Win11 list items and nav pills behave.
    /// </summary>
    public static Palette PaletteFor(bool dark, string? accentHex)
    {
        var accent = ParseAccent(accentHex);
        return dark
            ? new Palette(
                Panel:    FromRgb(0x20, 0x20, 0x20),   // Win11 SolidBackgroundFillColorBase dark
                Field:    FromRgb(0x2D, 0x2D, 0x2D),   // ControlFillColorDefault dark
                Border:   FromRgb(0x38, 0x38, 0x38),   // CardStrokeColorDefault dark
                Title:    FromRgb(0xFF, 0xFF, 0xFF),   // TextFillColorPrimary dark
                Subtitle: FromRgb(0xC8, 0xC8, 0xC8),   // TextFillColorSecondary dark
                Hover:    Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF),   // SubtleFillColorSecondary-ish
                Selected: Tint(accent, 0x2E),          // accent @ 18% — softer with the new selection pill
                GlyphBox: FromRgb(0x2D, 0x2D, 0x2D),
                Accent:   accent,
                Separator: FromRgb(0x33, 0x33, 0x33))
            : new Palette(
                Panel:    FromRgb(0xF3, 0xF3, 0xF3),   // Win11 SolidBackgroundFillColorBase light
                Field:    FromRgb(0xFB, 0xFB, 0xFB),
                Border:   FromRgb(0xE5, 0xE5, 0xE5),   // CardStrokeColorDefault light
                Title:    FromRgb(0x1B, 0x1B, 0x1B),   // TextFillColorPrimary light
                Subtitle: FromRgb(0x5F, 0x5F, 0x5F),   // TextFillColorSecondary light
                Hover:    Color.FromArgb(0x0D, 0x00, 0x00, 0x00),
                Selected: Tint(accent, 0x26),
                GlyphBox: FromRgb(0xFB, 0xFB, 0xFB),
                Accent:   accent,
                Separator: FromRgb(0xE0, 0xE0, 0xE0));
    }

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
