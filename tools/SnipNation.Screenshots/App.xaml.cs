using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnipNation.Branding;
using SnipNation.Core;
using SnipNation.Editor;
using SnipNation.Gallery;
using SnipNation.Imaging;
using SnipNation.Windows;

namespace SnipNation.Screenshots;

public partial class ScreenshotApp : Application
{
    private const int ExampleWidth = 760;
    private const int ExampleHeight = 340;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length != 1)
            throw new ArgumentException("Pass the directory for the generated screenshots.");

        var output = Directory.CreateDirectory(e.Args[0]);
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var examples = Directory.CreateTempSubdirectory("snipnation-screenshots-");
        try
        {
            Generate(output.FullName, new CaptureStore(examples.FullName));
        }
        finally
        {
            examples.Delete(recursive: true);
        }
        Shutdown();
    }

    private static void Generate(string output, CaptureStore store)
    {
        var notifications = WorkspaceExample("Notifications", "Weekly summary", "Product updates");
        var access = WorkspaceExample("Access", "Link sharing", "Guest access");
        var darkGreen = Brand.Argb("LightAccent");
        var lime = Brand.Argb("Accent");

        var drawing = store.SaveCapture(stream => Images.SavePng(notifications, stream));
        CaptureStore.SaveAnnotations(drawing,
        [
            new(AnnotationKind.Arrow, [new(520, 122), new(643, 122)], darkGreen, 4),
            new(AnnotationKind.Line, [new(48, 82), new(207, 82)], darkGreen, 4),
            new(AnnotationKind.Freehand,
                [new(559, 290), new(555, 275), new(568, 260), new(602, 254), new(672, 254),
                 new(711, 261), new(733, 276), new(729, 297), new(703, 310), new(648, 314),
                 new(595, 311), new(571, 304), new(559, 290)], lime, 4)
        ]);

        var text = store.SaveCapture(stream => Images.SavePng(access, stream));
        CaptureStore.SaveAnnotations(text,
        [
            new(AnnotationKind.Highlight, [new(47, 108), new(167, 137)], (lime & 0x00FFFFFF) | 0x66000000, 4),
            new(AnnotationKind.Text, [new(48, 258)], Brand.Argb("Background"), 24, "Enable link sharing\nbefore saving.")
        ]);

        var gallery = new GalleryWindow();
        foreach (var capture in store.Recent(GalleryWindow.HistoryLimit))
            gallery.AddOrUpdate(capture, Images.ReadPreview(capture));
        ShowForScreenshot(gallery);
        SaveView(gallery, output, "gallery.png");
        gallery.CloseForExit();

        SaveEditor(drawing, AnnotationKind.Freehand, output, "editor-drawing.png");
        SaveEditor(text, AnnotationKind.Text, output, "editor-text.png");
        SaveCaptureOverlay(notifications, output);
    }

    private static void SaveEditor(CaptureFile capture, AnnotationKind tool, string output, string file)
    {
        var editor = new EditorWindow(capture, Images.Read(capture.OriginalPath), CaptureStore.LoadAnnotations(capture));
        ShowForScreenshot(editor);
        ((RadioButton)editor.FindName($"{tool}Tool")).IsChecked = true;
        SaveView(editor, output, file);
        editor.Close();
    }

    private static void SaveCaptureOverlay(BitmapSource example, string output)
    {
        var bounds = new PixelRect(0, 0, 880, 440);
        var window = new PixelRect(60, 50, ExampleWidth, ExampleHeight);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
        {
            drawing.DrawRectangle(Brand.BrushFor("MutedInkBrush"), null, new Rect(0, 0, bounds.Width, bounds.Height));
            drawing.DrawImage(example, new Rect(window.X, window.Y, window.Width, window.Height));
        }

        var selection = new CaptureSelection(bounds, [window]);
        selection.Move(new PixelPoint(window.X + 20, window.Y + 20));
        var overlay = new CaptureOverlay(new MonitorFrame(bounds, Render(background, bounds.Width, bounds.Height)));
        overlay.DrawSelection(selection.Highlight);
        ShowForScreenshot(overlay);
        SaveView(overlay, output, "capture.png");
        overlay.Close();
    }

    private static BitmapSource WorkspaceExample(string heading, string firstSetting, string secondSetting)
    {
        var image = new DrawingVisual();
        using (var drawing = image.RenderOpen())
        {
            var background = new SolidColorBrush(Brand.ColorFor("LightBackground"));
            var green = new SolidColorBrush(Brand.ColorFor("LightAccent"));
            var ink = Brand.BrushFor("PaneBrush");
            drawing.DrawRectangle(background, null, new Rect(0, 0, ExampleWidth, ExampleHeight));
            WriteText(drawing, heading, 36, ink, 48, 30);
            WriteText(drawing, firstSetting, 23, ink, 48, 107);
            WriteText(drawing, secondSetting, 23, ink, 48, 181);
            drawing.DrawLine(new Pen(Brand.BrushFor("DividerBrush"), 1), new Point(48, 162), new Point(712, 162));
            drawing.DrawRoundedRectangle(green, null, new Rect(655, 108, 57, 28), 14, 14);
            drawing.DrawRoundedRectangle(Brand.BrushFor("MutedInkBrush"), null, new Rect(655, 182, 57, 28), 14, 14);
            drawing.DrawEllipse(Brand.BrushFor("InkBrush"), null, new Point(698, 122), 9, 9);
            drawing.DrawEllipse(Brand.BrushFor("InkBrush"), null, new Point(669, 196), 9, 9);
            drawing.DrawRoundedRectangle(green, null, new Rect(571, 263, 143, 43), 6, 6);
            WriteText(drawing, "Save changes", 18, Brand.BrushFor("InkBrush"), 586, 274);
        }

        return Render(image, ExampleWidth, ExampleHeight);
    }

    private static void WriteText(DrawingContext drawing, string text, int size, Brush brush, double x, double y)
    {
        var font = new Typeface(Brand.FontFamily, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, size, brush, 1);
        drawing.DrawText(formatted, new Point(x, y));
    }

    private static void ShowForScreenshot(Window window)
    {
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();
        window.Dispatcher.Invoke(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
    }

    private static void SaveView(Window window, string output, string file)
    {
        window.UpdateLayout();
        var view = (FrameworkElement)window.Content;
        var bitmap = Render(view, (int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight));
        using var stream = File.Create(Path.Combine(output, file));
        Images.SavePng(bitmap, stream);
        Console.WriteLine($"Created {file} ({bitmap.PixelWidth} x {bitmap.PixelHeight}).");
    }

    private static RenderTargetBitmap Render(Visual view, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        return bitmap;
    }
}
