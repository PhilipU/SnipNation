namespace SnipNation.Core;

public enum AnnotationKind
{
    Arrow,
    Line,
    Freehand,
    Highlight,
    Text,
    Frame,
    Number
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

public enum FrameShape
{
    Rectangle,
    RoundedRectangle,
    Circle,
    Cloud,
    Ellipse
}

public enum NumberShape
{
    Circle,
    RoundedSquare,
    Diamond,
    Square,
    Hexagon,
    Pill
}

public enum ConnectorStyle
{
    Straight,
    Elbow,
    Arrow,
    Dashed,
    Curved,
    Dotted
}

public sealed record AnnotationStyle(uint Argb, double Size, StrokePattern Pattern = StrokePattern.Solid,
    ArrowheadStyle Arrowhead = ArrowheadStyle.Filled, bool Bold = false,
    AnnotationFont Font = AnnotationFont.SansSerif, bool Outline = false, FrameShape Frame = FrameShape.Rectangle,
    NumberShape Marker = NumberShape.Circle, ConnectorStyle Connector = ConnectorStyle.Straight)
{
    internal bool IsValid => double.IsFinite(Size) && Size > 0 && Enum.IsDefined(Pattern) &&
        Enum.IsDefined(Arrowhead) && Enum.IsDefined(Font) && Enum.IsDefined(Frame) &&
        Enum.IsDefined(Marker) && Enum.IsDefined(Connector);

    public Annotation CreateAnnotation(AnnotationKind kind, ImagePoint[] points, int? presetIndex = null, string text = "",
        int number = 0) =>
        new(kind, points, Argb, Size, text, Pattern, Arrowhead, Bold, presetIndex, Font, Outline, Frame, Marker, Connector, number);
}

public sealed record Annotation(AnnotationKind Kind, ImagePoint[] Points, uint Argb, double Size, string Text = "",
    StrokePattern Pattern = StrokePattern.Solid, ArrowheadStyle Arrowhead = ArrowheadStyle.Filled, bool Bold = false,
    int? PresetIndex = null, AnnotationFont Font = AnnotationFont.SansSerif, bool Outline = false,
    FrameShape Frame = FrameShape.Rectangle, NumberShape Marker = NumberShape.Circle,
    ConnectorStyle Connector = ConnectorStyle.Straight, int Number = 0)
{
    public AnnotationStyle Style => new(Argb, Size, Pattern, Arrowhead, Bold, Font, Outline, Frame, Marker, Connector);

    public Annotation Offset(double x, double y) => this with
    {
        Points = Points.Select(point => new ImagePoint(point.X + x, point.Y + y)).ToArray()
    };
}
