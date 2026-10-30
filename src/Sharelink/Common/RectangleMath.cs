namespace Sharelink.Common;

internal static class RectangleMath
{
    /// <summary>Retângulo entre dois pontos, independente da direção em que o mouse foi arrastado.</summary>
    public static Rectangle FromPoints(Point a, Point b) =>
        Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    /// <summary>O encoder H.264 (yuv420p) exige largura e altura pares.</summary>
    public static Rectangle MakeEven(Rectangle r) =>
        new(r.X, r.Y, r.Width - (r.Width % 2), r.Height - (r.Height % 2));
}
