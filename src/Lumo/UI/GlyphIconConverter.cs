using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Lumo.UI;

/// <summary>
/// v1.7 — XAML bridge for the vector icon set: converts a ResultItem's glyph key
/// (set by the search engine: "A", "F", "⚡", "⚙"…) into a frozen StreamGeometry
/// for the row icon <see cref="System.Windows.Shapes.Path"/>.
/// </summary>
public sealed class GlyphIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try { return VectorIcons.ForGlyph(value as string); }
        catch { return null; }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// v2.4.0-alpha.2 — uppercases section-header titles ("Apps" → "APPS"), matching
/// Raycast's root-search header convention (11px, 600, muted, uppercase).
/// WPF has no letter-spacing property, so the casing carries the identity.
/// </summary>
public sealed class UpperTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s ? s.ToUpperInvariant() : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value; // headers are read-only rows; a round-trip passthrough is enough
}
