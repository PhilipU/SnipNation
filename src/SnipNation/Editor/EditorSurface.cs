using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnipNation.Branding;
using SnipNation.Core;
using SnipNation.Imaging;

namespace SnipNation.Editor;

internal sealed class EditorSurface : FrameworkElement
{
    private BitmapSource _image;
    private IReadOnlyList<Annotation> _annotations = [];
    private Annotation? _preview;
    private int _selection = -1;
    private bool _moving;
    private double _zoom = 1;
    private PixelRect? _cropSelection;

    internal EditorSurface(BitmapSource image)
    {
        _image = image;
        Width = image.PixelWidth;
        Height = image.PixelHeight;
        ClipToBounds = true;
        Focusable = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    internal void SetImage(BitmapSource image)
    {
        _image = image;
        Width = image.PixelWidth;
        Height = image.PixelHeight;
        InvalidateVisual();
    }

    internal void SetView(IReadOnlyList<Annotation> annotations, int selection, Annotation? preview, bool moving, double zoom,
        PixelRect? cropSelection)
    {
        _annotations = annotations;
        _selection = selection;
        _preview = preview;
        _moving = moving;
        _zoom = zoom;
        _cropSelection = cropSelection;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        Images.Draw(context, _image, new Size(Width, Height), VisibleAnnotations());
        if (_cropSelection is { } crop)
        {
            var cropBounds = new Rect(crop.X, crop.Y, crop.Width, crop.Height);
            var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(Width, Height)),
                new RectangleGeometry(cropBounds));
            context.PushOpacity(0.62);
            context.DrawGeometry(Brand.BrushFor("PaneBrush"), null, outside);
            context.Pop();
            context.DrawRectangle(null, new Pen(Brand.BrushFor("AccentBrush"), 2 / _zoom), cropBounds);
        }
        if (_selection < 0)
            return;

        var selected = _moving && _preview is not null ? _preview : _annotations[_selection];
        var bounds = AnnotationGraphics.Create(selected).Bounds;
        if (bounds.IsEmpty)
            return;

        bounds.Inflate(3 / _zoom, 3 / _zoom);
        var pen = new Pen(Brand.BrushFor("AccentBrush"), 1.5 / _zoom) { DashStyle = DashStyles.Dash };
        context.DrawRectangle(null, new Pen(Brand.BrushFor("PaneBrush"), 2.5 / _zoom), bounds);
        context.DrawRectangle(null, pen, bounds);
    }

    private IEnumerable<Annotation> VisibleAnnotations()
    {
        for (var index = 0; index < _annotations.Count; index++)
            yield return _moving && index == _selection && _preview is not null ? _preview : _annotations[index];
        if (!_moving && _preview is not null)
            yield return _preview;
    }
}
