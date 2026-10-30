using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Sharelink.Common;

namespace Sharelink.Capture;

/// <summary>
/// Tela "congelada" sobre todos os monitores. Arraste para selecionar uma área,
/// ou clique para pegar a janela sob o cursor. Esc ou botão direito cancela.
/// </summary>
internal sealed class RegionSelectorForm : OverlayForm
{
    private const int MagnifierPixels = 15;
    private const int MagnifierZoom = 8;
    private const int MinSelection = 3;

    private readonly Bitmap _screenshot;
    private readonly Bitmap _dimmed;
    private readonly List<Rectangle> _windows;
    private readonly Point _origin;
    private readonly string _hint;
    private readonly Pen _borderPen = new(Color.FromArgb(52, 152, 255), 2) { Alignment = PenAlignment.Inset };
    private readonly Font _font = new(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 10f);

    private Point _mouse;
    private Point _dragStart;
    private bool _dragging;
    private Rectangle _selection;
    private Rectangle? _hoverWindow;
    private Rectangle _lastDirty;
    private bool _hintHidden;

    /// <summary>Área escolhida, em coordenadas da tela.</summary>
    public Rectangle SelectedArea { get; private set; }

    /// <param name="screenshot">Captura da tela virtual inteira (o form não assume a posse).</param>
    public RegionSelectorForm(Bitmap screenshot, Rectangle virtualScreen, List<Rectangle> windows, string hint)
    {
        _screenshot = screenshot;
        _origin = virtualScreen.Location;
        _hint = hint;
        _windows = windows.Select(w => w with { X = w.X - _origin.X, Y = w.Y - _origin.Y }).ToList();
        _dimmed = CreateDimmed(screenshot);

        Bounds = virtualScreen;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.UserPaint, true);
    }

    private static Bitmap CreateDimmed(Bitmap source)
    {
        var dimmed = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        using Graphics g = Graphics.FromImage(dimmed);
        g.DrawImageUnscaled(source, 0, 0);
        using var shade = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
        g.FillRectangle(shade, 0, 0, source.Width, source.Height);
        return dimmed;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        _mouse = PointToClient(MousePosition);
        UpdateHover();
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
            Finish(Rectangle.Empty);
        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragStart = e.Location;
            _dragging = true;
            _selection = Rectangle.Empty;
        }
        else if (e.Button == MouseButtons.Right)
        {
            if (_dragging)
            {
                _dragging = false;
                _selection = Rectangle.Empty;
                InvalidateChanged();
            }
            else
            {
                Finish(Rectangle.Empty);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _mouse = e.Location;
        if (_dragging)
            _selection = RectangleMath.FromPoints(_dragStart, e.Location);
        else
            UpdateHover();
        InvalidateChanged();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_dragging)
            return;

        _dragging = false;
        if (_selection.Width >= MinSelection && _selection.Height >= MinSelection)
            Finish(_selection);
        else if (_hoverWindow is Rectangle window)
            Finish(window);
    }

    private void UpdateHover() =>
        _hoverWindow = _windows.Select(w => (Rectangle?)w).FirstOrDefault(w => w!.Value.Contains(_mouse));

    private void Finish(Rectangle clientArea)
    {
        if (clientArea.IsEmpty)
        {
            DialogResult = DialogResult.Cancel;
        }
        else
        {
            clientArea.Intersect(new Rectangle(Point.Empty, _screenshot.Size));
            SelectedArea = clientArea with { X = clientArea.X + _origin.X, Y = clientArea.Y + _origin.Y };
            DialogResult = DialogResult.OK;
        }
        Close();
    }

    private Rectangle CurrentHighlight => _dragging ? _selection : _hoverWindow ?? Rectangle.Empty;

    /// <summary>Redesenha só o que mudou: repintar vários monitores a cada movimento seria lento.</summary>
    private void InvalidateChanged()
    {
        Rectangle dirty = CurrentHighlight;
        dirty = Union(dirty, LabelBounds(CurrentHighlight));
        dirty = Union(dirty, MagnifierBounds());
        if (HintBounds().Contains(_mouse) != _hintHidden)
        {
            _hintHidden = !_hintHidden;
            dirty = Union(dirty, HintBounds());
        }
        dirty.Inflate(4, 4);

        Invalidate(Union(dirty, _lastDirty));
        _lastDirty = dirty;
    }

    private static Rectangle Union(Rectangle a, Rectangle b) =>
        a.IsEmpty ? b : b.IsEmpty ? a : Rectangle.Union(a, b);

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Tudo é desenhado em OnPaint.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Rectangle clip = e.ClipRectangle;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.DrawImage(_dimmed, clip, clip, GraphicsUnit.Pixel);

        Rectangle highlight = CurrentHighlight;
        if (!highlight.IsEmpty)
        {
            Rectangle visible = Rectangle.Intersect(highlight, clip);
            if (!visible.IsEmpty)
                g.DrawImage(_screenshot, visible, visible, GraphicsUnit.Pixel);
            g.CompositingMode = CompositingMode.SourceOver;
            g.DrawRectangle(_borderPen, highlight);
            DrawLabel(g, highlight, $"{highlight.Width} × {highlight.Height}");
        }

        g.CompositingMode = CompositingMode.SourceOver;
        DrawHint(g);
        DrawMagnifier(g);
    }

    private Rectangle HintBounds()
    {
        Rectangle primary = Screen.PrimaryScreen?.Bounds ?? Bounds;
        Size size = TextRenderer.MeasureText(_hint, _font);
        return new Rectangle(primary.X - _origin.X + (primary.Width - size.Width) / 2 - 12, primary.Y - _origin.Y + 16,
            size.Width + 24, size.Height + 12);
    }

    private void DrawHint(Graphics g)
    {
        Rectangle box = HintBounds();
        if (box.Contains(_mouse))
            return;
        using var bg = new SolidBrush(Color.FromArgb(200, 20, 20, 20));
        g.FillRectangle(bg, box);
        TextRenderer.DrawText(g, _hint, _font, box, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private Rectangle LabelBounds(Rectangle highlight)
    {
        if (highlight.IsEmpty)
            return Rectangle.Empty;
        Size size = TextRenderer.MeasureText("00000 × 00000", _font);
        int y = highlight.Y - size.Height - 6 >= 0 ? highlight.Y - size.Height - 6 : highlight.Y + 4;
        return new Rectangle(highlight.X, y, size.Width + 8, size.Height + 4);
    }

    private void DrawLabel(Graphics g, Rectangle highlight, string text)
    {
        Rectangle box = LabelBounds(highlight);
        using var bg = new SolidBrush(Color.FromArgb(200, 20, 20, 20));
        g.FillRectangle(bg, box);
        TextRenderer.DrawText(g, text, _font, box, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private Rectangle MagnifierBounds()
    {
        int size = MagnifierPixels * MagnifierZoom;
        int textHeight = TextRenderer.MeasureText("X", _font).Height * 2 + 6;
        int x = _mouse.X + 24, y = _mouse.Y + 24;
        if (x + size > ClientSize.Width) x = _mouse.X - 24 - size;
        if (y + size + textHeight > ClientSize.Height) y = _mouse.Y - 24 - size - textHeight;
        return new Rectangle(x, y, size, size + textHeight);
    }

    private void DrawMagnifier(Graphics g)
    {
        Rectangle bounds = MagnifierBounds();
        int size = MagnifierPixels * MagnifierZoom;
        var zoomArea = new Rectangle(bounds.X, bounds.Y, size, size);
        var source = new Rectangle(_mouse.X - MagnifierPixels / 2, _mouse.Y - MagnifierPixels / 2, MagnifierPixels, MagnifierPixels);

        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.FillRectangle(Brushes.Black, zoomArea);
        g.DrawImage(_screenshot, zoomArea, source, GraphicsUnit.Pixel);
        g.PixelOffsetMode = PixelOffsetMode.Default;

        int center = MagnifierPixels / 2 * MagnifierZoom;
        using var cross = new Pen(Color.FromArgb(180, 52, 152, 255), 1);
        g.DrawRectangle(cross, zoomArea.X + center, zoomArea.Y + center, MagnifierZoom, MagnifierZoom);
        g.DrawRectangle(Pens.White, zoomArea);

        Point screenPos = new(_mouse.X + _origin.X, _mouse.Y + _origin.Y);
        Color pixel = _screenshot.GetPixel(Math.Clamp(_mouse.X, 0, _screenshot.Width - 1), Math.Clamp(_mouse.Y, 0, _screenshot.Height - 1));
        var textArea = new Rectangle(bounds.X, zoomArea.Bottom, size, bounds.Height - size);
        using var bg = new SolidBrush(Color.FromArgb(220, 20, 20, 20));
        g.FillRectangle(bg, textArea);
        TextRenderer.DrawText(g, $"X {screenPos.X}  Y {screenPos.Y}\n#{pixel.R:X2}{pixel.G:X2}{pixel.B:X2}",
            _font, textArea, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dimmed.Dispose();
            _borderPen.Dispose();
            _font.Dispose();
        }
        base.Dispose(disposing);
    }
}
