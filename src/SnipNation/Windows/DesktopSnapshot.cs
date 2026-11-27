using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnipNation.Core;
using Screen = System.Windows.Forms.Screen;
using static SnipNation.Windows.NativeMethods;

namespace SnipNation.Windows;

internal sealed record MonitorFrame(PixelRect Bounds, BitmapSource Image);

internal sealed class DesktopSnapshot(MonitorFrame[] frames, IReadOnlyList<PixelRect> windows)
{
    private const int BytesPerPixel = 4;

    internal IReadOnlyList<MonitorFrame> Frames => frames;
    internal IReadOnlyList<PixelRect> Windows => windows;
    internal PixelRect Bounds { get; } = frames.Select(frame => frame.Bounds).Aggregate((a, b) => a.Enclose(b));

    internal static DesktopSnapshot Take()
    {
        // The gallery has been hidden. Wait for the compositor before reading the desktop.
        DwmFlush();
        var windows = VisibleWindows();
        var screens = Screen.AllScreens;
        var frames = new List<MonitorFrame>(screens.Length);

        var desktop = GetDC(0);
        if (desktop == 0)
            throw new Win32Exception();

        try
        {
            foreach (var screen in screens)
            {
                var bounds = screen.Bounds;
                var pixels = new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
                if (!pixels.IsEmpty)
                    frames.Add(new MonitorFrame(pixels, ReadMonitor(desktop, pixels)));
            }
        }
        finally
        {
            ReleaseDC(0, desktop);
        }

        if (frames.Count == 0)
            throw new InvalidOperationException("No display is available.");

        return new DesktopSnapshot(frames.ToArray(), windows);
    }

    internal BitmapSource Crop(PixelRect selection)
    {
        foreach (var frame in frames)
        {
            if (selection.Intersect(frame.Bounds) == selection)
            {
                var crop = new CroppedBitmap(frame.Image, ToInt32Rect(selection.RelativeTo(frame.Bounds)));
                crop.Freeze();
                return crop;
            }
        }

        // Display gaps remain black in a rectangle spanning multiple monitors.
        var stride = checked(selection.Width * BytesPerPixel);
        var buffer = new byte[checked(stride * selection.Height)];
        foreach (var frame in frames)
        {
            if (CropPiece.ForDisplay(frame.Bounds, selection) is not { } piece)
                continue;

            var offset = piece.Destination.Y * stride + piece.Destination.X * BytesPerPixel;
            frame.Image.CopyPixels(ToInt32Rect(piece.Source), buffer, stride, offset);
        }
        return CreateImage(selection, buffer, stride);
    }

    private static BitmapSource ReadMonitor(nint desktop, PixelRect bounds)
    {
        var stride = checked(bounds.Width * BytesPerPixel);
        var buffer = new byte[checked(stride * bounds.Height)];
        var info = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = bounds.Width,
            Height = -bounds.Height,
            Planes = 1,
            BitCount = 32
        };

        var memory = CreateCompatibleDC(desktop);
        if (memory == 0)
            throw new Win32Exception();

        nint bitmap = 0;
        nint previous = 0;
        try
        {
            bitmap = CreateDIBSection(memory, ref info, 0, out var bits, 0, 0);
            if (bitmap == 0 || bits == 0)
                throw new Win32Exception();

            previous = SelectObject(memory, bitmap);
            if (previous == 0 || previous == -1 ||
                !BitBlt(memory, 0, 0, bounds.Width, bounds.Height, desktop, bounds.X, bounds.Y, SourceCopyWithLayers))
                throw new Win32Exception();

            GdiFlush();
            Marshal.Copy(bits, buffer, 0, buffer.Length);
            return CreateImage(bounds, buffer, stride);
        }
        finally
        {
            if (previous != 0 && previous != -1)
                SelectObject(memory, previous);
            if (bitmap != 0)
                DeleteObject(bitmap);
            DeleteDC(memory);
        }
    }

    private static BitmapSource CreateImage(PixelRect bounds, byte[] buffer, int stride)
    {
        var image = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Bgr32, null, buffer, stride);
        image.Freeze();
        return image;
    }

    private static IReadOnlyList<PixelRect> VisibleWindows()
    {
        const int extendedStyleIndex = -20;
        const int transparentStyle = 0x20;
        var shell = GetShellWindow();
        var windows = new List<PixelRect>();
        var className = new StringBuilder(256);
        EnumWindows((window, _) =>
        {
            if (window == shell || !IsWindowVisible(window) || IsIconic(window) ||
                (GetWindowLong(window, extendedStyleIndex) & transparentStyle) != 0 ||
                (DwmGetWindowAttribute(window, Cloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0))
                return true;

            className.Clear();
            GetClassName(window, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return true;

            if (DwmGetWindowAttribute(window, ExtendedFrameBounds, out NativeRect bounds, Marshal.SizeOf<NativeRect>()) != 0 &&
                !GetWindowRect(window, out bounds))
                return true;

            var rect = bounds.ToPixels();
            if (!rect.IsEmpty)
                windows.Add(rect);
            return true;
        }, 0);
        return windows;
    }

    private static Int32Rect ToInt32Rect(PixelRect pixels) => new(pixels.X, pixels.Y, pixels.Width, pixels.Height);
}
