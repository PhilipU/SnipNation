using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SnipNation.Branding;
using SnipNation.Core;

namespace SnipNation.Windows;

internal sealed class CaptureOverlay : Window
{
    private readonly OverlayDrawing _drawing;

    internal CaptureOverlay(MonitorFrame frame)
    {
        MonitorBounds = frame.Bounds;
        _drawing = new OverlayDrawing(frame);
        Content = _drawing;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brand.BrushFor("PaneBrush");
        Cursor = Cursors.Cross;
    }

    internal PixelRect MonitorBounds { get; }

    internal void DrawSelection(PixelRect? selection) => _drawing.DrawSelection(selection);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var bounds = MonitorBounds;
        if (!NativeMethods.SetWindowPos(hwnd, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                NativeMethods.NoActivateOrZOrder))
            throw new Win32Exception();
    }

    private sealed class OverlayDrawing : FrameworkElement
    {
        private static readonly Brush Shade = new SolidColorBrush(Brand.ColorFor("Background")) { Opacity = 0.32 };
        private static readonly Pen Outline = new(Brand.BrushFor("AccentBrush"), 2);
        private static readonly Pen OutlineBacking = new(Brand.BrushFor("PaneBrush"), 4);
        private readonly MonitorFrame _frame;
        private PixelRect? _selection;

        internal OverlayDrawing(MonitorFrame frame)
        {
            _frame = frame;
            ClipToBounds = true;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        }

        internal void DrawSelection(PixelRect? selection)
        {
            if (_selection == selection)
                return;
            _selection = selection;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext context)
        {
            var viewport = new Rect(RenderSize);
            context.DrawImage(_frame.Image, viewport);
            if (_selection is not { } selection || selection.Intersect(_frame.Bounds).IsEmpty)
            {
                context.DrawRectangle(Shade, null, viewport);
                return;
            }

            var local = selection.RelativeTo(_frame.Bounds);
            var scaleX = viewport.Width / _frame.Bounds.Width;
            var scaleY = viewport.Height / _frame.Bounds.Height;
            var highlight = new Rect(local.X * scaleX, local.Y * scaleY, local.Width * scaleX, local.Height * scaleY);
            var shade = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(viewport),
                new RectangleGeometry(highlight));
            context.DrawGeometry(Shade, null, shade);
            context.DrawRectangle(null, OutlineBacking, highlight);
            context.DrawRectangle(null, Outline, highlight);
        }
    }
}
