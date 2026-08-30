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
/// • Rim glow — a single comet-like accent gradient that orbits INSIDE the window
///   border (no outer bleed, no halo): the minimal animated-rim look of modern AI
///   chat boxes. Any stop set loops seamlessly because a 360° rotation of the brush
///   transform lands exactly back on its start.
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

    // v2.0 "comet" rim presets — one bright head + a fading tail, the rest of the rim
    // stays quiet so the effect reads as light chasing the border (z.ai-style), not a
    // rainbow fence. Loop is seamless: a full 360° brush rotation returns to itself.
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

    /// <summary>
    /// Builds the border brush for the glow effect. Returns the brush and (for animated
    /// styles) the rotation transform the caller must animate 0→360°.
    /// </summary>
    public static Brush BuildBorderBrush(string? styleName, string? accentHex, out RotateTransform? rotation)
    {
        rotation = null;

        if (BorderPresets.TryGetValue(styleName ?? "", out var stops))
        {
            var rot = new RotateTransform(0, 0.5, 0.5);
            rotation = rot;
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                RelativeTransform = rot,
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
            };
            foreach (var hex in stops)
                brush.GradientStops.Add(new GradientStop(ParseColor(hex), 0.0));
            // even out the offsets
            for (int i = 0; i < brush.GradientStops.Count; i++)
                brush.GradientStops[i].Offset = i / (double)(brush.GradientStops.Count - 1);
            // NOTE: intentionally NOT frozen — the caller animates the RelativeTransform.
            return brush;
        }

        // "Solid" (or anything unknown) → static accent-coloured border, no animation.
        var solid = new SolidColorBrush(ParseAccent(accentHex));
        solid.Freeze();
        return solid;
    }

    /// <summary>A brighter, semi-transparent accent used for the halo glow behind the window.</summary>
    public static Brush BuildHaloBrush(string? styleName, string? accentHex, out RotateTransform? rotation)
    {
        var brush = BuildBorderBrush(styleName, accentHex, out rotation);
        if (brush is SolidColorBrush solid)
        {
            var c = solid.Color;
            var soft = new SolidColorBrush(Color.FromArgb(0x88, c.R, c.G, c.B));
            soft.Freeze();
            return soft;
        }
        return brush; // gradient halo — the launcher lowers overall opacity for softness
    }

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
                Selected: Tint(accent, 0x36),          // accent @ 21% — nav/list selection
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
                Selected: Tint(accent, 0x2A),
                GlyphBox: FromRgb(0xFB, 0xFB, 0xFB),
                Accent:   accent,
                Separator: FromRgb(0xE0, 0xE0, 0xE0));
    }

    /// <summary>accent with the given alpha — the base for tinted highlights.</summary>
    public static Color Tint(Color accent, byte alpha) => Color.FromArgb(alpha, accent.R, accent.G, accent.B);

    // ---------------------------------------------------------------- rim wash (v2.0)

    /// <summary>
    /// v2.0 — a whisper of accent light INSIDE the top of the card, reading as the rim
    /// glow spilling a few pixels over the edge (never bleeding outside the window).
    /// Kept very quiet so the comet border stays the star.
    /// </summary>
    public static Brush BuildWashBrush(string? styleName, string? accentHex)
    {
        Color tint;
        if (BorderPresets.TryGetValue(styleName ?? "", out var stops))
            tint = ParseColor(stops[0]);
        else
            tint = ParseAccent(accentHex);

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x2E, tint.R, tint.G, tint.B), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x0E, tint.R, tint.G, tint.B), 0.45));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
        brush.Freeze();
        return brush;
    }

    private static Color FromRgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    // ---------------------------------------------------------------- system theme

    /// <summary>
    /// Reads the Windows personalization setting (Settings → Personalization → Colors →
    /// "Choose your mode"). Returns true when Windows apps are set to dark.
    /// </summary>
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v)
                return v == 0;
        }
        catch { }
        return true; // default to dark — Lumo's signature look
    }
}
