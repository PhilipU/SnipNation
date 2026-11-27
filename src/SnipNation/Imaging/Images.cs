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
        const int thumbnailSize = 240;
        try
        {
            var edits = CaptureStore.LoadEdits(capture);
            if (edits.Crop is { } crop)
            {
                var cropped = Crop(Read(capture.OriginalPath), crop);
                var scale = Math.Min(1, (double)thumbnailSize / Math.Max(cropped.PixelWidth, cropped.PixelHeight));
                return Render(cropped, edits.Annotations, scale);
            }

            var preview = Read(capture.OriginalPath, thumbnailSize);
            if (edits.Annotations.Length == 0)
                return preview;

            using var stream = File.OpenRead(capture.OriginalPath);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
            var original = decoder.Frames[0];
            var group = new DrawingGroup();
            using (var context = group.Open())
                Draw(context, preview, new Size(original.PixelWidth, original.PixelHeight), edits.Annotations);

            var thumbnail = new DrawingImage(group);
            thumbnail.Freeze();
            return thumbnail;
        }
        catch (Exception error) when (IsFileError(error))
        {
            return Read(capture.OriginalPath, thumbnailSize);
        }
    }

    internal static BitmapSource Crop(BitmapSource original, PixelRect? selection)
    {
        if (selection is not { } crop)
            return original;

        if (crop.X < 0 || crop.Y < 0 || crop.IsEmpty ||
            crop.Width > original.PixelWidth - crop.X || crop.Height > original.PixelHeight - crop.Y)
            throw new InvalidDataException("The crop is outside the original screenshot.");

        var image = new CroppedBitmap(original, new Int32Rect(crop.X, crop.Y, crop.Width, crop.Height));
        image.Freeze();
        return image;
    }

    internal static void SavePng(BitmapSource image, Stream stream) =>
        Encode(image, stream, new PngBitmapEncoder());

    internal static void SaveJpeg(BitmapSource image, Stream stream) =>
        Encode(image, stream, new JpegBitmapEncoder { QualityLevel = 92 });

    private static void Encode(BitmapSource image, Stream stream, BitmapEncoder encoder)
    {
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

    internal static BitmapSource Compose(BitmapSource image, IReadOnlyList<Annotation> annotations) =>
        annotations.Count == 0 ? image : Render(image, annotations, 1);

    private static BitmapSource Render(BitmapSource image, IReadOnlyList<Annotation> annotations, double scale)
    {
        var width = Math.Max(1, (int)Math.Round(image.PixelWidth * scale));
        var height = Math.Max(1, (int)Math.Round(image.PixelHeight * scale));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform((double)width / image.PixelWidth, (double)height / image.PixelHeight));
            Draw(context, image, new Size(image.PixelWidth, image.PixelHeight), annotations);
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
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
