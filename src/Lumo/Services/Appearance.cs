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
/// • Glow-border gradient presets — the animated border around the launcher, in the
///   style of modern chat UIs: a multi-colour gradient stroke that slowly rotates,
///   plus a blurred "halo" copy of the same gradient bleeding out behind the window.
/// • A shared Apple-flavoured palette (iOS system greys, Apple text colours) so the
///   launcher and the settings window look like one product.
/// • v1.3 — selection/hover tints are now derived from the user's accent colour
///   (macOS-style translucent accent tints) and "auto" theme can read the Windows
///   personalization setting.
/// </summary>
public static class Appearance
{
    public static readonly string[] BorderStyleNames = { "Aurora", "Sunset", "Ocean", "Ember", "Mint", "Solid" };

    public static readonly string[] AccentPresets =
    {
        "#7C6CFF", // violet (classic Lumo)
        "#0A84FF", // iOS blue
        "#22D3EE", // cyan
        "#30D158", // iOS green
        "#F472B6", // pink
        "#FF9F0A", // iOS orange
        "#EF4444", // red
        "#BF5AF2", // iOS purple
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

    /// <summary>
    /// The effective palette. Hover/Selected are translucent tints of the accent colour
    /// (the macOS way) so every accent choice produces a coherent highlight system.
    /// </summary>
    public sealed record Palette(
        Color Panel, Color Field, Color Border, Color Title, Color Subtitle,
        Color Hover, Color Selected, Color GlyphBox, Color Accent, Color Separator);

    public static Palette PaletteFor(bool dark, string? accentHex)
    {
        var accent = ParseAccent(accentHex);
        return dark
            ? new Palette(
                Panel:    FromRgb(0x1C, 0x1C, 0x1E),   // iOS systemBackground dark
                Field:    FromRgb(0x2C, 0x2C, 0x2E),   // iOS secondarySystemBackground
                Border:   FromRgb(0x38, 0x38, 0x3A),   // iOS separator
                Title:    FromRgb(0xF5, 0xF5, 0xF7),   // Apple marketing white
                Subtitle: FromRgb(0x98, 0x98, 0x9D),   // iOS secondaryLabel
                Hover:    Tint(accent, 0x16),          // accent @ 9%
                Selected: Tint(accent, 0x2A),          // accent @ 16%
                GlyphBox: FromRgb(0x2C, 0x2C, 0x2E),
                Accent:   accent,
                Separator: FromRgb(0x38, 0x38, 0x3A))
            : new Palette(
                Panel:    Colors.White,
                Field:    FromRgb(0xF5, 0xF5, 0xF7),   // Apple site grey
                Border:   FromRgb(0xE5, 0xE5, 0xEA),   // iOS separator light
                Title:    FromRgb(0x1D, 0x1D, 0x1F),   // Apple near-black
                Subtitle: FromRgb(0x86, 0x86, 0x8B),   // Apple grey text
                Hover:    Tint(accent, 0x0F),
                Selected: Tint(accent, 0x22),
                GlyphBox: FromRgb(0xF5, 0xF5, 0xF7),
                Accent:   accent,
                Separator: FromRgb(0xE8, 0xE8, 0xED));
    }

    /// <summary>accent with the given alpha — the base for tinted highlights.</summary>
    public static Color Tint(Color accent, byte alpha) => Color.FromArgb(alpha, accent.R, accent.G, accent.B);

    // ---------------------------------------------------------------- glass (v1.7)

    /// <summary>
    /// v1.7 — translucent panel/border/chip fills for the glassmorphism look. These sit
    /// ON TOP of the acrylic backdrop, so they carry real alpha (the blur shines through);
    /// when the backdrop is unavailable the launcher falls back to the opaque values from
    /// <see cref="PaletteFor"/>.
    /// </summary>
    public sealed record GlassPalette(Color Panel, Color Border, Color Separator, Color Chip, Color GlyphBox);

    public static GlassPalette GlassFor(bool dark) => dark
        ? new GlassPalette(
            Panel:     Color.FromArgb(0xC2, 0x18, 0x18, 0x1C),  // deep smoked glass
            Border:    Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF),  // 19% white edge highlight
            Separator: Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
            Chip:      Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF),
            GlyphBox:  Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF))
        : new GlassPalette(
            Panel:     Color.FromArgb(0xC8, 0xFA, 0xFA, 0xFC),  // light frost
            Border:    Color.FromArgb(0x2E, 0x00, 0x00, 0x00),
            Separator: Color.FromArgb(0x22, 0x00, 0x00, 0x00),
            Chip:      Color.FromArgb(0x12, 0x00, 0x00, 0x00),
            GlyphBox:  Color.FromArgb(0x10, 0x00, 0x00, 0x00));

    /// <summary>
    /// v1.7 — the ambient colour wash inside the top of the glass card. Replaces the old
    /// outer halo (which needed a transparent window bleed); on glass it reads like a
    /// soft light source — accent-coloured, fading down into the panel.
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
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x5E, tint.R, tint.G, tint.B), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x16, tint.R, tint.G, tint.B), 0.45));
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
