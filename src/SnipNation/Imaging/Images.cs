using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnipNation.Core;

namespace SnipNation.Imaging;

internal static class Images
{
    private static readonly SemaphoreSlim ClipboardGate = new(1, 1);

    internal static BitmapSource Read(string path, int decodeWidth = 0)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = stream;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = decodeWidth;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    internal static ImageSource ReadPreview(CaptureFile capture)
    {
        var preview = Read(capture.OriginalPath, 240);
        Annotation[] annotations;
        try
        {
            annotations = CaptureStore.LoadAnnotations(capture);
        }
        catch (Exception error) when (IsFileError(error))
        {
            return preview;
        }

        if (annotations.Length == 0)
            return preview;

        using var stream = File.OpenRead(capture.OriginalPath);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
        var original = decoder.Frames[0];
        var group = new DrawingGroup();
        using (var context = group.Open())
            Draw(context, preview, new Size(original.PixelWidth, original.PixelHeight), annotations);

        var thumbnail = new DrawingImage(group);
        thumbnail.Freeze();
        return thumbnail;
    }

    internal static void SavePng(BitmapSource image, Stream stream)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
    }

    internal static void Draw(DrawingContext context, BitmapSource image, Size size, IEnumerable<Annotation> annotations)
    {
        var bounds = new Rect(size);
        context.PushClip(new RectangleGeometry(bounds));
        context.DrawImage(image, bounds);
        foreach (var annotation in annotations)
            context.DrawDrawing(AnnotationGraphics.Create(annotation));
        context.Pop();
    }

    internal static BitmapSource Compose(BitmapSource image, IReadOnlyList<Annotation> annotations)
    {
        if (annotations.Count == 0)
            return image;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            Draw(context, image, new Size(image.PixelWidth, image.PixelHeight), annotations);

        var bitmap = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    internal static async Task<bool> CopyAsync(BitmapSource image)
    {
        await ClipboardGate.WaitAsync();
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Clipboard.SetImage(image);
                    return true;
                }
                catch (ExternalException)
                {
                    if (attempt < 2)
                        await Task.Delay(50);
                }
            }
            return false;
        }
        finally
        {
            ClipboardGate.Release();
        }
    }

    internal static bool IsFileError(Exception error) =>
        error is IOException or UnauthorizedAccessException or InvalidDataException or
            JsonException or FormatException or NotSupportedException or ExternalException;
}
