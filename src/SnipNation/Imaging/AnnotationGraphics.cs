using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SnipNation.Branding;
using SnipNation.Core;

namespace SnipNation.Imaging;

internal static class AnnotationGraphics
{
    private static readonly FontFamily Serif = new("Georgia");
    private static readonly FontFamily Monospace = new("Consolas");

    internal static DrawingGroup Create(Annotation annotation)
    {
        var group = new DrawingGroup();
        using var context = group.Open();
        var points = annotation.Points;
        var start = ToPoint(points[0]);
        var brush = new SolidColorBrush(ToColor(annotation.Argb));
        var pen = new Pen(brush, annotation.Size)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            DashCap = PenLineCap.Round,
            DashStyle = annotation.Pattern switch
            {
                StrokePattern.Dashed => DashStyles.Dash,
                StrokePattern.Dotted => DashStyles.Dot,
                _ => DashStyles.Solid
            },
            LineJoin = PenLineJoin.Round
        };

        switch (annotation.Kind)
        {
            case AnnotationKind.Arrow:
                DrawArrow(context, pen, start, ToPoint(points[1]), annotation.Arrowhead);
                break;
            case AnnotationKind.Line:
                context.DrawLine(pen, start, ToPoint(points[1]));
                break;
            case AnnotationKind.Freehand:
                context.DrawGeometry(null, pen, Path(start, points.Skip(1).Select(ToPoint).ToArray(), false));
                break;
            case AnnotationKind.Highlight:
                context.DrawRectangle(brush, null, new Rect(start, ToPoint(points[1])));
                break;
            case AnnotationKind.Text:
                DrawText(context, annotation, brush, start);
                break;
        }
        return group;
    }

    internal static bool HitTest(Annotation annotation, Point point, double tolerance)
    {
        var drawing = Create(annotation);
        var bounds = drawing.Bounds;
        if (bounds.IsEmpty)
            return false;

        bounds.Inflate(tolerance, tolerance);
        if (!bounds.Contains(point))
            return false;
        if (annotation.Kind == AnnotationKind.Text)
            return true;

        foreach (var item in drawing.Children.OfType<GeometryDrawing>())
        {
            if (item.Brush is not null && item.Geometry.FillContains(point))
                return true;
            if (item.Pen is not { } stroke)
                continue;
            var pen = stroke.Clone();
            pen.Thickness += 2 * tolerance;
            pen.DashStyle = DashStyles.Solid;
            if (item.Geometry.StrokeContains(pen, point))
                return true;
        }
        return false;
    }

    internal static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    internal static bool IsLight(Color color) => 299 * color.R + 587 * color.G + 114 * color.B >= 120_000;

    private static Point ToPoint(ImagePoint point) => new(point.X, point.Y);

    private static void DrawText(DrawingContext context, Annotation annotation, SolidColorBrush brush, Point start)
    {
        var family = annotation.Font switch
        {
            AnnotationFont.Serif => Serif,
            AnnotationFont.Monospace => Monospace,
            _ => Brand.FontFamily
        };
        var typeface = new Typeface(family, FontStyles.Normal,
            annotation.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        var text = new FormattedText(annotation.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            typeface, annotation.Size, brush, 1);
        if (!annotation.Outline)
        {
            context.DrawText(text, start);
            return;
        }

        var glyphs = text.BuildGeometry(start);
        var outline = Brand.ColorFor(IsLight(brush.Color) ? "Background" : "Foreground");
        outline.A = brush.Color.A;
        var pen = new Pen(new SolidColorBrush(outline), Math.Clamp(annotation.Size * 0.125, 2, 8))
        {
            LineJoin = PenLineJoin.Round
        };
        context.DrawGeometry(null, pen, glyphs);
        context.DrawGeometry(brush, null, glyphs);
    }

    private static void DrawArrow(DrawingContext context, Pen pen, Point start, Point end, ArrowheadStyle arrowhead)
    {
        var direction = end - start;
        var length = direction.Length;
        if (length < 0.01)
            return;

        direction /= length;
        if (arrowhead is ArrowheadStyle.Tapered or ArrowheadStyle.Block)
        {
            var shapeHeadLength = Math.Min(Math.Max(12, pen.Thickness * 4.8), length * 0.54);
            var shoulder = end - direction * shapeHeadLength;
            var halfHead = ArrowWing(direction, shapeHeadLength);
            var halfShaft = halfHead * 0.46;
            var halfTail = arrowhead == ArrowheadStyle.Tapered ? new Vector() : halfShaft;
            context.DrawGeometry(pen.Brush, null, Path(start + halfTail,
                [shoulder + halfShaft, shoulder + halfHead, end, shoulder - halfHead, shoulder - halfShaft, start - halfTail],
                true));
            return;
        }

        var doubleEnded = arrowhead == ArrowheadStyle.DoubleEnded;
        var headLength = Math.Min(Math.Max(9, pen.Thickness * 3.8), length * (doubleEnded ? 0.4 : 0.6));
        if (arrowhead == ArrowheadStyle.Curved)
        {
            DrawCurvedArrow(context, pen, start, end, direction, length, headLength);
            return;
        }
        if (arrowhead == ArrowheadStyle.Open)
        {
            var headPen = pen.Clone();
            headPen.DashStyle = DashStyles.Solid;
            var headBase = end - direction * headLength;
            var wing = ArrowWing(direction, headLength);
            context.DrawLine(pen, start, end);
            context.DrawGeometry(null, headPen, Path(headBase + wing, [end, headBase - wing], false));
            return;
        }

        var inset = Math.Max(0, headLength - pen.Thickness / 2);
        context.DrawLine(pen, doubleEnded ? start + direction * inset : start, end - direction * inset);
        DrawFilledHead(context, pen.Brush, end, direction, headLength);
        if (doubleEnded)
            DrawFilledHead(context, pen.Brush, start, -direction, headLength);
    }

    private static void DrawCurvedArrow(DrawingContext context, Pen pen, Point start, Point end, Vector direction,
        double length, double headLength)
    {
        var normal = new Vector(-direction.Y, direction.X);
        var bow = Math.Min(96, length * 0.32);
        var tangent = direction - normal * (bow / (length * 0.36));
        tangent.Normalize();
        var shaftEnd = end - tangent * Math.Max(0, headLength - pen.Thickness / 2);
        var shaft = new StreamGeometry();
        using (var curve = shaft.Open())
        {
            curve.BeginFigure(start, isFilled: false, isClosed: false);
            curve.BezierTo(start + direction * (length * 0.30) + normal * bow,
                shaftEnd - tangent * (length * 0.32), shaftEnd, isStroked: true, isSmoothJoin: true);
        }
        shaft.Freeze();
        context.DrawGeometry(null, pen, shaft);
        DrawFilledHead(context, pen.Brush, end, tangent, headLength);
    }

    private static void DrawFilledHead(DrawingContext context, Brush brush, Point tip, Vector direction, double length)
    {
        var headBase = tip - direction * length;
        var wing = ArrowWing(direction, length);
        context.DrawGeometry(brush, null, Path(tip, [headBase + wing, headBase - wing], true));
    }

    private static Vector ArrowWing(Vector direction, double length) =>
        new Vector(-direction.Y, direction.X) * (length * 0.47);

    private static StreamGeometry Path(Point start, IList<Point> points, bool closed)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: closed, isClosed: closed);
            context.PolyLineTo(points, isStroked: true, isSmoothJoin: true);
        }
        geometry.Freeze();
        return geometry;
    }
}
