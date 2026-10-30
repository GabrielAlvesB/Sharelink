using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Sharelink.Common;
using Sharelink.Theme;

namespace Sharelink.Gallery;

/// <summary>Miniaturas das capturas, geradas em segundo plano e guardadas em memória.</summary>
internal sealed class ThumbnailCache(Size size) : IDisposable
{
    private const int MaxEntries = 1500;

    private readonly Dictionary<string, (DateTime Stamp, Bitmap Thumb)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Size Size => size;

    public Bitmap? Get(GalleryItem item) =>
        _cache.TryGetValue(item.FullPath, out var entry) && entry.Stamp == item.Modified ? entry.Thumb : null;

    public void Put(GalleryItem item, Bitmap thumb)
    {
        if (_cache.Remove(item.FullPath, out var old))
            old.Thumb.Dispose();
        if (_cache.Count >= MaxEntries)
        {
            foreach (string key in _cache.Keys.Take(MaxEntries / 4).ToList())
                if (_cache.Remove(key, out var evicted))
                    evicted.Thumb.Dispose();
        }
        _cache[item.FullPath] = (item.Modified, thumb);
    }

    /// <summary>
    /// Miniatura com cantos arredondados, recortada para preencher o espaço.
    /// Lê o arquivo sem bloqueá-lo. Seguro chamar fora da thread da interface.
    /// </summary>
    public static Bitmap Render(GalleryItem item, Size size)
    {
        var thumb = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using Graphics g = Graphics.FromImage(thumb);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using GraphicsPath clip = AppIcon.RoundedRect(new RectangleF(0, 0, size.Width, size.Height), 8);
        g.SetClip(clip);

        if (item.Kind == GalleryItemKind.Video)
        {
            DrawPlaceholder(g, size, Glyphs.Video, "MP4");
            return thumb;
        }

        try
        {
            using Image image = LoadImage(item.FullPath);
            // "Cover": preenche a área inteira, cortando o excesso do centro.
            float scale = Math.Max((float)size.Width / image.Width, (float)size.Height / image.Height);
            float w = image.Width * scale, h = image.Height * scale;
            g.DrawImage(image, (size.Width - w) / 2, (size.Height - h) / 2, w, h);
            if (item.Kind == GalleryItemKind.Gif)
                DrawTag(g, size, "GIF");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException or UnauthorizedAccessException)
        {
            DrawPlaceholder(g, size, Glyphs.Image, "inválido");
        }
        return thumb;
    }

    /// <summary>Carrega uma cópia em memória (o arquivo fica livre para mover/excluir).</summary>
    public static Bitmap LoadImage(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        return new Bitmap(image);
    }

    private static void DrawPlaceholder(Graphics g, Size size, string glyph, string label)
    {
        using var bg = new LinearGradientBrush(new Rectangle(Point.Empty, size), Color.FromArgb(52, 52, 64), Color.FromArgb(30, 30, 38), 90f);
        g.FillRectangle(bg, 0, 0, size.Width, size.Height);
        using Font icon = Glyphs.CreateFont(size.Height / 3.2f);
        DrawCentered(g, glyph, icon, new RectangleF(0, 0, size.Width, size.Height), Color.FromArgb(220, 220, 230));
        DrawTag(g, size, label);
    }

    private static void DrawTag(Graphics g, Size size, string text)
    {
        using var font = new Font("Segoe UI", Math.Max(9, size.Height / 11f), FontStyle.Bold, GraphicsUnit.Pixel);
        SizeF textSize = g.MeasureString(text, font);
        var tag = new RectangleF(8, size.Height - textSize.Height - 10, textSize.Width + 4, textSize.Height + 2);
        using GraphicsPath path = AppIcon.RoundedRect(tag, 4);
        using var bg = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
        g.FillPath(bg, path);
        DrawCentered(g, text, font, tag, Color.White);
    }

    // GDI+ (DrawString) respeita a transparência do bitmap; o TextRenderer (GDI) não.
    private static void DrawCentered(Graphics g, string text, Font font, RectangleF area, Color color)
    {
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, area, format);
    }

    public void Dispose()
    {
        foreach (var entry in _cache.Values)
            entry.Thumb.Dispose();
        _cache.Clear();
    }
}
