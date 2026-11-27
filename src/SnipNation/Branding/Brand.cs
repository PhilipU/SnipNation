using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SnipNation.Windows;

namespace SnipNation.Branding;

internal static class Brand
{
    internal static readonly FontFamily FontFamily =
        new(new Uri("pack://application:,,,/SnipNation;component/"), "./Assets/Fonts/#Inter");

    internal static Color ColorFor(string role) => (Color)Application.Current.FindResource($"Brand{role}");

    internal static Brush BrushFor(string key) => (Brush)Application.Current.FindResource(key);

    internal static uint Argb(string role)
    {
        var color = ColorFor(role);
        return (uint)color.A << 24 | (uint)color.R << 16 | (uint)color.G << 8 | color.B;
    }

    internal static void ApplyCaption(Window window)
    {
        const int borderColor = 34;
        const int captionColor = 35;
        const int textColor = 36;
        var handle = new WindowInteropHelper(window).Handle;
        var background = ToColorRef(ColorFor("Background"));
        var foreground = ToColorRef(ColorFor("Foreground"));
        NativeMethods.DwmSetWindowAttribute(handle, borderColor, in background, sizeof(int));
        NativeMethods.DwmSetWindowAttribute(handle, captionColor, in background, sizeof(int));
        NativeMethods.DwmSetWindowAttribute(handle, textColor, in foreground, sizeof(int));
    }

    private static int ToColorRef(Color color) => color.R | color.G << 8 | color.B << 16;
}
