using System.Drawing;
using System.Drawing.Drawing2D;

namespace Lumo.Services;

/// <summary>Draws the Lumo tray/window icon at runtime — no .ico file dependency.</summary>
public static class IconFactory
{
    public static Icon CreateAppIcon(int size = 32)
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
