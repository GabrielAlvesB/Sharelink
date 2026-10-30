using System.ComponentModel;
using System.Drawing.Drawing2D;
using Sharelink.Editor.Shapes;

namespace Sharelink.Editor;

/// <summary>Área de desenho: mostra a imagem com zoom e as anotações por cima.</summary>
internal sealed class EditorCanvas : Control
{
    private readonly Bitmap _image;
    private float _zoom = 1f;

    public EditorCanvas(Bitmap image)
    {
        _image = image;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Cross;
        UpdateSize();
    }

    public List<AnnotationShape> Shapes { get; } = [];
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AnnotationShape? CurrentShape { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, 0.1f, 8f);
            UpdateSize();
            Invalidate();
        }
    }

    private void UpdateSize() => Size = new Size((int)Math.Ceiling(_image.Width * _zoom), (int)Math.Ceiling(_image.Height * _zoom));

    public Point ToImagePoint(Point p) => new((int)(p.X / _zoom), (int)(p.Y / _zoom));

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.InterpolationMode = _zoom < 1f ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.ScaleTransform(_zoom, _zoom);
        g.DrawImage(_image, 0, 0, _image.Width, _image.Height);
        RenderShapes(g, _image, Shapes, CurrentShape);
    }

    public static void RenderShapes(Graphics g, Bitmap source, IEnumerable<AnnotationShape> shapes, AnnotationShape? current)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Default;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        foreach (AnnotationShape shape in shapes)
            shape.Draw(g, source);
        current?.Draw(g, source);
    }

    /// <summary>Imagem final com as anotações "carimbadas".</summary>
    public Bitmap Flatten()
    {
        var result = new Bitmap(_image.Width, _image.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(result);
        g.DrawImageUnscaled(_image, 0, 0);
        RenderShapes(g, _image, Shapes, null);
        return result;
    }
}
