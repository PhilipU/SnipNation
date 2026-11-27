using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using SnipNation.Branding;
using SnipNation.Core;
using SnipNation.Editor;
using SnipNation.Gallery;
using SnipNation.Imaging;
using SnipNation.Windows;
using Forms = System.Windows.Forms;

namespace SnipNation;

public partial class App : Application
{
    private readonly CaptureStore _store = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnipNationData", "Captures"));
    private readonly Dictionary<CaptureFile, EditorWindow> _editors = [];
    private readonly HashSet<CaptureFile> _opening = [];
    private readonly CancellationTokenSource _stopping = new();
    private readonly UpdateService _updates = new();
    private GalleryWindow _gallery = null!;
    private GlobalHotkey _hotkey = null!;
    private Forms.NotifyIcon _tray = null!;
    private Forms.ToolStripMenuItem _printItem = null!;
    private Forms.ToolStripMenuItem _alternateItem = null!;
    private Stream? _iconStream;
    private Task? _captureTask;
    private CaptureSession? _session;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _gallery = new GalleryWindow();
        _gallery.CaptureRequested += StartCapture;
        _gallery.EditRequested += capture => _ = EditAsync(capture);
        _gallery.CopyRequested += capture => _ = CopyAsync(capture);

        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += StartCapture;
        CreateTray();
        RegisterPreferredShortcut();
        _ = LoadHistoryAsync();
        if (_updates.IsInstalled)
        {
            var token = _stopping.Token;
            _ = Task.Run(() => _updates.RunAsync(token), token);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _stopping.Cancel();
        _session?.Cancel();
        _hotkey?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Icon?.Dispose();
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
        }
        _iconStream?.Dispose();
        _stopping.Dispose();
        base.OnExit(e);
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip { Renderer = new TrayRenderer() };
        var shortcuts = new Forms.ToolStripMenuItem("Keyboard shortcuts");
        _printItem = new Forms.ToolStripMenuItem(ShortcutLabel(CaptureShortcut.Print), null,
            (_, _) => ChooseShortcut(CaptureShortcut.Print));
        _alternateItem = new Forms.ToolStripMenuItem(ShortcutLabel(CaptureShortcut.Alternate), null,
            (_, _) => ChooseShortcut(CaptureShortcut.Alternate));
        shortcuts.DropDownItems.AddRange([
            _printItem, _alternateItem, new Forms.ToolStripSeparator(),
            new Forms.ToolStripMenuItem("Windows keyboard settings", null, (_, _) => OpenKeyboardSettings())
        ]);
        shortcuts.DropDown.Renderer = menu.Renderer;
        menu.Items.Add(shortcuts);

