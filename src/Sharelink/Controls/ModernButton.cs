using System.ComponentModel;
using System.Drawing.Drawing2D;
using Sharelink.Common;
using Sharelink.Theme;

namespace Sharelink.Controls;

internal enum ButtonVariant
{
    /// <summary>Ação principal (cor de destaque).</summary>
    Primary,
    /// <summary>Ação comum (fundo de superfície com borda).</summary>
    Secondary,
    /// <summary>Só texto/ícone; fundo aparece ao passar o mouse.</summary>
    Ghost,
    /// <summary>Ação destrutiva (excluir, apagar link).</summary>
    Danger,
    /// <summary>Filtro/aba: alterna entre marcado e desmarcado.</summary>
    Chip,
}

/// <summary>Botão arredondado com ícone, desenhado com as cores do tema.</summary>
internal sealed class ModernButton : Button
{
    private bool _hover;
    private bool _pressed;
    private bool _checked;

    public ModernButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Height = 34;
        Padding = new Padding(12, 0, 12, 0);
        BackColor = Color.Transparent;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonVariant Variant { get; set { field = value; Invalidate(); } } = ButtonVariant.Secondary;

    /// <summary>Caractere da fonte de ícones (<see cref="Glyphs"/>). Vazio = sem ícone.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Glyph { get; set { field = value; Invalidate(); } } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            _checked = value;
            Invalidate();
        }
    }

    /// <summary>Ajusta a largura ao texto + ícone automaticamente (inclusive quando a escala de DPI muda a fonte).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AutoWidth { get; set { field = value; FitWidth(); } }

    private void FitWidth()
    {
        if (AutoWidth)
            Width = PreferredWidth;
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitWidth(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitWidth(); }
    protected override void OnPaddingChanged(EventArgs e) { base.OnPaddingChanged(e); FitWidth(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); FitWidth(); }

    /// <summary>Alinhar conteúdo à esquerda (listas de ações) em vez de centralizar.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AlignLeft { get; set { field = value; Invalidate(); } }

    /// <summary>Largura ideal para o texto + ícone.</summary>
    public int PreferredWidth
    {
        get
        {
            int text = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font).Width;
            int icon = Glyph.Length > 0 ? IconSize + (text > 0 ? 8 : 0) : 0;
            return Padding.Horizontal + icon + text + 2;
        }
    }

    private int IconSize => (int)(Font.Height * 1.05f);

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Palette p = ThemeManager.Current;
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Fundo do pai, para os cantos arredondados não ficarem com "quadrado" atrás.
        using (var parentBg = new SolidBrush(Parent?.BackColor ?? p.Background))
            g.FillRectangle(parentBg, ClientRectangle);

        (Color fill, Color border, Color fore) = Colors(p);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (GraphicsPath path = AppIcon.RoundedRect(rect, Math.Min(8, Height / 2f - 1)))
        {
            if (fill.A > 0)
            {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }
            if (border.A > 0)
            {
                using var pen = new Pen(border);
                g.DrawPath(pen, path);
            }
            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(p.Accent, 2);
                g.DrawPath(focus, path);
            }
        }

        int textWidth = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font).Width;
        int iconWidth = Glyph.Length > 0 ? IconSize : 0;
        int gap = iconWidth > 0 && textWidth > 0 ? 8 : 0;
        int content = iconWidth + gap + textWidth;
        int x = AlignLeft ? Padding.Left : Math.Max(Padding.Left, (Width - content) / 2);

        if (iconWidth > 0)
        {
            using Font iconFont = Glyphs.CreateFont(IconSize * 0.85f);
            TextRenderer.DrawText(g, Glyph, iconFont, new Rectangle(x, 0, iconWidth, Height), fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += iconWidth + gap;
        }
        if (textWidth > 0)
        {
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x - Padding.Right / 2, Height), fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    private (Color Fill, Color Border, Color Fore) Colors(Palette p)
    {
        if (!Enabled)
        {
            return Variant == ButtonVariant.Ghost
                ? (Color.Transparent, Color.Transparent, Dim(p))
                : (p.Surface, p.Border, Dim(p));
        }

        bool active = _hover || _pressed;
        return Variant switch
        {
            ButtonVariant.Primary => (_pressed ? p.Accent : active ? p.AccentHover : p.Accent, Color.Transparent, p.AccentText),
            ButtonVariant.Danger => (active ? p.Danger : Color.FromArgb(p.IsDark ? 40 : 22, p.Danger), Color.FromArgb(120, p.Danger),
                active ? Color.White : p.Danger),
            ButtonVariant.Ghost => (active ? p.SurfaceHover : Color.Transparent, Color.Transparent, p.Text),
            ButtonVariant.Chip => _checked
                ? (p.Selection, p.Accent, p.Text)
                : (active ? p.SurfaceHover : Color.Transparent, p.Border, p.TextMuted),
            _ => (active ? p.SurfaceHover : p.Surface, p.Border, p.Text),
        };
    }

    private static Color Dim(Palette p) => p.IsDark ? Color.FromArgb(110, 110, 120) : Color.FromArgb(160, 164, 172);
}
