using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Sharelink.Common;
using Sharelink.Gallery;
using Sharelink.Theme;

namespace Sharelink.Controls;

/// <summary>
/// Grade de miniaturas agrupada por data, desenhada do zero (rápida e com o visual do tema).
/// Só os itens visíveis são desenhados e só eles pedem miniatura.
/// </summary>
internal sealed class GalleryView : ScrollableControl
{
    private readonly record struct Tile(Rectangle Bounds, int Index);
    private readonly record struct Header(Rectangle Bounds, string Text, int Count);

    private List<GalleryItem> _items = [];
    private readonly List<Tile> _tiles = [];
    private readonly List<Header> _headers = [];
    private readonly HashSet<int> _selected = [];
    private Func<GalleryItem, string> _groupOf = _ => "";
    private int _hover = -1;
    private int _focus = -1;
    private int _anchor = -1;
    private int _columns = 1;

    public GalleryView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        AutoScroll = true;
        TabStop = true;
    }

    /// <summary>Tamanho da miniatura em pixels.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Size ThumbnailSize { get; set; } = new(176, 120);

    /// <summary>Miniatura já carregada, ou null (aí a galeria dispara <see cref="ThumbnailRequested"/>).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<GalleryItem, Bitmap?>? GetThumbnail { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<GalleryItem, bool>? HasLink { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText { get; set; } = "";

    public event Action<GalleryItem>? ThumbnailRequested;
    public event EventHandler? SelectionChanged;
    public event EventHandler? ItemActivated;

    public IReadOnlyList<GalleryItem> SelectedItems => _selected.Order().Select(i => _items[i]).ToList();

    private int Gap => LogicalToDeviceUnits(14);
    private int Margin_ => LogicalToDeviceUnits(18);
    private int HeaderHeight => LogicalToDeviceUnits(40);
    private int TextHeight => Font.Height * 2 + LogicalToDeviceUnits(10);
    private Size TileSize => new(ThumbnailSize.Width + LogicalToDeviceUnits(12), ThumbnailSize.Height + TextHeight + LogicalToDeviceUnits(12));

    public void SetItems(List<GalleryItem> items, Func<GalleryItem, string> groupOf)
    {
        var keep = new HashSet<string>(SelectedItems.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);
        string? focusPath = _focus >= 0 && _focus < _items.Count ? _items[_focus].FullPath : null;

        _items = items;
        _groupOf = groupOf;
        _selected.Clear();
        _focus = _anchor = _hover = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (keep.Contains(items[i].FullPath))
                _selected.Add(i);
            if (string.Equals(items[i].FullPath, focusPath, StringComparison.OrdinalIgnoreCase))
                _focus = _anchor = i;
        }
        Relayout();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void InvalidateItem(string path)
    {
        foreach (Tile tile in _tiles)
        {
            if (string.Equals(_items[tile.Index].FullPath, path, StringComparison.OrdinalIgnoreCase))
            {
                Invalidate(Offset(tile.Bounds));
                return;
            }
        }
    }

    public void SelectAll()
    {
        for (int i = 0; i < _items.Count; i++)
            _selected.Add(i);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------- Layout ----------

    private void Relayout()
    {
        _tiles.Clear();
        _headers.Clear();
        Size tile = TileSize;
        int usable = ClientSize.Width - Margin_ * 2;
        _columns = Math.Max(1, (usable + Gap) / (tile.Width + Gap));

        int y = LogicalToDeviceUnits(6);
        int i = 0;
        while (i < _items.Count)
        {
            string group = _groupOf(_items[i]);
            int start = i;
            while (i < _items.Count && _groupOf(_items[i]) == group)
                i++;

            _headers.Add(new Header(new Rectangle(Margin_, y, usable, HeaderHeight), group, i - start));
            y += HeaderHeight;
            for (int k = start; k < i; k++)
            {
                int col = (k - start) % _columns, row = (k - start) / _columns;
                _tiles.Add(new Tile(new Rectangle(Margin_ + col * (tile.Width + Gap), y + row * (tile.Height + Gap), tile.Width, tile.Height), k));
            }
            int rows = (i - start + _columns - 1) / _columns;
            y += rows * (tile.Height + Gap) + LogicalToDeviceUnits(4);
        }

        AutoScrollMinSize = new Size(0, y + Margin_);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsHandleCreated)
            Relayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyScrollbarTheme();
        ThemeManager.Changed += ApplyScrollbarTheme;
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    private void ApplyScrollbarTheme()
    {
        if (IsHandleCreated)
            SetWindowTheme(Handle, ThemeManager.Current.IsDark ? "DarkMode_Explorer" : "Explorer", null);
    }

    private Rectangle Offset(Rectangle r) => r with { Y = r.Y + AutoScrollPosition.Y };

    private int HitTest(Point client)
    {
        var p = new Point(client.X, client.Y - AutoScrollPosition.Y);
        foreach (Tile tile in _tiles)
            if (tile.Bounds.Contains(p))
                return tile.Index;
        return -1;
    }

    // ---------- Desenho ----------

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var bg = new SolidBrush(ThemeManager.Current.Background);
        e.Graphics.FillRectangle(bg, e.ClipRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Palette p = ThemeManager.Current;
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_items.Count == 0)
        {
            using Font big = new(Font.FontFamily, Font.Size * 1.15f);
            TextRenderer.DrawText(g, EmptyText, big, ClientRectangle, p.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        using var headerFont = new Font(Font.FontFamily, Font.Size * 1.05f, FontStyle.Bold);
        foreach (Header header in _headers)
        {
            Rectangle r = Offset(header.Bounds);
            if (!r.IntersectsWith(e.ClipRectangle))
                continue;
            Size textSize = TextRenderer.MeasureText(header.Text, headerFont);
            TextRenderer.DrawText(g, header.Text, headerFont, new Point(r.X + 2, r.Y + (r.Height - textSize.Height) / 2), p.Text);
            string count = header.Count == 1 ? "1 item" : $"{header.Count} itens";
            TextRenderer.DrawText(g, count, Font, new Point(r.X + textSize.Width + 8, r.Y + (r.Height - Font.Height) / 2 + 1), p.TextMuted);
        }

        foreach (Tile tile in _tiles)
        {
            Rectangle r = Offset(tile.Bounds);
            if (r.IntersectsWith(e.ClipRectangle))
                DrawTile(g, r, tile.Index, p);
        }
    }

    private void DrawTile(Graphics g, Rectangle r, int index, Palette p)
    {
        GalleryItem item = _items[index];
        bool selected = _selected.Contains(index);
        bool hover = index == _hover;

        using (GraphicsPath card = AppIcon.RoundedRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 10))
        {
            Color fill = selected ? p.Selection : hover ? p.SurfaceHover : p.Surface;
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, card);
            using var pen = new Pen(selected ? p.Accent : p.Border, selected ? 2 : 1);
            g.DrawPath(pen, card);
        }

        int pad = LogicalToDeviceUnits(6);
        var thumbArea = new Rectangle(r.X + pad, r.Y + pad, ThumbnailSize.Width, ThumbnailSize.Height);
        Bitmap? thumb = GetThumbnail?.Invoke(item);
        if (thumb is null)
        {
            using var placeholder = new SolidBrush(p.CanvasBackground);
            using GraphicsPath path = AppIcon.RoundedRect(thumbArea, 6);
            g.FillPath(placeholder, path);
            ThumbnailRequested?.Invoke(item);
        }
        else
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(thumb, thumbArea);
        }

        if (HasLink?.Invoke(item) == true)
            DrawLinkBadge(g, thumbArea, p);

        var nameArea = new Rectangle(r.X + pad + 2, thumbArea.Bottom + LogicalToDeviceUnits(6), r.Width - pad * 2 - 4, Font.Height);
        TextRenderer.DrawText(g, item.Name, Font, nameArea, p.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        string meta = $"{item.Modified:HH:mm}  ·  {GalleryScanner.FormatSize(item.Size)}";
        TextRenderer.DrawText(g, meta, Font, nameArea with { Y = nameArea.Bottom + 2 }, p.TextMuted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private void DrawLinkBadge(Graphics g, Rectangle thumbArea, Palette p)
    {
        int d = LogicalToDeviceUnits(26);
        var badge = new Rectangle(thumbArea.Right - d - 6, thumbArea.Y + 6, d, d);
        using var fill = new SolidBrush(p.Accent);
        g.FillEllipse(fill, badge);
        using Font icon = Glyphs.CreateFont(d * 0.5f);
        TextRenderer.DrawText(g, Glyphs.Link, icon, badge, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    // ---------- Mouse ----------

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hit = HitTest(e.Location);
        if (hit == _hover)
            return;
        InvalidateIndex(_hover);
        _hover = hit;
        InvalidateIndex(_hover);
        Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateIndex(_hover);
        _hover = -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        int hit = HitTest(e.Location);
        if (e.Button == MouseButtons.Right)
        {
            // Botão direito num item fora da seleção seleciona só ele (para o menu agir nele).
            if (hit >= 0 && !_selected.Contains(hit))
                SelectSingle(hit);
            return;
        }
        if (e.Button != MouseButtons.Left)
            return;

        if (hit < 0)
        {
            if (ModifierKeys == Keys.None && _selected.Count > 0)
            {
                _selected.Clear();
                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        if (ModifierKeys.HasFlag(Keys.Shift) && _anchor >= 0)
            SelectRange(_anchor, hit, additive: ModifierKeys.HasFlag(Keys.Control));
        else if (ModifierKeys.HasFlag(Keys.Control))
        {
            if (!_selected.Remove(hit))
                _selected.Add(hit);
            _focus = _anchor = hit;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        else
            SelectSingle(hit);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && HitTest(e.Location) >= 0)
            ItemActivated?.Invoke(this, EventArgs.Empty);
    }

    private void SelectSingle(int index)
    {
        _selected.Clear();
        _selected.Add(index);
        _focus = _anchor = index;
        EnsureVisible(index);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SelectRange(int from, int to, bool additive)
    {
        if (!additive)
            _selected.Clear();
        for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++)
            _selected.Add(i);
        _focus = to;
        EnsureVisible(to);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void InvalidateIndex(int index)
    {
        if (index < 0)
            return;
        foreach (Tile tile in _tiles)
            if (tile.Index == index)
            {
                Rectangle r = Offset(tile.Bounds);
                r.Inflate(2, 2);
                Invalidate(r);
                return;
            }
    }

    private void EnsureVisible(int index)
    {
        Tile tile = _tiles.FirstOrDefault(t => t.Index == index);
        if (tile.Bounds.IsEmpty)
            return;
        int top = -AutoScrollPosition.Y;
        int y = tile.Bounds.Y;
        if (y < top)
            AutoScrollPosition = new Point(0, Math.Max(0, y - HeaderHeight));
        else if (tile.Bounds.Bottom > top + ClientSize.Height)
            AutoScrollPosition = new Point(0, tile.Bounds.Bottom - ClientSize.Height + Gap);
    }

    // ---------- Teclado ----------

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_items.Count == 0)
            return;

        int current = _focus < 0 ? 0 : _focus;
        int target = e.KeyCode switch
        {
            Keys.Left => current - 1,
            Keys.Right => current + 1,
            Keys.Up => current - _columns,
            Keys.Down => current + _columns,
            Keys.Home => 0,
            Keys.End => _items.Count - 1,
            _ => int.MinValue,
        };

        if (target != int.MinValue)
        {
            target = Math.Clamp(target, 0, _items.Count - 1);
            if (e.Shift && _anchor >= 0)
                SelectRange(_anchor, target, additive: false);
            else
                SelectSingle(target);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter && _selected.Count > 0)
        {
            ItemActivated?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ThemeManager.Changed -= ApplyScrollbarTheme;
        base.Dispose(disposing);
    }
}
