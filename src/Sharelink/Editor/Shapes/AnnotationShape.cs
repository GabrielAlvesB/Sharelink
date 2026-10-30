using System.Drawing.Drawing2D;
using Sharelink.Common;

namespace Sharelink.Editor.Shapes;

/// <summary>Uma anotação desenhada sobre a imagem. Coordenadas sempre em pixels da imagem.</summary>
internal abstract class AnnotationShape
{
    public Color Color { get; init; } = Color.Red;
    public float Thickness { get; init; } = 4;

    /// <summary>Atualiza a forma enquanto o mouse é arrastado.</summary>
    public abstract void Drag(Point current);

    /// <summary>Formas pequenas demais (clique sem arrastar) são descartadas.</summary>
    public abstract bool IsValid { get; }

    /// <param name="source">Imagem original, usada por pixelar/desfocar.</param>
    public abstract void Draw(Graphics g, Bitmap source);
}

/// <summary>Forma definida por dois cantos.</summary>
internal abstract class BoxShape(Point start) : AnnotationShape
{
    public Point Start { get; } = start;
    public Point End { get; private set; } = start;
    public Rectangle Bounds => RectangleMath.FromPoints(Start, End);

    public override void Drag(Point current) => End = current;
    public override bool IsValid => Bounds.Width > 2 && Bounds.Height > 2;
}

internal sealed class RectangleShape(Point start) : BoxShape(start)
{
    public override void Draw(Graphics g, Bitmap source)
    {
        using var pen = new Pen(Color, Thickness) { LineJoin = LineJoin.Round };
        g.DrawRectangle(pen, Bounds);
    }
}

internal sealed class EllipseShape(Point start) : BoxShape(start)
{
    public override void Draw(Graphics g, Bitmap source)
    {
        using var pen = new Pen(Color, Thickness);
        g.DrawEllipse(pen, Bounds);
    }
}

/// <summary>Marca-texto: retângulo cheio semitransparente.</summary>
internal sealed class HighlightShape(Point start) : BoxShape(start)
{
    public override void Draw(Graphics g, Bitmap source)
    {
        using var brush = new SolidBrush(Color.FromArgb(90, Color));
        g.FillRectangle(brush, Bounds);
    }
}

internal sealed class ArrowShape(Point start) : AnnotationShape
{
    public Point Start { get; } = start;
    public Point End { get; private set; } = start;

    public override void Drag(Point current) => End = current;

    public override bool IsValid => Math.Abs(End.X - Start.X) + Math.Abs(End.Y - Start.Y) > 8;

    public override void Draw(Graphics g, Bitmap source)
    {
        using var cap = new AdjustableArrowCap(3.2f, 3.2f, isFilled: true);
        using var pen = new Pen(Color, Thickness) { StartCap = LineCap.Round, CustomEndCap = cap };
        g.DrawLine(pen, Start, End);
    }
}

internal sealed class FreehandShape : AnnotationShape
{
    private readonly List<Point> _points;

    public FreehandShape(Point start) => _points = [start];

    public override void Drag(Point current)
    {
        if (_points[^1] != current)
            _points.Add(current);
    }

    public override bool IsValid => _points.Count > 1;

    public override void Draw(Graphics g, Bitmap source)
    {
        if (_points.Count < 2)
            return;
        using var pen = new Pen(Color, Thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, _points.ToArray());
    }
}

/// <summary>Texto com contorno branco para ficar legível sobre qualquer fundo.</summary>
internal sealed class TextShape(Point location, string text) : AnnotationShape
{
    public Point Location { get; } = location;
    public string Text { get; } = text;
    public float FontSize => 14 + Thickness * 3;

    public override void Drag(Point current)
    {
    }

    public override bool IsValid => !string.IsNullOrWhiteSpace(Text);

    public override void Draw(Graphics g, Bitmap source)
    {
        using var path = new GraphicsPath();
        using var family = new FontFamily("Segoe UI");
        // Tamanho em pixels da imagem, não depende do DPI do monitor.
        path.AddString(Text, family, (int)FontStyle.Bold, FontSize * 96f / 72f, Location, StringFormat.GenericTypographic);
        using var outline = new Pen(Color.White, Math.Max(3, FontSize / 5)) { LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(Color);
        g.DrawPath(outline, path);
        g.FillPath(fill, path);
    }
}
