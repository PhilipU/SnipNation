using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SnipNation.Branding;
using SnipNation.Core;

namespace SnipNation.Imaging;

internal static class AnnotationGraphics
{
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
            LineJoin = PenLineJoin.Round
        };

        switch (annotation.Kind)
        {
            case AnnotationKind.Arrow:
                DrawArrow(context, pen, start, ToPoint(points[1]));
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
                var text = new FormattedText(annotation.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(Brand.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    annotation.Size, brush, 1);
                context.DrawText(text, start);
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
            if (item.Geometry.StrokeContains(pen, point))
                return true;
        }
        return false;
    }

    internal static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static Point ToPoint(ImagePoint point) => new(point.X, point.Y);

    private static void DrawArrow(DrawingContext context, Pen pen, Point start, Point end)
    {
        var direction = end - start;
        var length = direction.Length;
        if (length < 0.01)
            return;

        direction /= length;
        var headLength = Math.Min(Math.Max(9, pen.Thickness * 3.8), length * 0.6);
        var headBase = end - direction * headLength;
        var wing = new Vector(-direction.Y, direction.X) * (headLength * 0.47);
        context.DrawLine(pen, start, headBase + direction * pen.Thickness / 2);
        context.DrawGeometry(pen.Brush, null, Path(end, [headBase + wing, headBase - wing], true));
    }

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
