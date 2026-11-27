namespace SnipNation.Core;

public enum AnnotationKind
{
    Arrow,
    Line,
    Freehand,
    Highlight,
    Text
}

public readonly record struct ImagePoint(double X, double Y);

public sealed record Annotation(AnnotationKind Kind, ImagePoint[] Points, uint Argb, double Size, string Text = "")
{
    public Annotation Offset(double x, double y) => this with
    {
        Points = Points.Select(point => new ImagePoint(point.X + x, point.Y + y)).ToArray()
    };
}
