namespace SnipNation.Core;

public sealed class CaptureSelection(PixelRect desktop, IReadOnlyList<PixelRect> windowsInZOrder)
{
    private PixelPoint? _anchor;
    private int _dragThreshold;
    private bool _dragging;
    private PixelRect? _window;
    private PixelRect? _region;

    public PixelRect? Highlight => _region ?? (_dragging ? null : _window);

    public void Move(PixelPoint point)
    {
        if (_anchor is { } anchor && (_dragging || ReachedDragThreshold(anchor, point)))
        {
            _dragging = true;
            var bounds = PixelRect.FromCorners(anchor, point).Intersect(desktop);
            _region = bounds.IsEmpty ? null : bounds;
            return;
        }

        _window = null;
        if (_region is not null)
            return;

        foreach (var window in windowsInZOrder)
        {
            if (window.Contains(point))
            {
                _window = window.Intersect(desktop);
                break;
            }
        }
    }

    public void Press(PixelPoint point, int dragThreshold)
    {
        _anchor = point;
        _dragThreshold = dragThreshold;
        Move(point);
    }

    public PixelRect? Release(PixelPoint point)
    {
        if (_anchor is null)
            return null;

        Move(point);
        _anchor = null;
        if (!_dragging)
            return Confirm();

        _dragging = false;
        Move(point);
        return null;
    }

    public void CancelPress()
    {
        if (_dragging)
            _region = null;
        _anchor = null;
        _dragging = false;
    }

    public PixelRect? Confirm() => _anchor is null && Highlight is { IsEmpty: false } bounds ? bounds : null;

    private bool ReachedDragThreshold(PixelPoint anchor, PixelPoint point) =>
        Math.Abs(point.X - anchor.X) >= _dragThreshold || Math.Abs(point.Y - anchor.Y) >= _dragThreshold;
}
