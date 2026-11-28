using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SnipNation.Core;

namespace SnipNation.Windows;

internal sealed class CaptureSession(DesktopSnapshot snapshot, Dispatcher dispatcher) : IDisposable
{
    private readonly CaptureSelection _selection = new(snapshot.Bounds, snapshot.Windows.Select(window => window.Bounds).ToArray());
    private readonly List<CaptureOverlay> _overlays = [];
    private readonly TaskCompletionSource<PixelRect?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CaptureOverlay? _pressedOverlay;

    internal Task<PixelRect?> Show()
    {
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        foreach (var frame in snapshot.Frames)
        {
            var overlay = new CaptureOverlay(frame);
            _overlays.Add(overlay);
            overlay.PreviewMouseMove += PointerMoved;
            overlay.PreviewMouseLeftButtonDown += PointerPressed;
            overlay.PreviewMouseLeftButtonUp += PointerReleased;
            overlay.LostMouseCapture += PointerCaptureLost;
            overlay.PreviewKeyDown += KeyPressed;
            overlay.Closed += OverlayClosed;
            overlay.Show();
        }

        var cursor = NativeMethods.CursorPosition();
        _selection.Move(cursor);
        Repaint();
        (_overlays.Find(overlay => overlay.MonitorBounds.Contains(cursor)) ?? _overlays[0]).Activate();
        return _result.Task;
    }

    internal void Cancel() => _result.TrySetResult(null);

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        Cancel();
        ReleasePointer();
        foreach (var overlay in _overlays.ToArray())
            overlay.Close();
    }

    private void PointerMoved(object sender, MouseEventArgs e)
    {
        _selection.Move(NativeMethods.CursorPosition());
        Repaint();
    }

    private void PointerPressed(object sender, MouseButtonEventArgs e)
    {
        if (_pressedOverlay is not null || _result.Task.IsCompleted)
            return;

        var overlay = (CaptureOverlay)sender;
        if (overlay.CaptureMouse())
        {
            _pressedOverlay = overlay;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(overlay);
            var threshold = (int)Math.Ceiling(SystemParameters.MinimumHorizontalDragDistance * dpi.DpiScaleX);
            _selection.Press(NativeMethods.CursorPosition(), Math.Max(3, threshold));
            Repaint();
        }
        e.Handled = true;
    }

    private void PointerReleased(object sender, MouseButtonEventArgs e)
    {
        var result = _selection.Release(NativeMethods.CursorPosition());
        ReleasePointer();
        if (result is { } bounds)
            _result.TrySetResult(bounds);
        Repaint();
        e.Handled = true;
    }

    private void ReleasePointer()
    {
        var pressed = _pressedOverlay;
        _pressedOverlay = null;
        pressed?.ReleaseMouseCapture();
    }

    private void PointerCaptureLost(object sender, MouseEventArgs e)
    {
        if (_pressedOverlay != sender)
            return;

        _pressedOverlay = null;
        _selection.CancelPress();
        _selection.Move(NativeMethods.CursorPosition());
        Repaint();
    }

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Cancel();
                e.Handled = true;
                break;
            case Key.Enter:
                _selection.Move(NativeMethods.CursorPosition());
                if (_selection.Confirm() is { } bounds)
                    _result.TrySetResult(bounds);
                e.Handled = true;
                break;
        }
    }

    private void OverlayClosed(object? sender, EventArgs e)
    {
        _overlays.Remove((CaptureOverlay)sender!);
        Cancel();
    }

    private void DisplaySettingsChanged(object? sender, EventArgs e) => dispatcher.BeginInvoke(Cancel);

    private void Repaint()
    {
        foreach (var overlay in _overlays)
            overlay.DrawSelection(_selection.Highlight);
    }
}
