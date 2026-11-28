using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Windows;
using Microsoft.Win32;
using SnipNation.Branding;

namespace SnipNation.Windows;

public partial class SettingsWindow : Window
{
    private readonly string _defaultFolder;
    private bool _saving;

    internal SettingsWindow(string screenshotFolder, string defaultFolder, UpdateChannel channel)
    {
        _defaultFolder = defaultFolder;
        ScreenshotFolder = screenshotFolder;
        InitializeComponent();
        FolderBox.Text = screenshotFolder;
        FolderBox.TextChanged += (_, _) => SetStatus(null);
        ChannelBox.ItemsSource = Enum.GetValues<UpdateChannel>();
        ChannelBox.SelectedItem = channel;
    }

    internal string ScreenshotFolder { get; private set; }
    internal UpdateChannel SelectedChannel => (UpdateChannel)ChannelBox.SelectedItem;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Brand.ApplyCaption(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_saving)
            e.Cancel = true;
        base.OnClosing(e);
    }

    private void BrowseClicked(object sender, RoutedEventArgs e)
    {
        var folder = FolderBox.Text.Trim();
        var dialog = new OpenFolderDialog { Title = "Choose a screenshot folder" };
        if (Directory.Exists(folder))
            dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(this) == true)
            FolderBox.Text = dialog.FolderName;
    }

    private void UseDefaultClicked(object sender, RoutedEventArgs e) => FolderBox.Text = _defaultFolder;

    private async void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (_saving)
            return;

        var path = FolderBox.Text.Trim();
        if (path == ScreenshotFolder)
        {
            DialogResult = true;
            return;
        }
        if (!Path.IsPathFullyQualified(path))
        {
            SetStatus("Enter a full folder path.");
            FolderBox.Focus();
            return;
        }

        _saving = true;
        IsEnabled = false;
        SetStatus(null);
        try
        {
            ScreenshotFolder = await Task.Run(() => PrepareFolder(path));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
                                     NotSupportedException or SecurityException)
        {
            SetStatus("Could not write to the screenshot folder.");
            Trace.TraceWarning("Screenshot folder: {0}", error.Message);
            return;
        }
        finally
        {
            _saving = false;
            IsEnabled = true;
        }
        DialogResult = true;
    }

    private static string PrepareFolder(string path)
    {
        var folder = Path.TrimEndingDirectorySeparator(Directory.CreateDirectory(path).FullName);
        var probePath = Path.Combine(folder, Path.GetRandomFileName());
        using var probe = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 1, FileOptions.DeleteOnClose);
        return folder;
    }

    private void SetStatus(string? message)
    {
        StatusText.Text = message;
        StatusText.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
