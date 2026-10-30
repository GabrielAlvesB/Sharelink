using System.Drawing.Drawing2D;
using Sharelink.Common;

namespace Sharelink.Theme;

/// <summary>Desenho de barras de ferramentas, menus e barra de status com as cores do tema.</summary>
internal sealed class ThemedRenderer(Palette p) : ToolStripProfessionalRenderer(new ThemedColorTable(p))
{
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown)
        {
            using var pen = new Pen(p.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }
        // Barras normais: sem borda (visual limpo).
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.ToolStrip is ToolStripDropDown ? p.Surface : e.ToolStrip.BackColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is not ToolStripButton button)
        {
            base.OnRenderButtonBackground(e);
            return;
        }
        bool active = button.Checked || button.Selected || button.Pressed;
        if (!active)
            return;

        var bounds = new Rectangle(1, 1, e.Item.Width - 2, e.Item.Height - 2);
        Color fill = button.Checked ? p.Selection : p.SurfaceHover;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = AppIcon.RoundedRect(bounds, 6);
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);
        if (button.Checked)
        {
            using var pen = new Pen(p.Accent);
            e.Graphics.DrawPath(pen, path);
        }
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled)
            return;
        var bounds = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = AppIcon.RoundedRect(bounds, 5);
        using var brush = new SolidBrush(p.SurfaceHover);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? p.Text : p.TextMuted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = p.TextMuted;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(p.Border);
        Rectangle r = e.Item.ContentRectangle;
        if (e.Vertical)
            e.Graphics.DrawLine(pen, r.Width / 2, r.Top + 4, r.Width / 2, r.Bottom - 4);
        else
            e.Graphics.DrawLine(pen, r.Left + 8, r.Height / 2, r.Right - 8, r.Height / 2);
    }

    private sealed class ThemedColorTable(Palette p) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => p.Surface;
        public override Color ImageMarginGradientBegin => p.Surface;
        public override Color ImageMarginGradientMiddle => p.Surface;
        public override Color ImageMarginGradientEnd => p.Surface;
        public override Color MenuBorder => p.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => p.SurfaceHover;
        public override Color StatusStripGradientBegin => p.Background;
        public override Color StatusStripGradientEnd => p.Background;
        public override Color ToolStripGradientBegin => p.Background;
        public override Color ToolStripGradientMiddle => p.Background;
        public override Color ToolStripGradientEnd => p.Background;
        public override Color SeparatorDark => p.Border;
        public override Color SeparatorLight => p.Border;
    }
}
