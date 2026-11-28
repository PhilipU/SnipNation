using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SnipNation.Branding;
using SnipNation.Core;

namespace SnipNation.Imaging;

internal static partial class AnnotationGraphics
{
    private static void DrawFrame(DrawingContext context, Pen pen, Point start, Point end, FrameShape shape)
    {
        var bounds = new Rect(start, end);
        var shorterSide = Math.Min(bounds.Width, bounds.Height);
        if (shorterSide <= 0)
            return;

        var radius = Math.Min(24, shorterSide * 0.25);
        Geometry frame = shape switch
        {
            FrameShape.RoundedRectangle => new RectangleGeometry(bounds, radius, radius),
            FrameShape.Circle => new EllipseGeometry(
                new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), shorterSide / 2, shorterSide / 2),
            FrameShape.Cloud => CloudOutline(bounds),
            FrameShape.Ellipse => new EllipseGeometry(bounds),
            _ => new RectangleGeometry(bounds)
        };
        context.DrawGeometry(null, pen, frame);
    }

    private static Geometry CloudOutline(Rect bounds)
    {
        const int lobes = 12;
        const double start = -Math.PI / 2;
        const double step = 2 * Math.PI / lobes;
        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        var outline = new StreamGeometry();
        using (var curve = outline.Open())
        {
            curve.BeginFigure(AtAngle(start, 0.80), isFilled: false, isClosed: true);
            for (var index = 0; index < lobes; index++)
            {
                var angle = start + index * step;
                curve.QuadraticBezierTo(AtAngle(angle + step / 2, 1.20), AtAngle(angle + step, 0.80),
                    isStroked: true, isSmoothJoin: false);
            }
        }
        outline.Freeze();
        return outline;

        Point AtAngle(double angle, double scale) => new(
            center.X + Math.Cos(angle) * bounds.Width / 2 * scale,
            center.Y + Math.Sin(angle) * bounds.Height / 2 * scale);
    }

    private static void DrawNumber(DrawingContext context, Annotation annotation, SolidColorBrush brush, Point center)
    {
        var ink = Brand.BrushFor(IsLight(brush.Color) ? "PaneBrush" : "InkBrush");
        var typeface = new Typeface(Brand.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var label = new FormattedText(annotation.Number.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, typeface, annotation.Size * 0.57, ink, 1);
        var padding = annotation.Marker == NumberShape.Diamond ? 0.73 : 0.42;
        var diameter = Math.Max(annotation.Size, label.Width + annotation.Size * padding);
        var width = annotation.Marker == NumberShape.Pill ? Math.Max(diameter, annotation.Size * 1.6) : diameter;
        var height = annotation.Marker == NumberShape.Pill ? annotation.Size : diameter;
        var bounds = new Rect(center.X - width / 2, center.Y - height / 2, width, height);
        var marker = NumberMarker(annotation.Marker, bounds, center);

        if (annotation.Points.Length == 2)
        {
            var target = ToPoint(annotation.Points[1]);
            if (!marker.FillContains(target))
                DrawNumberConnector(context, annotation.Connector, brush, annotation.Size, center, target);
        }

        context.DrawGeometry(brush, null, marker);
        context.DrawText(label, new Point(center.X - label.Width / 2, center.Y - label.Height / 2));
    }

    private static Geometry NumberMarker(NumberShape shape, Rect bounds, Point center) => shape switch
    {
        NumberShape.Square => new RectangleGeometry(bounds),
        NumberShape.RoundedSquare => new RectangleGeometry(bounds, bounds.Height * 0.23, bounds.Height * 0.23),
        NumberShape.Pill => new RectangleGeometry(bounds, bounds.Height / 2, bounds.Height / 2),
        NumberShape.Diamond => Path(new Point(center.X, bounds.Top),
            [new(bounds.Right, center.Y), new(center.X, bounds.Bottom), new(bounds.Left, center.Y)], true),
        NumberShape.Hexagon => Path(new Point(bounds.Left, center.Y),
            [new(bounds.Left + bounds.Width * 0.25, bounds.Top), new(bounds.Right - bounds.Width * 0.25, bounds.Top),
             new(bounds.Right, center.Y), new(bounds.Right - bounds.Width * 0.25, bounds.Bottom),
             new(bounds.Left + bounds.Width * 0.25, bounds.Bottom)], true),
        _ => new EllipseGeometry(bounds)
    };

    private static void DrawNumberConnector(DrawingContext context, ConnectorStyle style, Brush brush, double size,
        Point start, Point end)
    {
        var direction = end - start;
        var length = direction.Length;
        if (length < 0.01)
            return;

        var pen = new Pen(brush, Math.Clamp(size * 0.075, 1.5, 5))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            DashCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
            DashStyle = style switch
            {
                ConnectorStyle.Dashed => DashStyles.Dash,
                ConnectorStyle.Dotted => DashStyles.Dot,
                _ => DashStyles.Solid
            }
        };
        var radius = Math.Min(pen.Thickness * 1.6, length * 0.25);
        var ring = style is ConnectorStyle.Curved or ConnectorStyle.Dotted;
        var lineEnd = ring ? end - direction / length * radius : end;
        switch (style)
        {
            case ConnectorStyle.Arrow:
                DrawArrow(context, pen, start, end, ArrowheadStyle.Filled);
                return;
            case ConnectorStyle.Elbow:
                context.DrawGeometry(null, pen, Path(start, [new(end.X, start.Y), end], false));
                break;
            case ConnectorStyle.Curved:
                var bend = start + direction / 2 + new Vector(-direction.Y, direction.X) * Math.Min(0.48, 90 / length);
                var geometry = new StreamGeometry();
                using (var curve = geometry.Open())
                {
                    curve.BeginFigure(start, isFilled: false, isClosed: false);
                    curve.QuadraticBezierTo(bend, lineEnd, isStroked: true, isSmoothJoin: true);
                }
                geometry.Freeze();
                context.DrawGeometry(null, pen, geometry);
                break;
            default:
                context.DrawLine(pen, start, lineEnd);
                break;
        }
        context.DrawEllipse(ring ? null : brush, ring ? new Pen(brush, pen.Thickness * 0.8) : null, end, radius, radius);
    }
}
