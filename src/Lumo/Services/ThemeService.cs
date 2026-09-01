using Lumo.Core;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace Lumo.Services;

/// <summary>
/// v3.0 — the ONE place that paints. Every window's private ApplyTheme/ApplySelfTheme
/// (four near-identical hand-rolled brush builders) collapses into a call here:
/// the active ThemeSpec resolves from Settings (custom import &gt; preset &gt; legacy
/// pair — see ThemeSelect), the WPF-UI Fluent layer is kept in sync with the mode
/// and accent so its implicit control styles never fight the palette, and the full
/// brush-token superset is written into the window's Resources. Window-specific
/// work that remains outside (DWM acrylic, root-card translucency, decoration
/// geometry) reads the same resolved tokens via <see cref="Apply"/>'s return value.
///
/// Theme change flow: something mutates theme state (tray toggle, Settings, import)
/// → <see cref="RaiseChanged"/> → every open window re-runs its ApplyTheme, which
/// lands here and repaints in one shot.
/// </summary>
public static class ThemeService
{
    /// <summary>Raised after any external theme-state change (tray, Settings, import).</summary>
    public static event Action? Changed;

    private static (bool Dark, string Accent)? _fluentSynced;

    public static void RaiseChanged() => Changed?.Invoke();

    /// <summary>The final token set: ThemeSpec (source of truth) + the merged colors
    /// every window-specific extra paints from.</summary>
    public sealed record Resolved(
        ThemeSelect.ThemeSpec Spec,
        bool Dark,
        Color Panel, Color Field, Color Card, Color Sidebar, Color Border, Color Separator,
        Color Title, Color Subtitle, Color Caption, Color Placeholder, Color Hover,
        Color Selected, Color SelStroke, Color GlyphBox, Color Chip, Color Accent,
        Color UserBubble, Color UserBubbleText, Color Code);

    // ------------------------------------------------------------ resolution

    /// <summary>Resolves the active spec. The custom import lives as a FILE NAME inside
    /// ThemesDir (portable-safe); a bare absolute path is honored too.</summary>
    public static ThemeSelect.ThemeSpec ResolveSpec(Settings s)
    {
        string? customJson = null;
        var fileRef = s.CustomThemeFile;
        if (!string.IsNullOrWhiteSpace(fileRef))
        {
            try
            {
                var path = Path.IsPathRooted(fileRef) ? fileRef : Path.Combine(AppPaths.ThemesDir, fileRef);
                if (File.Exists(path)) customJson = File.ReadAllText(path);
            }
            catch { customJson = null; }
        }
        return ThemeSelect.Resolve(s.ThemePreset, customJson, s.Theme, s.AccentColor, Appearance.IsSystemDark());
    }

