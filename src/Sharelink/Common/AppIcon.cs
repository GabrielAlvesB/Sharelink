using System.Drawing.Drawing2D;
using Sharelink.Native;

namespace Sharelink.Common;

/// <summary>Ícone desenhado em código: evita arquivos binários de origem desconhecida no projeto.</summary>
internal static class AppIcon
{
    private static Icon? _icon;

    public static Icon Get() => _icon ??= Create(32);

    private static Icon Create(int size)
    {
        using var bmp = new Bitmap(size, size);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using (var path = RoundedRect(new RectangleF(1, 1, size - 2, size - 2), size / 5f))
            using (var fill = new LinearGradientBrush(new Rectangle(0, 0, size, size),
                       Color.FromArgb(52, 120, 246), Color.FromArgb(30, 64, 175), 45f))
                g.FillPath(fill, path);

            // Cantos de "visor" de câmera.
            float m = size * 0.24f, len = size * 0.18f, w = size / 11f;
            using var pen = new Pen(Color.White, w) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float a = m, b = size - m;
            g.DrawLines(pen, [new PointF(a, a + len), new PointF(a, a), new PointF(a + len, a)]);
            g.DrawLines(pen, [new PointF(b - len, a), new PointF(b, a), new PointF(b, a + len)]);
            g.DrawLines(pen, [new PointF(a, b - len), new PointF(a, b), new PointF(a + len, b)]);
            g.DrawLines(pen, [new PointF(b - len, b), new PointF(b, b), new PointF(b, b - len)]);
            using var dot = new SolidBrush(Color.White);
            float r = size * 0.09f;
            g.FillEllipse(dot, size / 2f - r, size / 2f - r, r * 2, r * 2);
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using Icon temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
