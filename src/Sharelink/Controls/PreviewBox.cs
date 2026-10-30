using System.ComponentModel;
using System.Drawing.Drawing2D;
using Sharelink.Common;
using Sharelink.Theme;

namespace Sharelink.Controls;

/// <summary>Prévia da imagem com cantos arredondados, ajustada sem distorcer.</summary>
internal sealed class PreviewBox : Control
{
    private Image? _image;

    public PreviewBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>A prévia assume a posse da imagem (descarta a anterior).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? Image
    {
        get => _image;
        set
        {
            Image? old = _image;
            _image = value;
            old?.Dispose();
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Placeholder { get; set; } = "";

    protected override void OnPaint(PaintEventArgs e)
    {
        Palette p = ThemeManager.Current;
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using (var parentBg = new SolidBrush(Parent?.BackColor ?? p.Background))
            g.FillRectangle(parentBg, ClientRectangle);

        using GraphicsPath path = AppIcon.RoundedRect(new RectangleF(0, 0, Width - 1, Height - 1), 10);
        using (var bg = new SolidBrush(p.CanvasBackground))
            g.FillPath(bg, path);

        if (_image is null)
        {
            using Font icon = Glyphs.CreateFont(Height / 5f);
            TextRenderer.DrawText(g, Glyphs.Image, icon, ClientRectangle with { Height = Height - Font.Height * 2 }, p.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, Placeholder, Font, ClientRectangle with { Y = Height / 2 + Font.Height / 2, Height = Font.Height * 2 }, p.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
            return;
        }

        g.SetClip(path);
        float scale = Math.Min((float)(Width - 16) / _image.Width, (float)(Height - 16) / _image.Height);
        scale = Math.Min(scale, 4f);
        float w = _image.Width * scale, h = _image.Height * scale;
        g.DrawImage(_image, (Width - w) / 2, (Height - h) / 2, w, h);
        g.ResetClip();
        using var border = new Pen(p.Border);
        g.DrawPath(border, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _image?.Dispose();
        base.Dispose(disposing);
    }
}
