using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using Sharelink.Common;
using Sharelink.Theme;

namespace Sharelink.Controls;

/// <summary>Campo de busca arredondado com ícone de lupa.</summary>
internal sealed class SearchBox : Control
{
    private readonly TextBox _text;
    private bool _focused;

    public SearchBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _text = new TextBox { BorderStyle = BorderStyle.None };
        _text.TextChanged += (_, _) => OnTextChanged(EventArgs.Empty);
        _text.GotFocus += (_, _) => { _focused = true; Invalidate(); };
        _text.LostFocus += (_, _) => { _focused = false; Invalidate(); };
        Controls.Add(_text);
        Height = 34;
        Cursor = Cursors.IBeam;
    }

    [AllowNull]
    public override string Text
    {
        get => _text.Text;
        set => _text.Text = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Placeholder
    {
        get => _text.PlaceholderText;
        set => _text.PlaceholderText = value;
    }

    public void FocusText() => _text.Focus();

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        _text.Focus();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int icon = Height;
        _text.Font = Font;
        _text.SetBounds(icon, (Height - _text.PreferredHeight) / 2 + 1, Width - icon - 10, _text.PreferredHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Palette p = ThemeManager.Current;
        _text.BackColor = p.Input;
        _text.ForeColor = p.Text;

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var parentBg = new SolidBrush(Parent?.BackColor ?? p.Background))
            g.FillRectangle(parentBg, ClientRectangle);

        using GraphicsPath path = AppIcon.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f - 1);
        using (var fill = new SolidBrush(p.Input))
            g.FillPath(fill, path);
        using (var pen = new Pen(_focused ? p.Accent : p.Border, _focused ? 1.6f : 1f))
            g.DrawPath(pen, path);

        using Font icon = Glyphs.CreateFont(Height * 0.4f);
        TextRenderer.DrawText(g, Glyphs.Search, icon, new Rectangle(4, 0, Height - 4, Height), p.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
