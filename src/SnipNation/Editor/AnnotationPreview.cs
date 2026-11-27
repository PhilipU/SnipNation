using System.Windows;
using System.Windows.Media;
using SnipNation.Branding;
using SnipNation.Core;
using SnipNation.Imaging;

namespace SnipNation.Editor;

internal static class AnnotationPreview
{
    internal static DrawingImage Render(AnnotationKind kind, AnnotationStyle style)
    {
        const double width = 88;
        const double height = 48;
        ImagePoint[] points = kind switch
        {
            AnnotationKind.Text => [new(0, 0)],
            AnnotationKind.Highlight => [new(0, 0), new(64, 18)],
            AnnotationKind.Freehand => [new(0, 4), new(9, 9), new(18, 7), new(29, -5), new(41, -8), new(53, -4), new(64, -8)],
            _ => [new(0, 14), new(64, 0)]
        };
        var mark = AnnotationGraphics.Create(style.CreateAnnotation(kind, points, text: "Aa"));
        var bounds = mark.Bounds;
        var color = AnnotationGraphics.ToColor(style.Argb);
        var useDarkPaper = 299 * color.R + 587 * color.G + 114 * color.B >= 120_000;
        var paper = useDarkPaper ? Brand.BrushFor("PaneBrush") : new SolidColorBrush(Brand.ColorFor("LightBackground"));
        var sample = new DrawingGroup();
        using (var context = sample.Open())
        {
            context.DrawRectangle(paper, null, new Rect(0, 0, width, height));
            if (!bounds.IsEmpty)
            {
                var scale = Math.Min(1, Math.Min((width - 16) / bounds.Width, (height - 10) / bounds.Height));
                var x = (width - bounds.Width * scale) / 2 - bounds.X * scale;
                var y = (height - bounds.Height * scale) / 2 - bounds.Y * scale;
                context.PushTransform(new MatrixTransform(scale, 0, 0, scale, x, y));
                if (kind == AnnotationKind.Highlight)
                {
                    var ink = new SolidColorBrush(Brand.ColorFor(useDarkPaper ? "Muted" : "LightMuted"));
                    context.DrawLine(new Pen(ink, 1.5), new Point(5, 7), new Point(59, 7));
                    context.DrawLine(new Pen(ink, 1.5), new Point(5, 12), new Point(47, 12));
                }
                context.DrawDrawing(mark);
                context.Pop();
            }
        }
        var image = new DrawingImage(sample);
        image.Freeze();
        return image;
    }
}
