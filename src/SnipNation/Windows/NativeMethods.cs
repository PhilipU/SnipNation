using System.Runtime.InteropServices;
using System.Text;
using SnipNation.Core;

namespace SnipNation.Windows;

internal static class NativeMethods
{
    internal const int HotkeyMessage = 0x0312;
    internal const uint PrintScreen = 0x2C;
    internal const uint NoRepeat = 0x4000;
    internal const uint ControlAndShift = 0x0006;
    internal const uint NoActivateOrZOrder = 0x0014;
    internal const uint SourceCopyWithLayers = 0x40CC0020;
    internal const int ExtendedFrameBounds = 9;
    internal const int Cloaked = 14;

    internal delegate bool WindowCallback(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly PixelRect ToPixels() => PixelRect.FromEdges(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint ImageSize;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public uint ColorsUsed;
        public uint ImportantColors;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    internal static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    internal static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(WindowCallback callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(nint window, StringBuilder className, int bufferSize);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(nint window, out NativeRect bounds);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmFlush();

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(nint window, int attribute, out NativeRect value, int size);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(nint window, int attribute, in int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint window, nint dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateDIBSection(nint dc, ref BitmapInfoHeader info, uint usage, out nint pixels,
        nint section, uint offset);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint SelectObject(nint dc, nint obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX,
        int sourceY, uint rasterOperation);

    [DllImport("gdi32.dll")]
    internal static extern bool GdiFlush();

    internal static PixelPoint CursorPosition()
    {
        GetCursorPos(out var point);
        return new PixelPoint(point.X, point.Y);
    }
}
