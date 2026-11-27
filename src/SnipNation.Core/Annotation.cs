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

public enum StrokePattern
{
    Solid,
    Dashed,
    Dotted
}

public enum ArrowheadStyle
{
    Filled,
    Open,
    DoubleEnded
}

public sealed record AnnotationStyle(uint Argb, double Size, StrokePattern Pattern = StrokePattern.Solid,
    ArrowheadStyle Arrowhead = ArrowheadStyle.Filled, bool Bold = false)
{
    internal bool IsValid => double.IsFinite(Size) && Size > 0 && Enum.IsDefined(Pattern) && Enum.IsDefined(Arrowhead);

    public Annotation CreateAnnotation(AnnotationKind kind, ImagePoint[] points, int? presetIndex = null, string text = "") =>
        new(kind, points, Argb, Size, text, Pattern, Arrowhead, Bold, presetIndex);
}

public sealed record Annotation(AnnotationKind Kind, ImagePoint[] Points, uint Argb, double Size, string Text = "",
    StrokePattern Pattern = StrokePattern.Solid, ArrowheadStyle Arrowhead = ArrowheadStyle.Filled, bool Bold = false,
    int? PresetIndex = null)
{
    public AnnotationStyle Style => new(Argb, Size, Pattern, Arrowhead, Bold);

    public Annotation Offset(double x, double y) => this with
    {
        Points = Points.Select(point => new ImagePoint(point.X + x, point.Y + y)).ToArray()
    };
}
