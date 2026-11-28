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
    DoubleEnded,
    Tapered,
    Curved,
    Block
}

public enum AnnotationFont
{
    SansSerif,
    Serif,
    Monospace
}

public sealed record AnnotationStyle(uint Argb, double Size, StrokePattern Pattern = StrokePattern.Solid,
    ArrowheadStyle Arrowhead = ArrowheadStyle.Filled, bool Bold = false,
    AnnotationFont Font = AnnotationFont.SansSerif, bool Outline = false)
{
    internal bool IsValid => double.IsFinite(Size) && Size > 0 && Enum.IsDefined(Pattern) &&
        Enum.IsDefined(Arrowhead) && Enum.IsDefined(Font);

    public Annotation CreateAnnotation(AnnotationKind kind, ImagePoint[] points, int? presetIndex = null, string text = "") =>
        new(kind, points, Argb, Size, text, Pattern, Arrowhead, Bold, presetIndex, Font, Outline);
}

public sealed record Annotation(AnnotationKind Kind, ImagePoint[] Points, uint Argb, double Size, string Text = "",
    StrokePattern Pattern = StrokePattern.Solid, ArrowheadStyle Arrowhead = ArrowheadStyle.Filled, bool Bold = false,
    int? PresetIndex = null, AnnotationFont Font = AnnotationFont.SansSerif, bool Outline = false)
{
    public AnnotationStyle Style => new(Argb, Size, Pattern, Arrowhead, Bold, Font, Outline);

    public Annotation Offset(double x, double y) => this with
    {
        Points = Points.Select(point => new ImagePoint(point.X + x, point.Y + y)).ToArray()
    };
}
