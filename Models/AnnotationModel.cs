using System.Windows;
using System.Windows.Media;

namespace Mpdf.Models;

public enum AnnotationKind
{
    Highlight,
    Underline,
    Strikeout,
    Rectangle,
    Ink
}

public sealed class AnnotationModel
{
    public Guid Id { get; } = Guid.NewGuid();
    public AnnotationKind Kind { get; init; }
    public Color Color { get; init; } = Colors.Yellow;
    public double StrokeWidth { get; init; } = 1.8;
    public List<Point> Points { get; } = new();

    public Rect Bounds
    {
        get
        {
            if (Points.Count == 0) return Rect.Empty;
            var minX = Points.Min(p => p.X);
            var minY = Points.Min(p => p.Y);
            var maxX = Points.Max(p => p.X);
            var maxY = Points.Max(p => p.Y);
            return new Rect(new Point(minX, minY), new Point(maxX, maxY));
        }
    }

    public bool IsArea => Kind != AnnotationKind.Ink;

    public bool HitTest(Point normalized, double tolerance)
    {
        var bounds = Bounds;
        if (bounds.IsEmpty) return false;
        return normalized.X >= bounds.Left - tolerance
            && normalized.X <= bounds.Right + tolerance
            && normalized.Y >= bounds.Top - tolerance
            && normalized.Y <= bounds.Bottom + tolerance;
    }
}