        var startup = new Forms.ToolStripMenuItem("Start with Windows")
        {
            Enabled = _updates.IsInstalled,
            Checked = _updates.IsInstalled && UserSettings.StartsAtLogon()
        };
        startup.Click += (_, _) =>
        {
            var enabled = !startup.Checked;
            if (UserSettings.SetLogonStartup(enabled))
                startup.Checked = enabled;
            else
                ShowTrayStatus("Could not change the startup setting.");
        };
        menu.Items.Add(startup);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());

        _iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/SnipNation.ico")).Stream;
        _tray = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(_iconStream),
            Text = "SnipNation",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button != Forms.MouseButtons.Left || _exiting || _captureTask is { IsCompleted: false })
                return;
            if (_gallery.IsActive)
                _gallery.Hide();
            else
                _gallery.Present(activate: true, NativeMethods.GetForegroundWindow());
        };
    }

    private void RegisterPreferredShortcut()
    {
        var preferred = UserSettings.PreferredShortcut();
        if (!_hotkey.TryRegister(preferred))
        {
            var fallback = preferred == CaptureShortcut.Print ? CaptureShortcut.Alternate : CaptureShortcut.Print;
            if (_hotkey.TryRegister(fallback))
                ShowTrayStatus($"{ShortcutLabel(preferred)} is already in use. {ShortcutLabel(fallback)} is active.");
            else
                ShowTrayStatus("Keyboard shortcuts are in use. Start a capture from the gallery.");
        }
        UpdateShortcutMenu();
    }

    private void ChooseShortcut(CaptureShortcut shortcut)
    {
        if (!_hotkey.TryRegister(shortcut))
            ShowTrayStatus($"{ShortcutLabel(shortcut)} is already in use.");
        else if (!UserSettings.SaveShortcut(shortcut))
            ShowTrayStatus("Could not save the keyboard shortcut.");
        UpdateShortcutMenu();
    }

    private void UpdateShortcutMenu()
    {
        _printItem.Checked = _hotkey.Registered == CaptureShortcut.Print;
        _alternateItem.Checked = _hotkey.Registered == CaptureShortcut.Alternate;
    }

    private static string ShortcutLabel(CaptureShortcut shortcut) =>
        shortcut == CaptureShortcut.Print ? "Print Screen" : "Ctrl+Shift+Print Screen";

    private void OpenKeyboardSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:easeofaccess-keyboard") { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            ShowTrayStatus("Could not open Windows keyboard settings.");
        }
    }

    private void ShowTrayStatus(string message) =>
        _tray.ShowBalloonTip(4500, "SnipNation", message, Forms.ToolTipIcon.Info);

    private void StartCapture()
    {
        if (!_exiting && _captureTask is not { IsCompleted: false })
            _captureTask = CaptureAsync();
    }

    private async Task CaptureAsync()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == _gallery.Handle)
            foreground = _gallery.PreviousForeground;
        _gallery.Hide();

        try
        {
            var snapshot = DesktopSnapshot.Take();
            PixelRect? selection;
            using (var session = new CaptureSession(snapshot, Dispatcher))
            {
                _session = session;
                selection = await session.Show();
            }
            _session = null;
            if (selection is not { } bounds)
            {
                RestoreFocus(foreground);
                return;
            }

            var image = snapshot.Crop(bounds);
            var copy = Images.CopyAsync(image);
            RestoreFocus(foreground);

            string? status = null;
            try
            {
                var capture = await Task.Run(() => _store.SaveCapture(stream => Images.SavePng(image, stream)));
                _gallery.AddOrUpdate(capture, await Task.Run(() => ReadPreview(capture)));
            }
            catch (Exception error) when (Images.IsFileError(error))
            {
                status = "Could not save the screenshot.";
                Trace.TraceWarning("Save screenshot: {0}", error.Message);
            }

            if (!await copy)
                status = status is null ? "Clipboard is busy. Try copying from the gallery." :
                    "Could not save or copy the screenshot.";
            ShowCaptureResult(foreground, status);
        }
        catch (Exception error) when (Images.IsFileError(error) ||
                                       error is Win32Exception or InvalidOperationException or OverflowException or OutOfMemoryException)
        {
            RestoreFocus(foreground);
            ShowCaptureResult(foreground, "Could not capture the screen.");
            Trace.TraceWarning("Capture: {0}", error.Message);
        }
        finally
        {
            _session = null;
        }
    }

    private void ShowCaptureResult(nint foreground, string? status)
    {
        if (!_exiting)
        {
            _gallery.ShowStatus(status);
            _gallery.Present(activate: false, foreground);
            if (NativeMethods.GetForegroundWindow() == _gallery.Handle)
                RestoreFocus(foreground);
        }
    }

    private static void RestoreFocus(nint foreground)
    {
        if (foreground != 0 && NativeMethods.IsWindowVisible(foreground))
            NativeMethods.SetForegroundWindow(foreground);
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var captures = await Task.Run(() => _store.Recent(GalleryWindow.HistoryLimit));
            foreach (var capture in captures)
            {
                var preview = await Task.Run(() => ReadPreview(capture));
                if (_exiting)
                    return;
                _gallery.AddOrUpdate(capture, preview, replace: false);
            }
        }
        catch (Exception error) when (Images.IsFileError(error))
        {
            _gallery.ShowStatus("Could not load the screenshots.");
            Trace.TraceWarning("Gallery: {0}", error.Message);
        }
    }

    private static ImageSource? ReadPreview(CaptureFile capture)
    {
        try
        {
            return Images.ReadPreview(capture);
        }
        catch (Exception error) when (Images.IsFileError(error))
        {
            Trace.TraceWarning("Thumbnail: {0}", error.Message);
            return null;
        }
    }

    private async Task EditAsync(CaptureFile capture)
    {
        if (_editors.TryGetValue(capture, out var existing))
        {
            _gallery.Hide();
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        if (!_opening.Add(capture))
            return;

        try
        {
            var document = await Task.Run(() => (Image: Images.Read(capture.OriginalPath), Edits: CaptureStore.LoadEdits(capture)));
            if (_exiting)
                return;

            var editor = new EditorWindow(capture, document.Image, document.Edits);
            editor.Saved += () => _gallery.AddOrUpdate(capture, ReadPreview(capture));
            editor.Closed += (_, _) => _editors.Remove(capture);
            _editors.Add(capture, editor);
            _gallery.Hide();
            editor.Show();
            editor.Activate();
        }
        catch (Exception error) when (Images.IsFileError(error))
        {
            _gallery.ShowStatus("Could not open the screenshot.");
            Trace.TraceWarning("Editor: {0}", error.Message);
        }
        finally
        {
            _opening.Remove(capture);
        }
    }

    private async Task CopyAsync(CaptureFile capture)
    {
        try
        {
            var document = await Task.Run(() =>
            {
                var edits = CaptureStore.LoadEdits(capture);
                var image = Images.Crop(Images.Read(capture.OriginalPath), edits.Crop);
                return (Image: image, edits.Annotations);
            });
            if (_exiting || _captureTask is { IsCompleted: false })
                return;
            var copied = await Images.CopyAsync(Images.Compose(document.Image, document.Annotations));
            _gallery.ShowStatus(copied ? null : "Clipboard is busy.");
        }
        catch (Exception error) when (Images.IsFileError(error))
        {
            _gallery.ShowStatus("Could not copy the screenshot.");
            Trace.TraceWarning("Copy screenshot: {0}", error.Message);
        }
    }

    private async Task ExitAsync()
    {
        if (_exiting)
            return;

        _exiting = true;
        _session?.Cancel();
        if (_captureTask is not null)
            await _captureTask;
        foreach (var editor in _editors.Values.ToArray())
        {
            editor.Close();
            if (editor.IsVisible)
            {
                _exiting = false;
                return;
            }
        }
        _gallery.CloseForExit();
        Shutdown();
    }
}
