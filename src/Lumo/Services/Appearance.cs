using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;

namespace Lumo.Services;

/// <summary>
/// Shared appearance helpers (v1.2).
///
/// • Glow-border gradient presets — the animated border around the launcher, in the
///   style of modern chat UIs: a multi-colour gradient stroke that slowly rotates,
///   plus a blurred "halo" copy of the same gradient bleeding out behind the window.
/// • A shared dark/light palette so the launcher and the settings window look alike.
/// </summary>
public static class Appearance
{
    public static readonly string[] BorderStyleNames = { "Aurora", "Sunset", "Ocean", "Ember", "Mint", "Solid" };

    public static readonly string[] AccentPresets =
    {
        "#7C6CFF", // violet (classic Lumo)
        "#22D3EE", // cyan
        "#34D399", // mint green
        "#F472B6", // pink
        "#F59E0B", // amber
        "#EF4444", // red
        "#60A5FA", // blue
        "#A78BFA", // lavender
    };

    // Each preset's first and last stop match, so a 0→360° rotation loops seamlessly.
    private static readonly Dictionary<string, string[]> BorderPresets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Aurora"] = new[] { "#FF6366F1", "#FF22D3EE", "#FFA78BFA", "#FFF472B6", "#FF6366F1" },
        ["Sunset"] = new[] { "#FFF59E0B", "#FFEF4444", "#FFEC4899", "#FF8B5CF6", "#FFF59E0B" },
        ["Ocean"]  = new[] { "#FF06B6D4", "#FF3B82F6", "#FF6366F1", "#FF0EA5E9", "#FF06B6D4" },
        ["Ember"]  = new[] { "#FFF97316", "#FFEF4444", "#FFFBBF24", "#FFDC2626", "#FFF97316" },
        ["Mint"]   = new[] { "#FF34D399", "#FF22D3EE", "#FFA7F3D0", "#FF10B981", "#FF34D399" },
    };

    /// <summary>Parses "#RRGGBB" or "#AARRGGBB"; falls back to the classic violet on garbage.</summary>
    public static Color ParseAccent(string? hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
                return (Color)ColorConverter.ConvertFromString(hex.Trim());
        }
        catch { /* fall through */ }
        return Color.FromRgb(0x7C, 0x6C, 0xFF);
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

    public sealed record Palette(
        Color Panel, Color Border, Color Title, Color Subtitle,
        Color Hover, Color Selected, Color GlyphBox, Color Accent, Color Separator);

    public static Palette PaletteFor(bool dark, string? accentHex)
    {
        var accent = ParseAccent(accentHex);
        return dark
            ? new Palette(
                Panel: FromRgb(0x1E, 0x1F, 0x26),
                Border: FromRgb(0x33, 0x36, 0x4A),
                Title: FromRgb(0xF2, 0xF3, 0xF7),
                Subtitle: FromRgb(0x8A, 0x8F, 0xA3),
                Hover: FromRgb(0x2E, 0x31, 0x40),
                Selected: FromRgb(0x3A, 0x3E, 0x52),
                GlyphBox: FromRgb(0x2E, 0x31, 0x40),
                Accent: accent,
                Separator: FromRgb(0x2A, 0x2C, 0x38))
            : new Palette(
                Panel: Colors.White,
                Border: FromRgb(0xE2, 0xE4, 0xEC),
                Title: FromRgb(0x1B, 0x1D, 0x27),
                Subtitle: FromRgb(0x7A, 0x7F, 0x92),
                Hover: FromRgb(0xF0, 0xF1, 0xF7),
                Selected: FromRgb(0xE4, 0xE6, 0xFB),
                GlyphBox: FromRgb(0xEF, 0xF0, 0xF8),
                Accent: accent,
                Separator: FromRgb(0xEC, 0xEC, 0xF2));
    }

    private static Color FromRgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
