using Lumo.Core;
using Lumo.Services;
using System.Windows;
using System.Windows.Media;

namespace Lumo.UI;

/// <summary>
/// v3.0 — draws one persona face (see Core/PersonaFaces). A tiny vector
/// character: tinted body + dark eyes/mouth + blush cheeks, rendered in a 24×24
/// design space scaled to the element. Static (no animation) — avatars, pickers,
/// menu icons. The animated sibling is <see cref="MascotView"/>.
/// </summary>
public class PersonaFaceView : FrameworkElement
{
    public static readonly DependencyProperty FaceIdProperty = DependencyProperty.Register(
        nameof(FaceId), typeof(string), typeof(PersonaFaceView),
        new FrameworkPropertyMetadata(Core.PersonaFaces.DefaultFace, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>#RRGGBB hex or "" ("" = the window's AccentBrush — follows the theme).</summary>
    public static readonly DependencyProperty PersonaColorProperty = DependencyProperty.Register(
        nameof(PersonaColor), typeof(string), typeof(PersonaFaceView),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public string FaceId { get => (string)GetValue(FaceIdProperty); set => SetValue(FaceIdProperty, value); }
    public string PersonaColor { get => (string)GetValue(PersonaColorProperty); set => SetValue(PersonaColorProperty, value); }

    private static readonly Dictionary<string, Geometry> GeoCache = new();

    protected static Geometry Geo(string path)
    {
        if (!GeoCache.TryGetValue(path, out var g))
        {
            g = Geometry.Parse(path);
            g.Freeze();
            GeoCache[path] = g;
        }
        return g;
    }

    /// <summary>Resolves the tint: explicit hex wins, otherwise the ambient accent.</summary>
    protected Color ResolveTint()
    {
        if (!string.IsNullOrWhiteSpace(PersonaColor))
        {
            try { return (Color)ColorConverter.ConvertFromString(PersonaColor); }
            catch { /* fall through */ }
        }
        if (TryFindResource("AccentBrush") is SolidColorBrush b) return b.Color;
        return Color.FromRgb(0xFF, 0x63, 0x63);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 4 || double.IsNaN(size)) return;

        double scale = size / 24.0;
        double ox = (ActualWidth - size) / 2.0, oy = (ActualHeight - size) / 2.0;

        dc.PushTransform(new TranslateTransform(ox, oy));
        dc.PushTransform(new ScaleTransform(scale, scale));

        var face = PersonaFaces.Resolve(FaceId);
        var tint = ResolveTint();

        // body: accent tint with a light catch at the top (the family material)
        var body = Geo(face.Body);
        var fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0.15, 1),
        };
        fill.GradientStops.Add(new GradientStop(ThemeService.Lift(tint, 0.28), 0.0));
        fill.GradientStops.Add(new GradientStop(tint, 1.0));
        fill.Freeze();
        dc.DrawGeometry(fill, null, body);

        // features ink: near-black in both modes (the body is the mid-tone here)
        var ink = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x28));
        ink.Freeze();
        var pen = new Pen(ink, 1.9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();

        dc.DrawGeometry(ink, null, Geo(EyesPath(face.Eyes)));
        DrawMouth(dc, face.Mouth, ink);

        // blush cheeks — a whisper of white, keeps the face friendly on any tint
        var blush = new SolidColorBrush(Color.FromArgb(0x5A, 0xFF, 0xFF, 0xFF));
        blush.Freeze();
        dc.DrawRoundedRectangle(blush, null, new Rect(4.6, 13.4, 2.8, 1.4), 0.7, 0.7);
        dc.DrawRoundedRectangle(blush, null, new Rect(16.6, 13.4, 2.8, 1.4), 0.7, 0.7);

        dc.Pop();
        dc.Pop();
    }

    protected static string EyesPath(string style) => style switch
    {
        // ∩ arcs — the "happy closed" eyes
        "happy" => "M6.1,11.8 C7.2,9.6 9.6,9.6 10.7,11.8 M13.3,11.8 C14.4,9.6 16.8,9.6 17.9,11.8",
        // flat lines — sleepy (moon)
        "sleepy" => "M6.4,11 L10.4,11 M13.6,11 L17.6,11",
        // filled squares — bot
        "square" => "M6.4,9.4 L10.6,9.4 L10.6,13.2 L6.4,13.2 Z M13.4,9.4 L17.6,9.4 L17.6,13.2 L13.4,13.2 Z",
        // default: filled round eyes (both circles in one path via two arcs)
        _ => "M8.5,9.3 C9.8,9.3 10.7,10.2 10.7,11.4 C10.7,12.6 9.8,13.5 8.5,13.5 C7.2,13.5 6.3,12.6 6.3,11.4 C6.3,10.2 7.2,9.3 8.5,9.3 Z M15.5,9.3 C16.8,9.3 17.7,10.2 17.7,11.4 C17.7,12.6 16.8,13.5 15.5,13.5 C14.2,13.5 13.3,12.6 13.3,11.4 C13.3,10.2 14.2,9.3 15.5,9.3 Z",
    };

    protected static void DrawMouth(DrawingContext dc, string style, Brush ink)
    {
        var pen = new Pen(ink, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        switch (style)
        {
            case "open":   // little "o" — ghost
                dc.DrawEllipse(ink, null, new Point(12, 15.4), 1.5, 1.9);
                break;
            case "grin":   // filled smile wedge — bot / star
                dc.DrawGeometry(ink, null, Geo("M8.9,14.6 C9.7,17.4 14.3,17.4 15.1,14.6 C13.1,15.5 10.9,15.5 8.9,14.6 Z"));
                break;
            case "cat":    // ω — cat
                dc.DrawGeometry(null, pen, Geo("M9.2,15.1 C9.8,14.4 10.5,14.4 11.1,15.0 M12.9,15.0 C13.5,14.4 14.2,14.4 14.8,15.1"));
                break;
            default:       // smile — stroke arc
                dc.DrawGeometry(null, pen, Geo("M8.9,14.7 C10.1,16.3 13.9,16.3 15.1,14.7"));
                break;
        }
    }
}
