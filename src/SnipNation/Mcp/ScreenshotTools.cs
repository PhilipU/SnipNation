using System.ComponentModel;
using System.Security;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SnipNation.Core;
using SnipNation.Editor;
using SnipNation.Imaging;
using SnipNation.Windows;

namespace SnipNation.Mcp;

internal enum CaptureTarget
{
    Window,
    Display,
    Region,
    Desktop,
    Interactive
}

internal sealed class ScreenshotTools(Dispatcher dispatcher)
{
    private readonly ToolPresets _presets = new(UserSettings.PresetFolder);
    private readonly Dictionary<CaptureFile, EditorWindow> _editors = [];
    private bool _selecting;

    [McpServerTool(Name = "list_capture_targets", ReadOnly = true)]
    [Description("List visible windows and displays with IDs and bounds in desktop pixels. Desktop origins can be negative.")]
    public Task<CallToolResult> ListCaptureTargets(CancellationToken cancellationToken) => OnDesktop(() =>
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var displays = DesktopSnapshot.Displays();
        return McpHost.Result(new
        {
            Displays = displays,
            Windows = DesktopSnapshot.VisibleWindows(displays).Select(window => new
            {
                window.Id, Title = window.ReadTitle(), window.Bounds, Foreground = window.Handle == foreground
            }).ToArray()
        });
    }, cancellationToken);

    [McpServerTool(Name = "capture_screenshot", Destructive = false)]
    [Description("Save a screenshot to the SnipNation gallery. Window capture takes visible desktop pixels at the window's bounds. Interactive opens the normal selection overlay.")]
    public Task<CallToolResult> CaptureScreenshot(
        [Description("Capture a window, display, region, entire desktop, or interactive selection.")] CaptureTarget target,
        [Description("Window or display ID from list_capture_targets.")] string? targetId = null,
        [Description("Rectangle in desktop pixels for a region capture.")] PixelRect? region = null,
        bool copyToClipboard = false, bool includePreview = true, int maxDimension = 1600,
        CancellationToken cancellationToken = default) => OnDesktop(async () =>
    {
        CheckPreviewSize(maxDimension);
        var needsId = target is CaptureTarget.Window or CaptureTarget.Display;
        if (needsId != !string.IsNullOrWhiteSpace(targetId) || (target == CaptureTarget.Region) != region.HasValue)
            throw new McpException("Provide targetId for a window or display, or region for a region capture.");
        if (_selecting)
            throw new McpException("Finish or cancel the active interactive capture.");

        var store = CurrentStore();
        var snapshot = DesktopSnapshot.Take();
        var bounds = target switch
        {
            CaptureTarget.Window => snapshot.Windows.FirstOrDefault(window =>
                string.Equals(window.Id, targetId, StringComparison.OrdinalIgnoreCase))?.Bounds
                ?? throw new McpException("The window is unavailable. Refresh the capture targets."),
            CaptureTarget.Display => snapshot.Frames.FirstOrDefault(frame =>
                string.Equals(frame.DisplayId, targetId, StringComparison.OrdinalIgnoreCase))?.Bounds
                ?? throw new McpException("The display is unavailable. Refresh the capture targets."),
            CaptureTarget.Region => region,
            CaptureTarget.Desktop => snapshot.Bounds,
            CaptureTarget.Interactive => await SelectArea(snapshot, cancellationToken),
            _ => throw new McpException("Choose a valid capture target.")
        };
        if (bounds is null)
            return McpHost.Result(new { Cancelled = true });

        var area = ClipDesktop(bounds.Value, snapshot);
        var image = snapshot.Crop(area);
        cancellationToken.ThrowIfCancellationRequested();
        var capture = store.SaveCapture(stream => Images.SavePng(image, stream));
        var document = CaptureStore.ReadEdits(capture);
        var copied = copyToClipboard && await Images.CopyAsync(image);
        return DescribeImage(capture, image, document, includePreview, maxDimension,
            captureBounds: area, clipboardCopied: copyToClipboard ? copied : null);
    }, cancellationToken);

    [McpServerTool(Name = "list_screenshots", ReadOnly = true)]
    [Description("List saved screenshot IDs, newest first. Use nextCursor as before to continue.")]
    public Task<CallToolResult> ListScreenshots(int limit = 20,
        [Description("Optional nextCursor from a previous page.")] string? before = null,
        CancellationToken cancellationToken = default) => OnDesktop(() =>
    {
        if (limit is < 1 or > 100)
            throw new McpException("Choose a page size from 1 to 100.");
        if (before is not null && !CaptureStore.IsCaptureId(before))
            throw new McpException("Use a screenshot ID as the page cursor.");

        var captures = CurrentStore().Recent(limit + 1, before);
        var items = captures.Take(limit).Select(capture => new
        {
            capture.Id,
            CapturedAt = File.GetCreationTimeUtc(capture.OriginalPath)
        }).ToArray();
        return McpHost.Result(new { Screenshots = items, NextCursor = captures.Count > limit ? items[^1].Id : null });
    }, cancellationToken);

    [McpServerTool(Name = "get_screenshot", ReadOnly = true)]
    [Description("Inspect screenshot dimensions, crop, annotations and edit revision. Optionally return the edited or original image as an MCP image.")]
    public Task<CallToolResult> GetScreenshot(string id, bool includePreview = true, bool original = false,
        [Description("Maximum width or height of the preview in pixels, from 1 to 4096.")] int maxDimension = 1600,
        CancellationToken cancellationToken = default) => OnDesktop(() =>
    {
        CheckPreviewSize(maxDimension);
        var capture = Find(id);
        return DescribeImage(capture, Images.Read(capture.OriginalPath), CaptureStore.ReadEdits(capture),
            includePreview, maxDimension, original);
    }, cancellationToken);

    [McpServerTool(Name = "edit_screenshot", Destructive = true)]
    [Description("Apply a batch of annotation and crop edits. Edits run in order using current image coordinates. Save false previews the batch without changing the gallery.")]
    public Task<CallToolResult> EditScreenshot(string id,
        [Description("Revision from get_screenshot or the last saved edit.")] string revision,
        [Description("Ordered add, update, remove, crop, or reset actions. Reset restores the full original and removes all annotations.")] ScreenshotEdit[] operations,
        bool save = true, bool includePreview = true, int maxDimension = 1600,
        CancellationToken cancellationToken = default) => OnDesktop(() =>
    {
        CheckPreviewSize(maxDimension);
        var capture = Find(id);
        var document = CaptureStore.ReadEdits(capture);
        if (document.Revision != revision)
            throw new CaptureEditConflictException();

        var original = Images.Read(capture.OriginalPath);
        var edits = ScreenshotEdits.Apply(document.Edits, operations, original.PixelWidth, original.PixelHeight, _presets);
        cancellationToken.ThrowIfCancellationRequested();
        var nextRevision = save ? CaptureStore.SaveEdits(capture, edits, revision) : revision;
        return DescribeImage(capture, original, new SavedCaptureEdits(edits, nextRevision), includePreview, maxDimension,
            saved: save);
    }, cancellationToken);

    [McpServerTool(Name = "copy_screenshot", Destructive = true, Idempotent = true)]
    [Description("Put the edited screenshot or its original image on the current Windows clipboard.")]
    public Task<CallToolResult> CopyScreenshot(string id, bool original = false,
        CancellationToken cancellationToken = default) => OnDesktop(async () =>
    {
        if (!await Images.CopyAsync(Render(Find(id), original)))
            throw new McpException("The clipboard is busy. Try copying again.");
        return McpHost.Result(new { Id = id, Copied = true });
    }, cancellationToken);

    [McpServerTool(Name = "export_screenshot", Destructive = true)]
    [Description("Export a full-resolution edited or original screenshot as PNG or JPEG. Existing destination files require overwrite true.")]
    public Task<CallToolResult> ExportScreenshot(string id,
        [Description("Absolute Windows path ending in .png, .jpg, or .jpeg.")] string path,
        bool original = false, bool overwrite = false, CancellationToken cancellationToken = default) => OnDesktop(() =>
    {
        var capture = Find(id);
        if (!Path.IsPathFullyQualified(path))
            throw new McpException("Enter an absolute Windows file path.");
        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path);
        if (directory is null || (CaptureStore.IsCaptureId(Path.GetFileName(directory)) &&
                                  string.Equals(Path.GetFileName(path), "original.png", StringComparison.OrdinalIgnoreCase)))
            throw new McpException("Choose a different name for the exported image.");
        var encode = Images.EncoderFor(path) ?? throw new McpException("Use a .png, .jpg, or .jpeg file name.");
        if (!overwrite && File.Exists(path))
            throw new McpException("The exported file exists. Choose another path or set overwrite to true.");

        var image = Render(capture, original);
        Directory.CreateDirectory(directory);
        CaptureStore.WriteFile(path, stream => encode(image, stream), overwrite);
        return McpHost.Result(new { Id = id, Path = path, Width = image.PixelWidth, Height = image.PixelHeight });
    }, cancellationToken);

    [McpServerTool(Name = "list_presets", ReadOnly = true)]
    [Description("List the six current presets for one annotation tool or for every annotation tool.")]
    public Task<CallToolResult> ListPresets(AnnotationKind? kind = null,
        CancellationToken cancellationToken = default) => OnDesktop(() =>
    {
        var kinds = kind is { } selected ? [selected] : Enum.GetValues<AnnotationKind>();
        var presets = kinds.SelectMany(tool => Enumerable.Range(0, AnnotationPresetStore.SlotCount)
            .Select(index => DescribePreset(tool, index, _presets.Get(tool, index)))).ToArray();
        return McpHost.Result(new { Presets = presets });
    }, cancellationToken);

    [McpServerTool(Name = "save_preset", Destructive = true, Idempotent = true)]
    [Description("Apply style changes to an existing preset and save it for both the editor and MCP.")]
    public Task<CallToolResult> SavePreset(AnnotationKind kind, int preset, StylePatch style,
        CancellationToken cancellationToken = default) => OnDesktop(() =>
    {
        var index = ScreenshotEdits.PresetIndex(preset);
        var updated = style.Apply(_presets.Get(kind, index));
        _presets.Save(kind, index, updated);
        return McpHost.Result(DescribePreset(kind, index, updated));
    }, cancellationToken);

    [McpServerTool(Name = "get_workspace", ReadOnly = true)]
    [Description("Read the screenshot folder shared by the MCP server and the tray gallery.")]
    public Task<CallToolResult> GetWorkspace(CancellationToken cancellationToken) => OnDesktop(() =>
        McpHost.Result(new { UserSettings.CaptureFolder, UserSettings.DefaultCaptureFolder }), cancellationToken);

    [McpServerTool(Name = "set_capture_folder", Destructive = true, Idempotent = true)]
    [Description("Set the screenshot folder shared with the Windows app. Use the default folder from get_workspace to reset it.")]
    public Task<CallToolResult> SetCaptureFolder(string path, CancellationToken cancellationToken) => OnDesktop(() =>
    {
        var folder = UserSettings.PrepareCaptureFolder(path);
        if (!UserSettings.SavePreferences(UserSettings.LoadPreferences() with { ScreenshotFolder = folder }))
            throw new McpException("Could not save the screenshot folder.");
        return McpHost.Result(new { CaptureFolder = folder });
    }, cancellationToken);

    [McpServerTool(Name = "open_editor", Destructive = false, Idempotent = true)]
    [Description("Open the saved screenshot in the SnipNation editor for manual review in the current Windows session.")]
    public Task<CallToolResult> OpenEditor(string id, CancellationToken cancellationToken) => OnDesktop(() =>
    {
        var capture = Find(id);
        if (!_editors.TryGetValue(capture, out var editor))
        {
            editor = new EditorWindow(capture, Images.Read(capture.OriginalPath), CaptureStore.ReadEdits(capture), _presets);
            _editors.Add(capture, editor);
            editor.Closed += (_, _) => _editors.Remove(capture);
            editor.Show();
        }
        if (editor.WindowState == WindowState.Minimized)
            editor.WindowState = WindowState.Normal;
        editor.Activate();
        return McpHost.Result(new { Id = id, Opened = true });
    }, cancellationToken);

    private async Task<PixelRect?> SelectArea(DesktopSnapshot snapshot, CancellationToken cancellationToken)
    {
        _selecting = true;
        var foreground = NativeMethods.GetForegroundWindow();
        try
        {
            using var session = new CaptureSession(snapshot, dispatcher);
            return await session.Show().WaitAsync(cancellationToken);
        }
        finally
        {
            _selecting = false;
            if (foreground != 0 && NativeMethods.IsWindowVisible(foreground))
                NativeMethods.SetForegroundWindow(foreground);
        }
    }

    private static PixelRect ClipDesktop(PixelRect bounds, DesktopSnapshot snapshot)
    {
        var desktop = snapshot.Bounds;
        var left = Math.Max(bounds.X, desktop.X);
        var top = Math.Max(bounds.Y, desktop.Y);
        var right = Math.Min((long)bounds.X + bounds.Width, desktop.Right);
        var bottom = Math.Min((long)bounds.Y + bounds.Height, desktop.Bottom);
        if (right <= left || bottom <= top)
            throw new McpException("The capture area must overlap a display.");
        var clipped = PixelRect.FromEdges(left, top, (int)right, (int)bottom);
        if (!snapshot.Frames.Any(frame => !frame.Bounds.Intersect(clipped).IsEmpty))
            throw new McpException("The capture area must overlap a display.");
        return clipped;
    }

    private static CaptureStore CurrentStore() => new(UserSettings.CaptureFolder);

    private static CaptureFile Find(string id) => CurrentStore().Find(id)
        ?? throw new McpException("Screenshot not found in the current capture folder. Use list_screenshots to find its ID.");

    private static void CheckPreviewSize(int maxDimension)
    {
        if (maxDimension is < 1 or > 4096)
            throw new McpException("Choose a preview dimension from 1 to 4096 pixels.");
    }

    private static BitmapSource Render(CaptureFile capture, bool original)
    {
        var image = Images.Read(capture.OriginalPath);
        if (original)
            return image;
        var edits = CaptureStore.LoadEdits(capture);
        return Images.Compose(Images.Crop(image, edits.Crop), edits.Annotations);
    }

    private static CallToolResult DescribeImage(CaptureFile capture, BitmapSource image, SavedCaptureEdits document,
        bool includePreview, int maxDimension, bool original = false, PixelRect? captureBounds = null,
        bool? clipboardCopied = null, bool? saved = null)
    {
        var edits = document.Edits;
        var source = original ? image : Images.Crop(image, edits.Crop);
        var preview = includePreview ? Images.Preview(source, original ? [] : edits.Annotations, maxDimension) : null;
        return McpHost.Result(new
        {
            Screenshot = new
            {
                capture.Id, document.Revision, edits.Crop,
                OriginalWidth = image.PixelWidth, OriginalHeight = image.PixelHeight,
                Width = edits.Crop?.Width ?? image.PixelWidth, Height = edits.Crop?.Height ?? image.PixelHeight,
                edits.NextNumber,
                Annotations = edits.Annotations.Select((mark, index) => new
                {
                    Index = index, mark.Kind, mark.Points,
                    Preset = mark.PresetIndex + 1,
                    Text = mark.Kind == AnnotationKind.Text ? mark.Text : null,
                    Number = mark.Kind == AnnotationKind.Number ? (int?)mark.Number : null,
                    Style = StylePatch.Describe(mark.Style)
                }).ToArray()
            },
            Preview = preview is null ? null : new
            {
                Original = original, Width = preview.PixelWidth, Height = preview.PixelHeight,
                SourceWidth = source.PixelWidth, SourceHeight = source.PixelHeight
            },
            CaptureBounds = captureBounds, ClipboardCopied = clipboardCopied, Saved = saved
        }, preview);
    }

    private static object DescribePreset(AnnotationKind kind, int index, AnnotationStyle style) => new
    {
        Kind = kind, Preset = index + 1, Description = ToolPresets.Describe(kind, index, style),
        Style = StylePatch.Describe(style)
    };

    private Task<CallToolResult> OnDesktop(Func<CallToolResult> action, CancellationToken cancellationToken) =>
        OnDesktop(() => Task.FromResult(action()), cancellationToken);

    private async Task<CallToolResult> OnDesktop(Func<Task<CallToolResult>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task.Unwrap();
        }
        catch (Exception error) when (Images.IsFileError(error) ||
                                      error is ArgumentException or Win32Exception or InvalidOperationException or SecurityException)
        {
            throw new McpException(error.Message);
        }
    }
}
