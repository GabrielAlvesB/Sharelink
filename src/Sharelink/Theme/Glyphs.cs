using System.Drawing.Text;

namespace Sharelink.Theme;

/// <summary>
/// Ícones da fonte de símbolos do próprio Windows (Segoe Fluent Icons no Windows 11,
/// Segoe MDL2 Assets no 10). Nenhum arquivo de imagem externo.
/// </summary>
internal static class Glyphs
{
    public const string Camera = "";
    public const string Monitor = "";
    public const string Link = "";
    public const string Video = "";
    public const string Gif = "";
    public const string Record = "";
    public const string Folder = "";
    public const string Settings = "";
    public const string Copy = "";
    public const string Edit = "";
    public const string Delete = "";
    public const string OpenExternal = "";
    public const string Search = "";
    public const string Close = "";
    public const string Check = "";
    public const string Undo = "";
    public const string Redo = "";
    public const string Save = "";
    public const string Arrow = "";
    public const string Rectangle = "";
    public const string Ellipse = "";
    public const string Text = "";
    public const string Highlight = "";
    public const string Pixelate = "";
    public const string Blur = "";
    public const string Upload = "";
    public const string Cloud = "";
    public const string Info = "";
    public const string Palette = "";
    public const string Keyboard = "";
    public const string Shield = "";
    public const string Image = "";
    public const string Stop = "";
    public const string Refresh = "";

    private static string? _fontName;

    public static string FontName => _fontName ??= DetectFont();

    private static string DetectFont()
    {
        using var fonts = new InstalledFontCollection();
        string[] names = fonts.Families.Select(f => f.Name).ToArray();
        return names.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }

    public static Font CreateFont(float pixelSize) => new(FontName, pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);

    /// <summary>Desenha um ícone num bitmap (para ToolStrip e menus).</summary>
    public static Bitmap Render(string glyph, int size, Color color)
    {
        var bmp = new Bitmap(size, size);
        using Graphics g = Graphics.FromImage(bmp);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using Font font = CreateFont(size * 0.8f);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), format);
        return bmp;
    }
}
