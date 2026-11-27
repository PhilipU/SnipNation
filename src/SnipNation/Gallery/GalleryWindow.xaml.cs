using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SnipNation.Core;
using SnipNation.Windows;
using Screen = System.Windows.Forms.Screen;

namespace SnipNation.Gallery;

public partial class GalleryWindow : Window
{
    internal const int HistoryLimit = 36;
    private readonly ObservableCollection<GalleryItem> _items = [];
    private bool _closing;

    public GalleryWindow()
    {
        InitializeComponent();
        PreviewList.ItemsSource = _items;
    }

    internal event Action? CaptureRequested;
    internal event Action<CaptureFile>? EditRequested;
    internal event Action<CaptureFile>? CopyRequested;
    internal nint PreviousForeground { get; private set; }
    internal nint Handle => new WindowInteropHelper(this).Handle;

    internal void Present(bool activate, nint previousForeground)
    {
        if (previousForeground != Handle)
            PreviousForeground = previousForeground;
        ShowActivated = activate;

        if (!IsVisible)
            Show();
        PlaceAtTray();
        if (activate)
            Activate();
    }

    internal void CloseForExit()
    {
        _closing = true;
        Close();
    }

    internal void AddOrUpdate(CaptureFile capture, ImageSource? preview, bool replace = true)
    {
        var item = new GalleryItem(capture, preview);
        for (var index = 0; index < _items.Count; index++)
        {
            var order = StringComparer.Ordinal.Compare(capture.Id, _items[index].Capture.Id);
            if (order == 0)
            {
                if (replace)
                    _items[index] = item;
                return;
            }
            if (order > 0)
            {
                _items.Insert(index, item);
                if (_items.Count > HistoryLimit)
                    _items.RemoveAt(HistoryLimit);
                return;
            }
        }
        if (_items.Count < HistoryLimit)
            _items.Add(item);
    }

    internal void ShowStatus(string? status)
    {
        StatusText.Text = status;
        StatusText.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        PlaceAtTray();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private void PlaceAtTray()
    {
        if (Screen.PrimaryScreen is not { } primary)
            return;

        var handle = Handle;
        var dpi = NativeMethods.GetDpiForWindow(handle);
        var scale = (dpi == 0 ? 96 : dpi) / 96.0;
        var workArea = primary.WorkingArea;
        var margin = (int)Math.Round(12 * scale);
        var width = Math.Min((int)Math.Round(Width * scale), workArea.Width - 2 * margin);
        var height = Math.Min((int)Math.Round(Height * scale), workArea.Height - 2 * margin);
        if (width > 0 && height > 0)
        {
            NativeMethods.SetWindowPos(handle, 0, workArea.Right - width - margin, workArea.Bottom - height - margin,
                width, height, NativeMethods.NoActivateOrZOrder);
        }
    }

    private void ScreenshotClicked(object sender, RoutedEventArgs e) => CaptureRequested?.Invoke();

    private void CloseClicked(object sender, RoutedEventArgs e) => Hide();

    private void PreviewDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && ItemFromEvent(e) is { } item)
        {
            EditRequested?.Invoke(item.Capture);
            e.Handled = true;
        }
    }

    private void PreviewRightClick(object sender, MouseButtonEventArgs e) => PreviewList.SelectedItem = ItemFromEvent(e);

    private GalleryItem? ItemFromEvent(RoutedEventArgs e) =>
        e.OriginalSource is DependencyObject source &&
        ItemsControl.ContainerFromElement(PreviewList, source) is ListBoxItem container
            ? container.Content as GalleryItem
            : null;

    private void EditClicked(object sender, RoutedEventArgs e) => EditSelected();

    private void CopyClicked(object sender, RoutedEventArgs e) => CopySelected();

    private void EditSelected()
    {
        if (PreviewList.SelectedItem is GalleryItem item)
            EditRequested?.Invoke(item.Capture);
    }

    private void CopySelected()
    {
        if (PreviewList.SelectedItem is GalleryItem item)
            CopyRequested?.Invoke(item.Capture);
    }

    private void GalleryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && PreviewList.IsKeyboardFocusWithin)
        {
            EditSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopySelected();
            e.Handled = true;
        }
    }

    private sealed record GalleryItem(CaptureFile Capture, ImageSource? Preview)
    {
        public string Label => DateTimeOffset.TryParseExact(Capture.Id.Split('_')[0], "yyyyMMdd'T'HHmmssfff'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)
                ? $"Screenshot {timestamp.LocalDateTime.ToString("g", CultureInfo.GetCultureInfo("en-US"))}"
                : "Screenshot";
    }
}
