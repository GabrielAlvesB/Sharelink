using Sharelink.Common;
using Sharelink.Controls;
using Sharelink.Theme;
using Sharelink.Editor.Shapes;
using Sharelink.Output;
using Sharelink.Settings;

namespace Sharelink.Editor;

internal enum EditorTool
{
    Arrow,
    Rectangle,
    Ellipse,
    Freehand,
    Text,
    Highlight,
    Pixelate,
    Blur,
}

/// <summary>
/// Editor de anotações. Enter conclui (segue para salvar/copiar), Ctrl+Enter conclui e gera link,
/// Esc descarta a captura.
/// </summary>
internal sealed class EditorForm : ThemedForm
{
    private static readonly (EditorTool Tool, string Label, string Glyph, string Tip)[] ToolDefs =
    [
        (EditorTool.Arrow, "Seta", Glyphs.Arrow, "Seta (1)"),
        (EditorTool.Rectangle, "Retângulo", Glyphs.Rectangle, "Retângulo (2)"),
        (EditorTool.Ellipse, "Elipse", Glyphs.Ellipse, "Elipse (3)"),
        (EditorTool.Freehand, "Caneta", Glyphs.Edit, "Desenho livre (4)"),
        (EditorTool.Text, "Texto", Glyphs.Text, "Texto: clique onde quer escrever (5)"),
        (EditorTool.Highlight, "Destacar", Glyphs.Highlight, "Marca-texto (6)"),
        (EditorTool.Pixelate, "Pixelar", Glyphs.Pixelate, "Pixelar: o jeito mais seguro de esconder dados (7)"),
        (EditorTool.Blur, "Desfocar", Glyphs.Blur, "Desfocar área (8)"),
    ];

    private static readonly Color[] QuickColors =
    [
        Color.FromArgb(239, 68, 68), Color.FromArgb(249, 115, 22), Color.FromArgb(250, 204, 21), Color.FromArgb(34, 197, 94),
        Color.FromArgb(59, 130, 246), Color.FromArgb(168, 85, 247), Color.White, Color.Black,
    ];

    private static readonly int[] Thicknesses = [2, 3, 4, 6, 8, 12];

    private readonly Bitmap _image;
    private readonly AppSettings _settings;
    private readonly EditorCanvas _canvas;
    private readonly Panel _scrollPanel;
    private readonly Dictionary<EditorTool, ToolStripButton> _toolButtons = [];
    private readonly ToolStripButton _colorButton;
    private readonly ToolStripComboBox _thicknessBox;
    private readonly Label _zoomLabel;
    private readonly List<(ToolStripItem Item, string Glyph)> _iconItems = [];
    private readonly List<(ToolStripButton Button, Color Color)> _swatches = [];
    private readonly Stack<AnnotationShape> _redo = new();

    private EditorTool _tool = EditorTool.Arrow;
    private Color _color = QuickColors[0];

    /// <summary>Imagem final (com anotações) quando o usuário conclui.</summary>
    public Bitmap? Result { get; private set; }

    /// <summary>O usuário escolheu "Concluir e gerar link".</summary>
    public bool GenerateLink { get; private set; }

    public EditorForm(Bitmap image, AppSettings settings)
    {
        _image = image;
        _settings = settings;

        Text = $"Sharelink: editor ({image.Width} × {image.Height})";
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        MinimumSize = new Size(760, 420);
        int iconSize = (int)(20 * DeviceDpi / 96f);

        // ---------- Ferramentas ----------
        var toolStrip = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(10, 6, 10, 6),
            ImageScalingSize = new Size(iconSize, iconSize),
            AutoSize = true,
        };
        ThemeManager.SetRole(toolStrip, ThemeRole.Surface);

        foreach ((EditorTool tool, string label, string glyph, string tip) in ToolDefs)
        {
            var button = new ToolStripButton(label)
            {
                ToolTipText = tip,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageAboveText,
                Padding = new Padding(4, 2, 4, 2),
            };
            button.Click += (_, _) => SelectTool(tool);
            _toolButtons[tool] = button;
            _iconItems.Add((button, glyph));
            toolStrip.Items.Add(button);
        }

