using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;

namespace Lumo.Services;

/// <summary>
/// Produces the Lumo tray icon.
///
/// v2.4.0-alpha.7 — the app art moved to the magic-wand icon (Assets/app.ico) in
/// alpha.6 via csproj ApplicationIcon, but that only stamps the exe/window icon;
/// the tray kept calling the runtime drawing below, so the old purple "L" tile
/// stayed in the notification area. The icon is now loaded from the embedded WPF
/// resource (the ico carries exact 16/24/32/48/64 frames; the tray asks for 32).
/// The legacy drawing survives only as a fallback if the pack resource is missing.
/// </summary>
public static class IconFactory
{
    public static Icon CreateAppIcon(int size = 32)
    {
        try
        {
            var sri = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (sri?.Stream is not null)
            {
                // Icon(Stream) reads the stream lazily and must keep it open for the
                // icon's lifetime — copy into a MemoryStream we deliberately never
                // dispose (a live MemoryStream holds only a managed buffer).
                using var src = sri.Stream;
                var copy = new MemoryStream();
                src.CopyTo(copy);
                copy.Position = 0;
                return new Icon(copy, size, size);
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("IconFactory.PackIcon", ex); }

        return CreateLegacyDrawnIcon(size);
    }

    /// <summary>The pre-alpha.6 drawn "L" tile — kept strictly as a fallback.</summary>
    public static Icon CreateLegacyDrawnIcon(int size = 32)
    {
        try
        {
            using var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                var rect = new RectangleF(1f, 1f, size - 2f, size - 2f);
                float r = size * 0.28f;

                using var path = Round(rect, r);
                using var fill = new LinearGradientBrush(rect, Color.FromArgb(0xFF, 0x7C, 0x6C, 0xFF), Color.FromArgb(0xFF, 0x54, 0x3F, 0xE0), 60f);
                g.FillPath(fill, path);

                // White "L"
                float bar = Math.Max(2f, size * 0.14f);
                using var lBrush = new SolidBrush(Color.White);
                float x = size * 0.28f;
                float y = size * 0.20f;
                float w = size * 0.44f;
                float h = size * 0.60f;
                g.FillRectangle(lBrush, x, y, bar, h);                    // vertical stem
                g.FillRectangle(lBrush, x, y + h - bar, w, bar);          // horizontal foot
            }

            var hIcon = bmp.GetHicon();
            var icon = Icon.FromHandle(hIcon);
            return (Icon)icon.Clone(); // detach from the temporary handle's lifetime
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private static GraphicsPath Round(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
