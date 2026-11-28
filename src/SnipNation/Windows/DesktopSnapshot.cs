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

internal sealed record MonitorFrame(PixelRect Bounds, BitmapSource Image, string DisplayId = "");

internal sealed record DesktopDisplay(string Id, PixelRect Bounds, bool Primary);

internal sealed record DesktopWindow(nint Handle, PixelRect Bounds)
{
    internal string Id => $"0x{(nuint)Handle:X}";

    internal string ReadTitle()
    {
        var title = new StringBuilder(512);
        GetWindowText(Handle, title, title.Capacity);
        return title.ToString();
    }
}

internal sealed class DesktopSnapshot(MonitorFrame[] frames, IReadOnlyList<DesktopWindow> windows)
{
    private const int BytesPerPixel = 4;

    internal IReadOnlyList<MonitorFrame> Frames => frames;
    internal IReadOnlyList<DesktopWindow> Windows => windows;
    internal PixelRect Bounds { get; } = frames.Select(frame => frame.Bounds).Aggregate((a, b) => a.Enclose(b));

    internal static DesktopSnapshot Take()
    {
        // The gallery has been hidden. Wait for the compositor before reading the desktop.
        DwmFlush();
        var displays = Displays();
        var windows = VisibleWindows(displays);
        var frames = new List<MonitorFrame>(displays.Length);

        var desktop = GetDC(0);
        if (desktop == 0)
            throw new Win32Exception();

        try
        {
            foreach (var display in displays)
                frames.Add(new MonitorFrame(display.Bounds, ReadMonitor(desktop, display.Bounds), display.Id));
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

    internal static DesktopDisplay[] Displays() => Screen.AllScreens.Select(screen =>
        {
            var bounds = screen.Bounds;
            return new DesktopDisplay(screen.DeviceName, new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height), screen.Primary);
        })
        .Where(display => !display.Bounds.IsEmpty)
        .ToArray();

    internal static IReadOnlyList<DesktopWindow> VisibleWindows(IReadOnlyList<DesktopDisplay> displays)
    {
        var shell = GetShellWindow();
        var candidates = new Dictionary<nint, DesktopWindow>();
        var className = new StringBuilder(256);
        EnumDesktopWindows(0, (window, _) =>
        {
            if (window == shell || !IsWindowVisible(window) || IsIconic(window) ||
                IsFullyTransparent(window) ||
                (DwmGetWindowAttribute(window, Cloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0))
                return true;

            className.Clear();
            GetClassName(window, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return true;

            var bounds = WindowBounds(window);
            if (displays.Any(display => !bounds.Intersect(display.Bounds).IsEmpty))
                candidates[window] = new DesktopWindow(window, bounds);
            return true;
        }, 0);

        // Walk the desktop from front to back. Track handles in case the order changes during the walk.
        var windows = new List<DesktopWindow>(candidates.Count);
        var visited = new HashSet<nint>();
        for (var window = GetTopWindow(0); window != 0 && candidates.Count != 0 && visited.Add(window);
             window = GetWindow(window, NextWindow))
        {
            if (candidates.Remove(window, out var candidate))
                windows.Add(candidate);
        }
        windows.AddRange(candidates.Values);
        return windows;
    }

    private static bool IsFullyTransparent(nint window)
    {
        const int extendedStyleIndex = -20;
        const int layeredStyle = 0x00080000;
        const uint uniformOpacity = 0x2;
        // Click-through windows can draw visible content. Exclude windows with zero uniform opacity.
        return (GetWindowLong(window, extendedStyleIndex) & layeredStyle) != 0 &&
            GetLayeredWindowAttributes(window, out _, out var opacity, out var flags) &&
            (flags & uniformOpacity) != 0 && opacity == 0;
    }

    private static PixelRect WindowBounds(nint window)
    {
        if (DwmGetWindowAttribute(window, ExtendedFrameBounds, out NativeRect frame, Marshal.SizeOf<NativeRect>()) == 0 &&
            frame.ToPixels() is { IsEmpty: false } bounds)
            return bounds;

        return GetWindowRect(window, out var rect) ? rect.ToPixels() : default;
    }

    private static Int32Rect ToInt32Rect(PixelRect pixels) => new(pixels.X, pixels.Y, pixels.Width, pixels.Height);
}