        toolStrip.Items.Add(new ToolStripSeparator());
        foreach (Color color in QuickColors)
        {
            var swatch = new ToolStripButton { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Cor", Padding = new Padding(1) };
            swatch.Click += (_, _) => SetColor(color);
            _swatches.Add((swatch, color));
            toolStrip.Items.Add(swatch);
        }
        _colorButton = new ToolStripButton("Outra…") { ToolTipText = "Escolher outra cor", DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, TextImageRelation = TextImageRelation.ImageAboveText };
        _colorButton.Click += (_, _) => PickColor();
        toolStrip.Items.Add(_colorButton);

        toolStrip.Items.Add(new ToolStripSeparator());
        _thicknessBox = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 64, ToolTipText = "Espessura / tamanho do texto" };
        foreach (int t in Thicknesses)
            _thicknessBox.Items.Add(t);
        _thicknessBox.SelectedItem = 4;
        toolStrip.Items.Add(new ToolStripLabel("Espessura"));
        toolStrip.Items.Add(_thicknessBox);

        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(IconItem("Desfazer", Glyphs.Undo, "Desfazer (Ctrl+Z)", Undo));
        toolStrip.Items.Add(IconItem("Refazer", Glyphs.Redo, "Refazer (Ctrl+Y)", Redo));
        toolStrip.Items.Add(IconItem("Copiar", Glyphs.Copy, "Copiar imagem (Ctrl+C)", CopyToClipboard));
        toolStrip.Items.Add(IconItem("Salvar como", Glyphs.Save, "Salvar em outro lugar (Ctrl+S)", SaveAs));

