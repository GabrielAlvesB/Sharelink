using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Sharelink.Editor.Shapes;

/// <summary>Efeito aplicado sobre uma área da imagem original. O resultado é guardado em cache.</summary>
internal abstract class EffectShape(Point start) : BoxShape(start)
{
    private Bitmap? _cache;
    private Rectangle _cacheBounds;

    public override void Draw(Graphics g, Bitmap source)
    {
        Rectangle area = Rectangle.Intersect(Bounds, new Rectangle(Point.Empty, source.Size));
        if (area.Width < 1 || area.Height < 1)
            return;

        if (_cache is null || _cacheBounds != area)
        {
            _cache?.Dispose();
            _cache = Apply(source, area);
            _cacheBounds = area;
        }
        g.DrawImage(_cache, area.Location);
    }

    protected abstract Bitmap Apply(Bitmap source, Rectangle area);

    /// <summary>Copia os pixels da área para um array BGRA, aplica a transformação e devolve um novo bitmap.</summary>
    protected static Bitmap Transform(Bitmap source, Rectangle area, Action<byte[], int, int, int> transform)
    {
        Bitmap result = source.Clone(area, PixelFormat.Format32bppArgb);
        BitmapData data = result.LockBits(new Rectangle(Point.Empty, area.Size), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            transform(pixels, area.Width, area.Height, data.Stride);
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            result.UnlockBits(data);
        }
        return result;
    }
}

/// <summary>
/// Pixelar: cada bloco vira a média das cores. É a forma mais segura de esconder
/// texto sensível, porque a informação original é descartada.
/// </summary>
internal sealed class PixelateShape(Point start) : EffectShape(start)
{
    protected override Bitmap Apply(Bitmap source, Rectangle area)
    {
        int block = Math.Max(8, (int)(Thickness * 3));
        return Transform(source, area, (px, w, h, stride) =>
        {
            for (int by = 0; by < h; by += block)
            for (int bx = 0; bx < w; bx += block)
            {
                int bw = Math.Min(block, w - bx), bh = Math.Min(block, h - by);
                long b = 0, g = 0, r = 0;
                for (int y = by; y < by + bh; y++)
                for (int x = bx; x < bx + bw; x++)
                {
                    int i = y * stride + x * 4;
                    b += px[i]; g += px[i + 1]; r += px[i + 2];
                }
                int n = bw * bh;
                for (int y = by; y < by + bh; y++)
                for (int x = bx; x < bx + bw; x++)
                {
                    int i = y * stride + x * 4;
                    px[i] = (byte)(b / n); px[i + 1] = (byte)(g / n); px[i + 2] = (byte)(r / n); px[i + 3] = 255;
                }
            }
        });
    }
}

/// <summary>Desfoque forte (box blur em 3 passadas, aproxima um gaussiano).</summary>
internal sealed class BlurShape(Point start) : EffectShape(start)
{
    protected override Bitmap Apply(Bitmap source, Rectangle area)
    {
        int radius = Math.Max(6, (int)(Thickness * 2.5f));
        return Transform(source, area, (px, w, h, stride) =>
        {
            var temp = new byte[px.Length];
            for (int pass = 0; pass < 3; pass++)
            {
                BoxBlur(px, temp, w, h, stride, radius, horizontal: true);
                BoxBlur(temp, px, w, h, stride, radius, horizontal: false);
            }
        });
    }

    private static void BoxBlur(byte[] src, byte[] dst, int w, int h, int stride, int radius, bool horizontal)
    {
        int lines = horizontal ? h : w, length = horizontal ? w : h;
        for (int line = 0; line < lines; line++)
        {
            int Index(int pos) => horizontal ? line * stride + pos * 4 : pos * stride + line * 4;

            for (int c = 0; c < 3; c++)
            {
                int sum = 0, count = 0;
                for (int k = 0; k <= Math.Min(radius, length - 1); k++) { sum += src[Index(k) + c]; count++; }
                for (int pos = 0; pos < length; pos++)
                {
                    dst[Index(pos) + c] = (byte)(sum / count);
                    int add = pos + radius + 1, remove = pos - radius;
                    if (add < length) { sum += src[Index(add) + c]; count++; }
                    if (remove >= 0) { sum -= src[Index(remove) + c]; count--; }
                }
            }
            for (int pos = 0; pos < length; pos++)
                dst[Index(pos) + 3] = 255;
        }
    }
}
