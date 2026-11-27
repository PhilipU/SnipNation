using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
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
    private const int PageWidth = 760;
    private const int PageHeight = 448;
    private static readonly Brush Paper = new SolidColorBrush(Color.FromRgb(255, 248, 234));

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (e.Args.Length is < 1 or > 2 || (e.Args.Length == 2 && e.Args[1] != "--desktop"))
                throw new ArgumentException("Pass the output directory and optionally --desktop.");

            var output = Directory.CreateDirectory(e.Args[0]);
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            var examples = Directory.CreateTempSubdirectory("snipnation-screenshots-");
            try
            {
                await Generate(output.FullName, new CaptureStore(examples.FullName), e.Args.Length == 2);
            }
            finally
            {
                examples.Delete(recursive: true);
            }
            Shutdown();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Shutdown(1);
        }
    }

    private static async Task Generate(string output, CaptureStore store, bool desktop)
    {
        var firstPage = DocumentPage("Lorem ipsum");
        var secondPage = DocumentPage("Dolor sit amet");
        var thirdPage = DocumentPage("Consectetur adipiscing");
        var green = Brand.Argb("LightAccent");
        var lime = Brand.Argb("Accent");

        store.SaveCapture(stream => Images.SavePng(thirdPage, stream));

        var drawing = store.SaveCapture(stream => Images.SavePng(firstPage, stream));
        CaptureStore.SaveEdits(drawing, new CaptureEdits(
        [
            new(AnnotationKind.Arrow, [new(718, 161), new(674, 136)], green, 5),
            new(AnnotationKind.Line, [new(62, 239), new(297, 239)], green, 3),
            new(AnnotationKind.Freehand,
                [new(51, 321), new(49, 310), new(65, 303), new(115, 299), new(227, 299),
                 new(294, 303), new(309, 311), new(307, 322), new(281, 331),
                 new(218, 334), new(115, 333), new(67, 328), new(51, 321)], green, 3)
        ]));

        var text = store.SaveCapture(stream => Images.SavePng(secondPage, stream));
        CaptureStore.SaveEdits(text, new CaptureEdits(
        [
            new(AnnotationKind.Highlight, [new(61, 122), new(445, 146)], (lime & 0x00FFFFFF) | 0x66000000, 4),
            new(AnnotationKind.Text, [new(62, 384)], green, 23, "Summarize this section.")
        ]));

        var gallery = new GalleryWindow();
        foreach (var capture in store.Recent(GalleryWindow.HistoryLimit))
            gallery.AddOrUpdate(capture, Images.ReadPreview(capture));
        gallery.Present(activate: false, previousForeground: 0);
        SaveView(gallery, output, "gallery.png");
        if (desktop)
        {
            var document = ShowDocument(firstPage);
            await SaveDesktop(output, "desktop-gallery.png");
            document.Close();
        }
        gallery.CloseForExit();

        await SaveEditor(drawing, AnnotationKind.Freehand, output, "editor-drawing.png", desktop);
        await SaveEditor(text, AnnotationKind.Text, output, "editor-text.png", desktop: false);
        SaveCaptureOverlay(firstPage, output);
    }

    private static async Task SaveEditor(CaptureFile capture, AnnotationKind tool, string output, string file, bool desktop)
    {
        var editor = new EditorWindow(capture, Images.Read(capture.OriginalPath), CaptureStore.LoadEdits(capture));
        ShowForScreenshot(editor);
        ((RadioButton)editor.FindName($"{tool}Tool")).IsChecked = true;
        SaveView(editor, output, file);
        if (desktop)
        {
            var workArea = SystemParameters.WorkArea;
            editor.Width = Math.Min(editor.Width, Math.Floor(workArea.Width * 0.84));
            editor.Height = Math.Min(editor.Height, Math.Floor(workArea.Height * 0.84));
            editor.Left = workArea.Left + (workArea.Width - editor.Width) / 2;
            editor.Top = workArea.Top + (workArea.Height - editor.Height) / 2;
            editor.ShowInTaskbar = true;
            editor.Activate();
            await SaveDesktop(output, "desktop-editor.png");
        }
        editor.Close();
    }

    private static Window ShowDocument(BitmapSource page)
    {
        var workArea = SystemParameters.WorkArea;
        var width = Math.Min(page.PixelWidth, workArea.Width * 0.66);
        var document = new Window
        {
            Title = "Lorem ipsum",
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = workArea.Left + workArea.Width * 0.055,
            Top = workArea.Top + workArea.Height * 0.075,
            Width = Math.Round(width),
            Height = Math.Round(width * page.PixelHeight / page.PixelWidth) + 32,
            ResizeMode = ResizeMode.NoResize,
            Background = Paper,
            Content = new Image { Source = page }
        };
        ShowForScreenshot(document);
        return document;
    }

    private static async Task SaveDesktop(string output, string file)
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == 0 || !NativeMethods.IsWindowVisible(taskbar) || !NativeMethods.GetWindowRect(taskbar, out var bounds))
            throw new InvalidOperationException("Desktop screenshots require a visible Windows taskbar.");

        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(350);
        var taskbarBounds = bounds.ToPixels();
        var primary = DesktopSnapshot.Take().Frames.Single(frame => !frame.Bounds.Intersect(taskbarBounds).IsEmpty);
        SaveImage(primary.Image, output, file);
    }

    private static void SaveCaptureOverlay(BitmapSource example, string output)
    {
        var bounds = new PixelRect(0, 0, 884, 536);
        var window = new PixelRect(62, 44, PageWidth, PageHeight);
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

    private static BitmapSource DocumentPage(string heading)
    {
        var image = new DrawingVisual();
        using (var drawing = image.RenderOpen())
        {
            var ink = new SolidColorBrush(Color.FromRgb(43, 55, 75));
            var title = new SolidColorBrush(Color.FromRgb(107, 58, 70));
            var rule = new SolidColorBrush(Color.FromRgb(199, 182, 167));
            drawing.DrawRectangle(Paper, null, new Rect(0, 0, PageWidth, PageHeight));
            WriteText(drawing, heading, 35, title, 44, FontWeights.Bold);
            drawing.DrawLine(new Pen(rule, 1), new Point(62, 100), new Point(698, 100));
            WriteText(drawing, "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
                19, ink, 120, FontWeights.Normal);
            WriteText(drawing, "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.",
                19, ink, 216, FontWeights.Normal);
            WriteText(drawing, "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur.",
                19, ink, 304, FontWeights.Normal);
        }

        return Render(image, PageWidth, PageHeight);
    }

    private static void WriteText(DrawingContext drawing, string text, int size, Brush brush, double y, FontWeight weight)
    {
        var font = new Typeface(new FontFamily("Georgia"), FontStyles.Normal, weight, FontStretches.Normal);
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, size, brush, 1)
        {
            MaxTextWidth = 636,
            LineHeight = 27
        };
        drawing.DrawText(formatted, new Point(62, y));
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
        window.Dispatcher.Invoke(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
        var view = (FrameworkElement)window.Content;
        var bitmap = Render(view, (int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight));
        SaveImage(bitmap, output, file);
    }

    private static void SaveImage(BitmapSource bitmap, string output, string file)
    {
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? windowName);
}