        // ---------- Rodapé ----------
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(16, 12, 16, 12) };
        ThemeManager.SetRole(footer, ThemeRole.Surface);
        var done = new ModernButton { Text = "Concluir", Glyph = Glyphs.Check, Variant = ButtonVariant.Primary, Dock = DockStyle.Right, Padding = new Padding(18, 0, 18, 0), AutoWidth = true };
        done.Click += (_, _) => Complete(generateLink: false);
        var doneLink = new ModernButton { Text = "Concluir e gerar link", Glyph = Glyphs.Link, Variant = ButtonVariant.Secondary, Dock = DockStyle.Right, Padding = new Padding(18, 0, 18, 0), AutoWidth = true };
        doneLink.Click += (_, _) => Complete(generateLink: true);
        var cancel = new ModernButton { Text = "Descartar", Glyph = Glyphs.Delete, Variant = ButtonVariant.Ghost, Dock = DockStyle.Right, Padding = new Padding(18, 0, 18, 0), AutoWidth = true };
        cancel.Click += (_, _) => Discard();
        _zoomLabel = new Label { Dock = DockStyle.Left, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        ThemeManager.SetRole(_zoomLabel, ThemeRole.Muted);
        var tips = new ToolTip();
        tips.SetToolTip(done, "Salvar e copiar (Enter)");
        tips.SetToolTip(doneLink, "Salvar e gerar link público (Ctrl+Enter)");
        tips.SetToolTip(cancel, "Descartar esta captura (Esc)");
        footer.Controls.AddRange([cancel, new Panel { Dock = DockStyle.Right, Width = 8 }, doneLink, new Panel { Dock = DockStyle.Right, Width = 8 }, done, _zoomLabel]);
        Disposed += (_, _) => tips.Dispose();

        // ---------- Área de desenho ----------
        _canvas = new EditorCanvas(image);
        _canvas.MouseDown += Canvas_MouseDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseUp += Canvas_MouseUp;

        _scrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        ThemeManager.SetRole(_scrollPanel, ThemeRole.Canvas);
        _scrollPanel.Controls.Add(_canvas);
        _scrollPanel.Resize += (_, _) => CenterCanvas();
        _scrollPanel.MouseWheel += Panel_MouseWheel;
        _canvas.MouseWheel += Panel_MouseWheel;

        Controls.Add(_scrollPanel);
        Controls.Add(toolStrip);
        Controls.Add(footer);

        RenderIcons();
        SelectTool(EditorTool.Arrow);
        UpdateColorButton();
        FitToScreen();
    }

    private ToolStripButton IconItem(string text, string glyph, string tip, Action action)
    {
        var item = new ToolStripButton(text)
        {
            ToolTipText = tip,
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageAboveText,
            Padding = new Padding(4, 2, 4, 2),
        };
        item.Click += (_, _) => action();
        _iconItems.Add((item, glyph));
        return item;
    }

    /// <summary>Desenha os ícones da barra na cor do tema atual.</summary>
    private void RenderIcons()
    {
        int size = (int)(20 * DeviceDpi / 96f);
        Color color = ThemeManager.Current.Text;
        foreach ((ToolStripItem item, string glyph) in _iconItems)
        {
            Image? old = item.Image;
            item.Image = Glyphs.Render(glyph, size, color);
            old?.Dispose();
        }
        UpdateColorButton();
    }

    protected override void OnThemeApplied() => RenderIcons();

    private float Thickness => _thicknessBox.SelectedItem is int t ? t : 4;

    private void FitToScreen()
    {
        Rectangle work = Screen.FromPoint(MousePosition).WorkingArea;
        Location = work.Location;
        // Espaço da barra de ferramentas + rodapé.
        int chrome = (int)(150 * DeviceDpi / 96f);
        var maxCanvas = new Size((int)(work.Width * 0.9) - 40, (int)(work.Height * 0.9) - chrome);
        float zoom = Math.Min(1f, Math.Min((float)maxCanvas.Width / _image.Width, (float)maxCanvas.Height / _image.Height));
        _canvas.Zoom = zoom;
        UpdateZoomLabel();

        ClientSize = new Size(
            Math.Max(MinimumSize.Width, Math.Min(maxCanvas.Width + 40, _canvas.Width + 48)),
            Math.Max(MinimumSize.Height, Math.Min(maxCanvas.Height + chrome, _canvas.Height + chrome + 24)));
    }

    private void CenterCanvas()
    {
        Size area = _scrollPanel.ClientSize;
        int x = Math.Max(0, (area.Width - _canvas.Width) / 2);
        int y = Math.Max(0, (area.Height - _canvas.Height) / 2);
        _canvas.Location = new Point(x + _scrollPanel.AutoScrollPosition.X, y + _scrollPanel.AutoScrollPosition.Y);
    }

    private void UpdateZoomLabel() => _zoomLabel.Text = $"Zoom {_canvas.Zoom:P0}  ·  Ctrl + roda do mouse";

    private void SelectTool(EditorTool tool)
    {
        _tool = tool;
        foreach ((EditorTool t, ToolStripButton b) in _toolButtons)
            b.Checked = t == tool;
        _canvas.Cursor = tool == EditorTool.Text ? Cursors.IBeam : Cursors.Cross;
    }

    private void PickColor()
    {
        using var dialog = new ColorDialog { Color = _color, FullOpen = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            SetColor(dialog.Color);
    }

    private void SetColor(Color color)
    {
        _color = color;
        UpdateColorButton();
    }

    private void UpdateColorButton()
    {
        int size = (int)(20 * DeviceDpi / 96f);
        Palette p = ThemeManager.Current;
        foreach ((ToolStripButton swatch, Color color) in _swatches)
        {
            Image? old = swatch.Image;
            swatch.Image = Swatch(color, size, p.Border);
            swatch.Checked = color.ToArgb() == _color.ToArgb();
            old?.Dispose();
        }
        Image? previous = _colorButton.Image;
        _colorButton.Image = Swatch(_color, size, p.Border);
        _colorButton.Checked = !_swatches.Any(s => s.Color.ToArgb() == _color.ToArgb());
        previous?.Dispose();
    }

    private static Bitmap Swatch(Color color, int size, Color border)
    {
        var bmp = new Bitmap(size, size);
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 2, 2, size - 5, size - 5);
        using var pen = new Pen(border);
        g.DrawEllipse(pen, 2, 2, size - 5, size - 5);
        return bmp;
    }

    private AnnotationShape? CreateShape(Point p) => _tool switch
    {
        EditorTool.Arrow => new ArrowShape(p) { Color = _color, Thickness = Thickness },
        EditorTool.Rectangle => new RectangleShape(p) { Color = _color, Thickness = Thickness },
        EditorTool.Ellipse => new EllipseShape(p) { Color = _color, Thickness = Thickness },
        EditorTool.Freehand => new FreehandShape(p) { Color = _color, Thickness = Thickness },
        EditorTool.Highlight => new HighlightShape(p) { Color = _color, Thickness = Thickness },
        EditorTool.Pixelate => new PixelateShape(p) { Thickness = Thickness },
        EditorTool.Blur => new BlurShape(p) { Thickness = Thickness },
        _ => null,
    };

    private void Canvas_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        Point p = _canvas.ToImagePoint(e.Location);
        if (_tool == EditorTool.Text)
        {
            using var dialog = new TextInputDialog();
            if (dialog.ShowDialog(this) == DialogResult.OK)
                AddShape(new TextShape(p, dialog.Value) { Color = _color, Thickness = Thickness });
            return;
        }

        _canvas.CurrentShape = CreateShape(p);
        _canvas.Capture = true;
    }

    private void Canvas_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_canvas.CurrentShape is null)
            return;
        _canvas.CurrentShape.Drag(_canvas.ToImagePoint(e.Location));
        _canvas.Invalidate();
    }

    private void Canvas_MouseUp(object? sender, MouseEventArgs e)
    {
        AnnotationShape? shape = _canvas.CurrentShape;
        if (shape is null || e.Button != MouseButtons.Left)
            return;

        _canvas.CurrentShape = null;
        _canvas.Capture = false;
        if (shape.IsValid)
            AddShape(shape);
        else
            _canvas.Invalidate();
    }

    private void Panel_MouseWheel(object? sender, MouseEventArgs e)
    {
        if (ModifierKeys != Keys.Control)
            return;
        if (e is HandledMouseEventArgs handled)
            handled.Handled = true;
        _canvas.Zoom *= e.Delta > 0 ? 1.25f : 0.8f;
        UpdateZoomLabel();
        CenterCanvas();
    }

    private void AddShape(AnnotationShape shape)
    {
        if (!shape.IsValid)
            return;
        _canvas.Shapes.Add(shape);
        _redo.Clear();
        _canvas.Invalidate();
    }

    private void Undo()
    {
        if (_canvas.Shapes.Count == 0)
            return;
        _redo.Push(_canvas.Shapes[^1]);
        _canvas.Shapes.RemoveAt(_canvas.Shapes.Count - 1);
        _canvas.Invalidate();
    }

    private void Redo()
    {
        if (_redo.Count == 0)
            return;
        _canvas.Shapes.Add(_redo.Pop());
        _canvas.Invalidate();
    }

    private void CopyToClipboard()
    {
        using Bitmap flat = _canvas.Flatten();
        ClipboardHelper.SetImage(flat);
    }

    private void SaveAs()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg",
            FilterIndex = _settings.ImageFormat == ImageFormatOption.Jpeg ? 2 : 1,
            InitialDirectory = Directory.Exists(_settings.OutputFolder) ? _settings.OutputFolder : null,
            FileName = FileNamer.Sanitize(FileNamer.Expand(_settings.FileNamePattern, DateTime.Now)),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        using Bitmap flat = _canvas.Flatten();
        ImageSaver.Save(flat, dialog.FileName, ImageSaver.FormatFromPath(dialog.FileName), _settings.JpegQuality);
    }

    private void Complete(bool generateLink)
    {
        GenerateLink = generateLink;
        Result = _canvas.Flatten();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Discard()
    {
        if (_canvas.Shapes.Count > 0 &&
            MessageBox.Show(this, "Descartar esta captura e as anotações?", "Sharelink",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.Z: Undo(); return true;
            case Keys.Control | Keys.Y: Redo(); return true;
            case Keys.Control | Keys.C: CopyToClipboard(); return true;
            case Keys.Control | Keys.S: SaveAs(); return true;
            case Keys.Enter: Complete(generateLink: false); return true;
            case Keys.Control | Keys.Enter: Complete(generateLink: true); return true;
            case Keys.Escape: Discard(); return true;
            case >= Keys.D1 and <= Keys.D8:
                SelectTool(ToolDefs[keyData - Keys.D1].Tool);
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        CenterToScreen();
        CenterCanvas();
        Activate();
    }
}