    /// <summary>The ThemeFile that would be exported for the current look (presets export
    /// their full color set, so a round-trip is faithful).</summary>
    public static ThemeFile ExportFile(Settings s)
    {
        var spec = ResolveSpec(s);
        return new ThemeFile(spec.Name, spec.Dark, spec.AccentHex, spec.Overrides.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    /// <summary>Resolve + sync the Fluent layer + paint. The one call every window makes.</summary>
    public static Resolved Apply(Window w, Settings s)
    {
        var spec = ResolveSpec(s);
        SyncFluent(spec);
        return Paint(w, spec);
    }

    /// <summary>Resolve a spec to its final colors WITHOUT touching any window — the
    /// theme gallery miniatures paint from this.</summary>
    public static Resolved ResolveColors(ThemeSelect.ThemeSpec spec)
    {
        bool dark = spec.Dark;
        var p = Appearance.PaletteFor(dark, spec.AccentHex);
        Color O(string key, Color fallback) =>
            spec.Overrides.TryGetValue(key.ToLowerInvariant(), out var hex) && TryColor(hex, out var c) ? c : fallback;
        return new Resolved(spec, dark,
            O("panel", p.Panel), O("field", p.Field),
            O("card", dark ? Color.FromRgb(0x14, 0x15, 0x1A) : Colors.White),
            O("sidebar", dark ? Color.FromRgb(0x0B, 0x0C, 0x0F) : Color.FromRgb(0xF1, 0xF1, 0xF4)),
            O("border", p.Border), O("separator", p.Separator),
            O("title", p.Title), O("subtitle", p.Subtitle),
            O("caption", dark ? Color.FromRgb(0x0B, 0x0B, 0x0D) : Color.FromRgb(0xF1, 0xF1, 0xF4)),
            O("placeholder", Appearance.PlaceholderFor(dark)), O("hover", p.Hover),
            O("selected", p.Selected), O("selStroke", p.SelStroke), O("glyphBox", p.GlyphBox),
            O("chip", dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0D, 0x00, 0x00, 0x00)),
            O("accent", Appearance.ParseAccent(spec.AccentHex)),
            O("userBubble", dark ? Color.FromRgb(0x2F, 0x2F, 0x33) : Color.FromRgb(0xE9, 0xE9, 0xEC)),
            O("userBubbleText", dark ? Color.FromRgb(0xF3, 0xF3, 0xF4) : Color.FromRgb(0x16, 0x16, 0x1A)),
            O("code", dark ? Color.FromRgb(0x12, 0x12, 0x15) : Color.FromRgb(0xF6, 0xF6, 0xF8)));
    }

    // ------------------------------------------------------------ painting

    /// <summary>
    /// Writes the complete brush-token superset into the window. Extra tokens are
    /// harmless; every window therefore shares one coherent ladder regardless of
    /// which subset its XAML references.
    /// </summary>
    public static Resolved Paint(Window w, ThemeSelect.ThemeSpec spec)
    {
        var t = ResolveColors(spec);
        bool dark = t.Dark;

        Set(w, "TitleBrush", t.Title);
        Set(w, "SubtitleBrush", t.Subtitle);
        Set(w, "HoverBrush", t.Hover);
        Set(w, "SelectedBrush", t.Selected);
        Set(w, "AccentBrush", t.Accent);
        Set(w, "ChipBrush", t.Chip);
        Set(w, "ChipTextBrush", t.Subtitle);
        Set(w, "PlaceholderBrush", t.Placeholder);
        Set(w, "IconBrush", t.Subtitle);
        Set(w, "GlyphBoxBrush", t.GlyphBox);
        Set(w, "BorderLineBrush", t.Border);
        Set(w, "SelStrokeBrush", t.SelStroke);
        Set(w, "PanelBrush", t.Panel);
        Set(w, "FieldBrush", t.Field);
        Set(w, "CardBrush", t.Card);
        Set(w, "SidebarBrush", t.Sidebar);
        Set(w, "SegTrackBrush", dark ? Color.FromRgb(0x21, 0x23, 0x28) : Color.FromRgb(0xE9, 0xEA, 0xEC));
        Set(w, "SegSelBrush", dark ? Color.FromRgb(0x34, 0x36, 0x3C) : Colors.White);
        Set(w, "SeparatorBrush", t.Separator);
        Set(w, "CaptionBrush", t.Caption);
        Set(w, "UserBubbleBrush", t.UserBubble);
        Set(w, "UserBubbleTextBrush", t.UserBubbleText);
        // the ChatGPT-clone send cap: solid face + INVERTED glyph, mode-driven
        Set(w, "SendButtonFaceBrush", dark ? Colors.White : Color.FromRgb(0x0D, 0x0D, 0x0D));
        Set(w, "SendButtonGlyphBrush", dark ? Color.FromRgb(0x0D, 0x0D, 0x0D) : Colors.White);
        Set(w, "AvatarBrush", Appearance.Tint(t.Accent, 0x2A));
        Set(w, "CodeBrush", t.Code);
        Set(w, "WarnBrush", Color.FromArgb(0x2A, 0xCA, 0x50, 0x10));

        // gradient tokens — send cap light, orb, halo (all accent-derived)
        SetBrush(w, "SendBrush", LightenGradient(t.Accent, 0.22, (0, 0), (0.2, 1)));
        SetBrush(w, "OrbBrush", LightenGradient(t.Accent, 0.35, (0.3, 0), (0.3, 1)));
        var halo = new RadialGradientBrush();
        halo.GradientStops.Add(new GradientStop(Appearance.Tint(t.Accent, 0x42), 0.0));
        halo.GradientStops.Add(new GradientStop(Appearance.Tint(t.Accent, 0x14), 0.62));
        halo.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
        halo.Freeze();
        SetBrush(w, "GlowBrush", halo);

        // v3.0 — top-lit strokes live here: the window edge light-catch shared by
        // the launcher rim, the preview pane and the chat card stroke.
        SetBrush(w, "CardStrokeBrush", TopLitStroke(t.Border, dark, 0x30));
        SetBrush(w, "PreviewStrokeBrush", TopLitStroke(t.Border, dark, 0x26));

        return t;
    }

    private static bool TryColor(string hex, out Color c)
    {
        try { c = (Color)ColorConverter.ConvertFromString(hex); return true; }
        catch { c = default; return false; }
    }

    private static void Set(Window w, string key, Color c) => SetBrush(w, key, new SolidColorBrush(c));

    private static void SetBrush(Window w, string key, Brush b) => w.Resources[key] = b;

    private static Brush LightenGradient(Color accent, double lift, (double X, double Y) from, (double X, double Y) to)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(from.X, from.Y), EndPoint = new Point(to.X, to.Y) };
        b.GradientStops.Add(new GradientStop(Lift(accent, lift), 0.0));
        b.GradientStops.Add(new GradientStop(accent, 1.0));
        b.Freeze();
        return b;
    }

    /// <summary>accent lifted toward white — gradient light ends for caps/orbs.</summary>
    public static Color Lift(Color c, double f) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * f),
        (byte)(c.G + (255 - c.G) * f),
        (byte)(c.B + (255 - c.B) * f));

    /// <summary>
    /// v3.0 — THE EDGE SHINE. The minimal glow: a 1 px vertical gradient that starts
    /// as a light catch along the top edge and settles into the quiet hairline. No
    /// orbiting comet, no sampled perimeter, no idle CPU — just one frozen brush.
    /// </summary>
    public static Brush TopLitStroke(Color hairline, bool dark, byte topAlpha)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        if (dark)
        {
            b.GradientStops.Add(new GradientStop(Color.FromArgb(topAlpha, 0xFF, 0xFF, 0xFF), 0.0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), 0.10));
            b.GradientStops.Add(new GradientStop(hairline, 0.42));
        }
        else
        {
            b.GradientStops.Add(new GradientStop(Colors.White, 0.0));
            b.GradientStops.Add(new GradientStop(hairline, 0.40));
        }
        b.GradientStops.Add(new GradientStop(hairline, 1.0));
        b.Freeze();
        return b;
    }

    // ------------------------------------------------------------ fluent sync

    /// <summary>
    /// Keeps the WPF-UI Fluent layer on the same mode + accent as the Lumo palette,
    /// so its implicit control styles (buttons, toggles, scrollbars, tooltips) blend
    /// in instead of fighting the theme. Cheap no-op when nothing changed.
    /// </summary>
    public static void SyncFluent(ThemeSelect.ThemeSpec spec)
    {
        try
        {
            var key = (spec.Dark, spec.AccentHex);
            if (key == _fluentSynced) return;

            var theme = spec.Dark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light;
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(theme, Wpf.Ui.Controls.WindowBackdropType.None, false);
            try
            {
                var accent = Appearance.ParseAccent(spec.AccentHex);
                Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(accent, theme, false, false);
            }
            catch { /* accent sync is cosmetic — never fatal */ }

            _fluentSynced = key;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Theme.SyncFluent", ex);
        }
    }
}
