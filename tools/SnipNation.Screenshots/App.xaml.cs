using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

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
                var store = new CaptureStore(Path.Combine(examples.FullName, "Captures"));
                var presets = new ToolPresets(Path.Combine(examples.FullName, "AnnotationPresets"));
                await Generate(output.FullName, store, presets, e.Args.Length == 2);
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

    private static async Task Generate(string output, CaptureStore store, ToolPresets presets, bool desktop)
    {
        var firstPage = DocumentPage("Lorem ipsum");
        var secondPage = DocumentPage("Dolor sit amet");
        var thirdPage = DocumentPage("Consectetur adipiscing");
        SaveExcerpt(store, firstPage, new PixelRect(48, 40, 665, 149));
        SaveExcerpt(store, secondPage, new PixelRect(48, 207, 665, 159));
        SaveExcerpt(store, thirdPage, new PixelRect(48, 105, 665, 166));

        var guides = store.SaveCapture(stream => Images.SavePng(thirdPage, stream));
        CaptureStore.SaveEdits(guides, new CaptureEdits(
        [
            presets.Get(AnnotationKind.Frame, 1).CreateAnnotation(AnnotationKind.Frame,
                [new(52, 35), new(514, 93)], 1),
            presets.Get(AnnotationKind.Number, 0).CreateAnnotation(AnnotationKind.Number,
                [new(28, 170), new(52, 133)], 0, number: 1),
            presets.Get(AnnotationKind.Number, 2).CreateAnnotation(AnnotationKind.Number,
                [new(724, 243), new(686, 230)], 2, number: 2)
        ]));

        var drawing = store.SaveCapture(stream => Images.SavePng(firstPage, stream));
        CaptureStore.SaveEdits(drawing, new CaptureEdits(
        [
            presets.Get(AnnotationKind.Arrow, 2).CreateAnnotation(AnnotationKind.Arrow, [new(718, 161), new(674, 136)], 2),
            presets.Get(AnnotationKind.Line, 1).CreateAnnotation(AnnotationKind.Line, [new(62, 244), new(290, 244)], 1),
            presets.Get(AnnotationKind.Freehand, 0).CreateAnnotation(AnnotationKind.Freehand,
                [new(51, 321), new(49, 312), new(62, 305), new(94, 301), new(144, 299),
                 new(194, 299), new(229, 302), new(243, 308), new(247, 316), new(237, 326),
                 new(214, 331), new(169, 334), new(114, 333), new(76, 330), new(51, 321)], 0)
        ]));

        var text = store.SaveCapture(stream => Images.SavePng(secondPage, stream));
        var textEdits = new CaptureEdits(
        [
            presets.Get(AnnotationKind.Highlight, 3)
                .CreateAnnotation(AnnotationKind.Highlight, [new(61, 122), new(305, 146)], 3),
            presets.Get(AnnotationKind.Text, 4).CreateAnnotation(AnnotationKind.Text, [new(62, 382)], 4, "Summarize this section.")
        ]).CropTo(new PixelRect(18, 16, 724, 416));
        CaptureStore.SaveEdits(text, textEdits);
        VerifyJpeg(text);

        var gallery = new GalleryWindow();
        foreach (var capture in store.Recent(GalleryWindow.HistoryLimit))
            gallery.AddOrUpdate(capture, Images.ReadPreview(capture));
        gallery.Present(activate: false, previousForeground: 0);
        SaveView(gallery, output, "gallery.png");
        if (desktop)
        {
            var document = ShowDocument(firstPage);
            await SaveDesktop(gallery, output, "desktop-gallery.png");
            document.Close();
        }
        gallery.CloseForExit();

        var settings = new SettingsWindow(@"C:\Users\Alex\Pictures\SnipNation",
            @"C:\Users\Alex\AppData\Local\SnipNationData\Captures", UpdateChannel.Stable);
        ShowForScreenshot(settings);
        SaveView(settings, output, "settings.png");
        settings.Close();

        await SaveEditor(drawing, presets, AnnotationKind.Freehand, output, "editor-drawing.png", desktop);
        await SaveEditor(text, presets, AnnotationKind.Text, output, "editor-text.png", desktop: false);
        SavePresetPickers(drawing, presets, output, Enum.GetValues<AnnotationKind>()
            .Where(kind => kind is not (AnnotationKind.Frame or AnnotationKind.Number)));
        SavePresetPickers(guides, presets, output, [AnnotationKind.Frame, AnnotationKind.Number]);
        SaveCaptureOverlay(firstPage, output);
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            await VerifyWindowSnap();
            await VerifyEditorApply(text, presets);
            await VerifyUpdateChannels(Directory.CreateDirectory(Path.Combine(store.DirectoryPath, "Update test")).FullName);
        }
    }

    private static async Task SaveEditor(CaptureFile capture, ToolPresets presets, AnnotationKind tool, string output,
        string file, bool desktop)
    {
        var editor = new EditorWindow(capture, Images.Read(capture.OriginalPath), CaptureStore.ReadEdits(capture), presets);
        ShowForScreenshot(editor, desktop);
        ((RadioButton)editor.FindName($"{tool}Tool")).IsChecked = true;
        SaveView(editor, output, file);
        if (desktop)
        {
            var workArea = SystemParameters.WorkArea;
            editor.Width = Math.Min(editor.Width, Math.Floor(workArea.Width * 0.84));
            editor.Height = Math.Min(editor.Height, Math.Floor(workArea.Height * 0.84));
            editor.Left = workArea.Left + (workArea.Width - editor.Width) / 2;
            editor.Top = workArea.Top + (workArea.Height - editor.Height) / 2;
            editor.Activate();
            await SaveDesktop(editor, output, "desktop-editor.png");
        }
        editor.Close();
    }

    private static async Task VerifyWindowSnap()
    {
        const int visiblePopup = unchecked((int)0x90000000);
        const int topmostToolWindow = 0x88;
        const int transparentStyle = 0x20;
        var display = DesktopSnapshot.Displays().Single(item => item.Primary).Bounds;
        var mainSettings = new HwndSourceParameters("Window snap test", 360, 240)
        {
            WindowStyle = visiblePopup,
            ExtendedWindowStyle = topmostToolWindow,
            PositionX = display.X + 40,
            PositionY = display.Y + 40
        };
        using var main = new HwndSource(mainSettings) { RootVisual = new Border { Background = Brushes.CornflowerBlue } };
        var popupSettings = new HwndSourceParameters("", 160, 100)
        {
            WindowStyle = visiblePopup,
            ExtendedWindowStyle = topmostToolWindow | transparentStyle,
            ParentWindow = main.Handle,
            PositionX = mainSettings.PositionX + 100,
            PositionY = mainSettings.PositionY + 70
        };
        using var popup = new HwndSource(popupSettings) { RootVisual = new Border { Background = Brushes.Gold } };
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(450);

        var snapshot = DesktopSnapshot.Take();
        var mainBounds = snapshot.Windows.FirstOrDefault(window => window.Handle == main.Handle)?.Bounds
            ?? throw new InvalidOperationException("The main window is missing from the capture targets.");
        var popupBounds = snapshot.Windows.FirstOrDefault(window => window.Handle == popup.Handle)?.Bounds
            ?? throw new InvalidOperationException("The visible owned pop-up is missing from the capture targets.");
        var selection = new CaptureSelection(snapshot.Bounds, snapshot.Windows.Select(window => window.Bounds).ToArray());
        var mainPoint = new PixelPoint(mainSettings.PositionX + 30, mainSettings.PositionY + 30);
        var popupPoint = new PixelPoint(popupSettings.PositionX + 80, popupSettings.PositionY + 50);

        selection.Move(popupPoint);
        if (selection.Highlight != popupBounds)
            throw new InvalidOperationException("Hovering over the pop-up did not outline the pop-up.");
        selection.Press(popupPoint, 4);
        if (selection.Release(popupPoint) is not { } popupArea || popupArea != popupBounds)
            throw new InvalidOperationException("Clicking the pop-up did not capture its bounds.");

        selection.Move(mainPoint);
        if (selection.Confirm() is not { } mainArea || mainArea != mainBounds)
            throw new InvalidOperationException("Confirming over the exposed main window did not capture its full bounds.");

        var mainImage = snapshot.Crop(mainArea);
        var popupImage = snapshot.Crop(popupArea);
        if (!HasColor(mainImage, mainArea, mainPoint, Colors.CornflowerBlue) ||
            !HasColor(mainImage, mainArea, popupPoint, Colors.Gold) ||
            !HasColor(popupImage, popupArea, popupPoint, Colors.Gold))
            throw new InvalidOperationException("The crops did not preserve the visible main window and pop-up pixels.");

        Console.WriteLine("Verified window snapping and crop pixels for an overlapping main window and owned pop-up.");

        static bool HasColor(BitmapSource image, PixelRect area, PixelPoint point, Color color)
        {
            var pixel = ReadPixel(image, new Int32Rect(point.X - area.X, point.Y - area.Y, 1, 1));
            return pixel[0] == color.B && pixel[1] == color.G && pixel[2] == color.R;
        }
    }

    private static async Task VerifyEditorApply(CaptureFile capture, ToolPresets presets)
    {
        var document = CaptureStore.ReadEdits(capture);
        var edits = document.Edits;
        var original = Images.Read(capture.OriginalPath);
        var expected = Images.Compose(Images.Crop(original, edits.Crop), edits.Annotations);
        var editor = new EditorWindow(capture, original, document, presets);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.Closed += (_, _) => closed.SetResult();
        ShowForScreenshot(editor);
        ((Button)editor.FindName("ApplyEditsButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var copied = Clipboard.GetImage();
        var highlightPixel = new Int32Rect(50, 115, 1, 1);
        if (copied is null || copied.PixelWidth != expected.PixelWidth || copied.PixelHeight != expected.PixelHeight ||
            !ReadPixel(copied, highlightPixel).SequenceEqual(ReadPixel(expected, highlightPixel)))
            throw new InvalidOperationException("Apply did not copy the cropped and annotated image.");

        Console.WriteLine("Verified that Apply copies the edited image and closes the editor.");
    }

    private static byte[] ReadPixel(BitmapSource image, Int32Rect pixel)
    {
        var colors = new byte[4];
        new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(pixel, colors, 4, 0);
        return colors;
    }

    private static async Task VerifyUpdateChannels(string folder)
    {
        const string currentStable = "1.2.3";
        const string nextStable = "1.2.4";
        const string nextBeta = "1.2.5-beta.42";
        WriteFeed("win", nextStable);
        WriteFeed("beta", nextBeta);

        var beta = new VelopackAsset
        {
            PackageId = "SnipNation",
            Version = SemanticVersion.Parse(nextBeta),
            Type = VelopackAssetType.Full
        };
        var stableInstall = new TestVelopackLocator("SnipNation", currentStable, folder,
            appDir: null, rootDir: null, updateExe: null, channel: "win", localPackage: beta);
        var updates = new UpdateService(stableInstall, preferred: null);
        var feeds = new SimpleFileSource(new DirectoryInfo(folder));
        var stable = await updates.CreateManager(updates.Channel, feeds).CheckForUpdatesAsync();
        if (updates.Channel != UpdateChannel.Stable || stable?.TargetFullRelease.Version.ToString() != nextStable ||
            UpdateService.CanAutoApply(stableInstall, preferred: null) ||
            !UpdateService.CanAutoApply(stableInstall, UpdateChannel.Beta))
            throw new InvalidOperationException("Stable did not exclude a cached beta unless the user opted in.");

        updates.SelectChannel(UpdateChannel.Beta);
        var preview = await updates.CreateManager(updates.Channel, feeds).CheckForUpdatesAsync();
        if (preview?.TargetFullRelease.Version.ToString() != nextBeta)
            throw new InvalidOperationException("Beta did not select the prerelease from its feed.");

        var betaInstall = new TestVelopackLocator("SnipNation", nextBeta, folder,
            appDir: null, rootDir: null, updateExe: null, channel: "beta");
        updates = new UpdateService(betaInstall, preferred: null);
        if (updates.Channel != UpdateChannel.Beta)
            throw new InvalidOperationException("The beta installer did not select its update channel.");

        updates.SelectChannel(UpdateChannel.Stable);
        var rollback = await updates.CreateManager(updates.Channel, feeds).CheckForUpdatesAsync();
        if (rollback is not { IsDowngrade: true } || rollback.TargetFullRelease.Version.ToString() != nextStable)
            throw new InvalidOperationException("Switching to Stable did not offer the latest stable package.");

        Console.WriteLine("Verified stable and beta update selection, cached package isolation, and rollback to stable.");

        void WriteFeed(string channel, string version)
        {
            var suffix = channel == "win" ? "" : $"-{channel}";
            var entry = new
            {
                PackageId = "SnipNation", Version = version, Type = "Full",
                FileName = $"SnipNation-{version}{suffix}-full.nupkg"
            };
            File.WriteAllText(Path.Combine(folder, $"releases.{channel}.json"), JsonSerializer.Serialize(new { Assets = new[] { entry } }));
        }
    }

    private static void SavePresetPickers(CaptureFile capture, ToolPresets presets, string output,
        IEnumerable<AnnotationKind> kinds)
    {
        var edits = CaptureStore.ReadEdits(capture);
        var editor = new EditorWindow(capture, Images.Read(capture.OriginalPath), edits, presets);
        ShowForScreenshot(editor);
        var panel = (Border)editor.FindName("PresetPanel");
        var buttons = (UniformGrid)editor.FindName("PresetButtons");
        var viewport = (ScrollViewer)editor.FindName("Viewport");
        foreach (var kind in kinds)
        {
            var tool = (RadioButton)editor.FindName($"{kind}Tool");
            tool.IsChecked = true;
            tool.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if (!panel.IsVisible || buttons.Children.Count != AnnotationPresetStore.SlotCount ||
                buttons.Children.Cast<Button>().Any(button => button.Content is not Image { Source: not null }))
                throw new InvalidOperationException($"The {kind} tool did not show six preset previews.");

            editor.UpdateLayout();
            var top = panel.TranslatePoint(new Point(), viewport).Y;
            if (top < 0 || top + panel.ActualHeight > viewport.ActualHeight)
                throw new InvalidOperationException($"The {kind} preset picker overlaps the editor controls.");

            var file = kind switch
            {
                AnnotationKind.Arrow => "presets-arrow.png",
                AnnotationKind.Highlight => "presets-highlight.png",
                AnnotationKind.Text => "presets-text.png",
                AnnotationKind.Frame => "presets-frame.png",
                AnnotationKind.Number => "presets-number.png",
                _ => null
            };
            if (file is not null)
                SaveView(editor, output, file);

            ((Button)buttons.Children[1]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if (panel.IsVisible)
                throw new InvalidOperationException($"The {kind} preset picker stayed open after selection.");

            if (kind is AnnotationKind.Frame or AnnotationKind.Number)
            {
                var mark = edits.Edits.Annotations.First(annotation => annotation.Kind == kind);
                var settings = new AnnotationStyleDialog(editor, mark);
                if (settings.SelectedStyle != mark.Style || settings.Number != mark.Number)
                    throw new InvalidOperationException($"The {kind} settings did not load the saved annotation.");
                settings.Close();
            }
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
        document.ShowInTaskbar = false;
        document.Show();
        return document;
    }

    private static async Task SaveDesktop(Window window, string output, string file)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var handle = new WindowInteropHelper(window).Handle;
        var name = window is GalleryWindow ? "SnipNation gallery" : "SnipNation editor";
        const int attempts = 12;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            await Task.Delay(450);
            var problem = VisibilityProblem(FindWindow("Shell_TrayWnd", null), "Windows taskbar", out var taskbar)
                ?? VisibilityProblem(handle, name, out _);
            if (problem is null)
            {
                var primary = DesktopSnapshot.Take().Frames.Single(frame => frame.Bounds.Contains(taskbar));
                SaveImage(primary.Image, output, file);
                return;
            }
            if (attempt == attempts)
                throw new InvalidOperationException(problem);
        }
    }

    private static string? VisibilityProblem(nint handle, string name, out PixelPoint center)
    {
        center = default;
        if (handle == 0 || !NativeMethods.IsWindowVisible(handle) || !NativeMethods.GetWindowRect(handle, out var bounds))
            return $"The {name} is unavailable.";

        center = new PixelPoint(bounds.Left + (bounds.Right - bounds.Left) / 2,
            bounds.Top + (bounds.Bottom - bounds.Top) / 2);
        const uint rootWindow = 2;
        var screenPoint = new NativeMethods.NativePoint { X = center.X, Y = center.Y };
        var topWindow = GetAncestor(WindowFromPoint(screenPoint), rootWindow);
        if (topWindow == handle)
            return null;

        var windowClass = new StringBuilder(256);
        NativeMethods.GetClassName(topWindow, windowClass, windowClass.Capacity);
        return $"The {name} is covered by another window ({windowClass}).";
    }

    private static void SaveExcerpt(CaptureStore store, BitmapSource page, PixelRect bounds) =>
        store.SaveCapture(stream => Images.SavePng(Images.Crop(page, bounds), stream));

    private static void VerifyJpeg(CaptureFile capture)
    {
        var edits = CaptureStore.LoadEdits(capture);
        var image = Images.Compose(Images.Crop(Images.Read(capture.OriginalPath), edits.Crop), edits.Annotations);
        using var stream = new MemoryStream();
        Images.SaveJpeg(image, stream);
        stream.Position = 0;
        var jpeg = new JpegBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames.Single();
        if (jpeg.PixelWidth != image.PixelWidth || jpeg.PixelHeight != image.PixelHeight)
            throw new InvalidOperationException("The JPEG export dimensions do not match the edited screenshot.");
        Console.WriteLine($"Verified JPEG export of the cropped and annotated screenshot ({jpeg.PixelWidth} x {jpeg.PixelHeight}).");
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

    private static void ShowForScreenshot(Window window, bool activate = false)
    {
        window.ShowActivated = activate;
        window.ShowInTaskbar = activate;
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

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativeMethods.NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);
}
