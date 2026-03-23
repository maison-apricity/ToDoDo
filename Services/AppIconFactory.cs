using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ToDoDo.Services;

public static class AppIconFactory
{
    public static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var shadowBrush = new SolidBrush(Color.FromArgb(65, 0, 0, 0));
        graphics.FillEllipse(shadowBrush, 9, 10, 46, 46);

        using var backgroundBrush = new LinearGradientBrush(
            new Rectangle(8, 8, 48, 48),
            Color.FromArgb(255, 104, 123, 255),
            Color.FromArgb(255, 84, 227, 195),
            45f);

        using var borderPen = new Pen(Color.FromArgb(150, 255, 255, 255), 1.8f);
        var rect = new RectangleF(8, 8, 48, 48);
        using var path = RoundedRect(rect, 16f);
        graphics.FillPath(backgroundBrush, path);
        graphics.DrawPath(borderPen, path);

        using var linePen = new Pen(Color.FromArgb(245, 255, 255, 255), 3.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        graphics.DrawLine(linePen, 21, 25, 43, 25);
        graphics.DrawLine(linePen, 21, 34, 39, 34);

        using var accentBrush = new SolidBrush(Color.FromArgb(255, 19, 29, 48));
        graphics.FillEllipse(accentBrush, 16, 19, 8, 8);
        graphics.FillEllipse(accentBrush, 16, 30, 8, 8);

        var iconHandle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(iconHandle).Clone();
        }
        finally
        {
            Win32.DestroyIcon(iconHandle);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}
