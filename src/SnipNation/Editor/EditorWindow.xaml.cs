using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SnipNation.Branding;
using SnipNation.Core;
using SnipNation.Imaging;

namespace SnipNation.Editor;

public partial class EditorWindow : Window
{
    private static readonly ColorOption[] Palette =
    [
        new("Lime green", Brand.Argb("Accent")),
        new("Dark green", Brand.Argb("LightAccent")),
        new("Charcoal", Brand.Argb("Background")),
        new("White", Brand.Argb("Foreground")),
        new("Light gray", Brand.Argb("Muted")),
        new("Dark gray", Brand.Argb("LightMuted")),
        new("Off-white", Brand.Argb("LightBackground"))
    ];
    private static readonly double[] StrokeSizes = [2, 4, 6, 10];
    private static readonly double[] TextSizes = [16, 24, 36, 52];

    private readonly CaptureFile _capture;
    private readonly BitmapSource _original;
    private readonly EditorSurface _surface;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Stack<CaptureEdits> _undo = new();
    private readonly Stack<CaptureEdits> _redo = new();
    private readonly List<ImagePoint> _stroke = [];
    private BitmapSource _image;
    private CaptureEdits _edits;
    private CaptureEdits _saved;
    private AnnotationKind? _tool;
    private int _selected = -1;
    private ImagePoint? _anchor;
    private Annotation? _preview;
    private PixelRect? _cropSelection;
    private uint _color;
    private double _size;
    private bool _fit = true;

    internal EditorWindow(CaptureFile capture, BitmapSource original, CaptureEdits edits)
    {
        _capture = capture;
        _original = original;
        _edits = _saved = edits;
        _image = Images.Crop(original, edits.Crop);
        _surface = new EditorSurface(_image) { LayoutTransform = _scale, Cursor = Cursors.Arrow };
        InitializeComponent();

        CanvasHost.Child = _surface;
        _surface.MouseLeftButtonDown += BeginGesture;
        _surface.MouseMove += MovePointer;
        _surface.MouseLeftButtonUp += EndGesture;
        _surface.LostMouseCapture += (_, _) =>
        {
            if (_anchor is not null && IsCropping)
                _cropSelection = null;
            ClearGesture();
        };
        Loaded += (_, _) => FitToViewport();
        SyncStyle();
        UpdateView();
    }

    internal event Action? Saved;

    private Annotation[] Annotations => _edits.Annotations;
    private bool IsDirty => _edits != _saved;
    private bool IsCropping => CropButton.IsChecked == true;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Brand.ApplyCaption(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (IsDirty)
        {
            var answer = MessageBox.Show(this, "Save changes?", "SnipNation",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            e.Cancel = answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !Save());
        }
        base.OnClosing(e);
    }

    private void ToolChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        ClearGesture();
        _tool = Enum.TryParse<AnnotationKind>((string?)((RadioButton)sender).Tag, out var kind) ? kind : null;
        _selected = -1;
        UpdateCursor();
        SyncStyle();
        UpdateView();
    }

    private void UpdateCursor()
    {
        _surface.Cursor = IsCropping ? Cursors.Cross : _tool switch
        {
            AnnotationKind.Text => Cursors.IBeam,
            null => Cursors.Arrow,
            _ => Cursors.Cross
        };
    }

    private void BeginGesture(object sender, MouseButtonEventArgs e)
    {
        if (_anchor is not null)
            return;

        _surface.Focus();
        var point = ImagePosition(e);
        if (!IsCropping && _tool == AnnotationKind.Text)
        {
            if (TextDialog.Edit(this, "") is { Length: > 0 } text)
                Add(new Annotation(AnnotationKind.Text, [point], _color, _size, text));
            e.Handled = true;
            return;
        }

        if (!IsCropping && _tool is null)
        {
            var cursor = new Point(point.X, point.Y);
            _selected = Array.FindLastIndex(Annotations, mark => AnnotationGraphics.HitTest(mark, cursor, 5 / _scale.ScaleX));
            SyncStyle();
            UpdateView();
            if (_selected < 0)
                return;
            if (e.ClickCount == 2 && Annotations[_selected].Kind == AnnotationKind.Text)
            {
                var mark = Annotations[_selected];
                if (TextDialog.Edit(this, mark.Text) is { Length: > 0 } text && text != mark.Text)
                    ReplaceSelected(mark with { Text = text });
                e.Handled = true;
                return;
            }
        }

        if (_surface.CaptureMouse())
        {
            _anchor = point;
            if (IsCropping)
            {
                _cropSelection = null;
                UpdateView();
            }
            else
            {
                _stroke.Add(point);
            }
        }
        e.Handled = true;
    }

    private void MovePointer(object sender, MouseEventArgs e) => UpdateGesture(ImagePosition(e));

    private void EndGesture(object sender, MouseButtonEventArgs e)
    {
        if (_anchor is null)
            return;

        UpdateGesture(ImagePosition(e));
        var preview = _preview;
        ClearGesture();
        if (preview is not null)
        {
            if (_tool is null)
                ReplaceSelected(preview);
            else
                Add(preview);
        }
        e.Handled = true;
    }

    private void UpdateGesture(ImagePoint point)
    {
        if (_anchor is not { } start)
            return;

        var dx = point.X - start.X;
        var dy = point.Y - start.Y;
        var moved = dx * dx + dy * dy >= 1;
        if (IsCropping)
        {
            _cropSelection = Math.Abs(dx) >= 1 && Math.Abs(dy) >= 1
                ? PixelRect.FromEdges(
                    (int)Math.Floor(Math.Min(start.X, point.X)), (int)Math.Floor(Math.Min(start.Y, point.Y)),
                    (int)Math.Ceiling(Math.Max(start.X, point.X)), (int)Math.Ceiling(Math.Max(start.Y, point.Y)))
                : null;
        }
        else if (_tool is not { } kind)
        {
            _preview = moved ? Annotations[_selected].Offset(dx, dy) : null;
        }
        else if (kind == AnnotationKind.Freehand)
        {
            var last = _stroke[^1];
            if (Math.Abs(last.X - point.X) >= 0.5 || Math.Abs(last.Y - point.Y) >= 0.5)
                _stroke.Add(point);
            _preview = _stroke.Count >= 2 ? new Annotation(kind, _stroke.ToArray(), _color, _size) : null;
        }
        else
        {
            var valid = kind == AnnotationKind.Highlight ? Math.Abs(dx) >= 1 && Math.Abs(dy) >= 1 : moved;
            _preview = valid ? new Annotation(kind, [start, point], _color, _size) : null;
        }
        UpdateView();
    }

    private void ClearGesture()
    {
        if (_anchor is null)
            return;
        _anchor = null;
        _preview = null;
        _stroke.Clear();
        _surface.ReleaseMouseCapture();
        UpdateView();
    }

    private ImagePoint ImagePosition(MouseEventArgs e)
    {
        var point = e.GetPosition(_surface);
        var inset = IsCropping ? 0 : 1;
        return new ImagePoint(Math.Clamp(point.X, 0, _image.PixelWidth - inset),
            Math.Clamp(point.Y, 0, _image.PixelHeight - inset));
    }

    private void CropModeChanged(object sender, RoutedEventArgs e)
    {
        ClearGesture();
        CropActions.Visibility = IsCropping ? Visibility.Visible : Visibility.Collapsed;
        ToolRail.IsEnabled = !IsCropping;
        _cropSelection = null;
        if (_selected >= 0)
        {
            _selected = -1;
            SyncStyle();
        }
        UpdateCursor();
        UpdateView();
    }

    private void ApplyCrop()
    {
        if (_cropSelection is not { } crop || _anchor is not null)
            return;

        ExitCropMode();
        if (crop != new PixelRect(0, 0, _image.PixelWidth, _image.PixelHeight))
            Commit(_edits.CropTo(crop));
    }

    private AnnotationKind? StyledKind => _selected >= 0 ? Annotations[_selected].Kind : _tool;

    private static uint WithOpacity(uint color, AnnotationKind? kind) =>
        kind == AnnotationKind.Highlight ? (color & 0x00FFFFFF) | 0x66000000 : color;

    private void SyncStyle()
    {
        var selected = _selected >= 0 ? Annotations[_selected] : null;
        var kind = StyledKind;
        var defaultColor = Brand.Argb(kind == AnnotationKind.Text ? "Background" : "Accent");
        _color = selected?.Argb ?? WithOpacity(defaultColor, kind);
        _size = selected?.Size ?? (kind == AnnotationKind.Text ? 24 : 4);
        ColorButton.IsEnabled = kind is not null;
        SizeButton.IsEnabled = kind is not null and not AnnotationKind.Highlight;
    }

    private void ColorClicked(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ColorButton, Placement = PlacementMode.Right };
        foreach (var option in Palette)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new Border
            {
                Width = 14, Height = 14, Background = option.Brush,
                BorderBrush = Brand.BrushFor("DividerBrush"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 8, 0)
            });
            header.Children.Add(new TextBlock { Text = option.Name });
            var color = WithOpacity(option.Argb, StyledKind);
            AddOption(menu, header, option.Name, color == _color, () =>
            {
                _color = color;
                ApplyStyle();
            });
        }
        menu.IsOpen = true;
    }

    private void SizeClicked(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SizeButton, Placement = PlacementMode.Right };
        var sizes = StyledKind == AnnotationKind.Text ? TextSizes : StrokeSizes;
        foreach (var size in sizes.Contains(_size) ? sizes : sizes.Append(_size).Order().ToArray())
        {
            var label = FormattableString.Invariant($"{size} px");
            AddOption(menu, label, label, size == _size, () =>
            {
                _size = size;
                ApplyStyle();
            });
        }
        menu.IsOpen = true;
    }

    private static void AddOption(ContextMenu menu, object header, string name, bool selected, Action choose)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = selected };
        AutomationProperties.SetName(item, name);
        if (selected)
            item.Foreground = Brand.BrushFor("AccentBrush");
        item.Click += (_, _) => choose();
        menu.Items.Add(item);
    }

    private void ApplyStyle()
    {
        if (_selected < 0)
            return;
        var mark = Annotations[_selected];
        var styled = mark with { Argb = _color, Size = _size };
        if (mark != styled)
            ReplaceSelected(styled);
    }

    private void Add(Annotation annotation) => Commit(_edits with { Annotations = [.. Annotations, annotation] });

    private void ReplaceSelected(Annotation annotation)
    {
        var changed = (Annotation[])Annotations.Clone();
        changed[_selected] = annotation;
        Commit(_edits with { Annotations = changed });
    }

    private void DeleteSelection()
    {
        if (_selected < 0)
            return;
        var index = _selected;
        _selected = -1;
        Commit(_edits with { Annotations = Annotations.Where((_, position) => position != index).ToArray() });
        SyncStyle();
    }

    private void Commit(CaptureEdits next)
    {
        _undo.Push(_edits);
        _redo.Clear();
        SetEdits(next);
    }

    private void Restore(Stack<CaptureEdits> source, Stack<CaptureEdits> destination)
    {
        if (!source.TryPop(out var previous))
            return;

        ClearGesture();
        CropButton.IsChecked = false;
        destination.Push(_edits);
        _selected = -1;
        SetEdits(previous);
        SyncStyle();
    }

    private void SetEdits(CaptureEdits edits)
    {
        var imageChanged = edits.Crop != _edits.Crop;
        _edits = edits;
        if (imageChanged)
        {
            _image = Images.Crop(_original, edits.Crop);
            _surface.SetImage(_image);
            FitToViewport();
        }
        UpdateView();
    }

    private void UpdateView()
    {
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
        SaveButton.IsEnabled = IsDirty;
        ApplyCropButton.IsEnabled = _cropSelection is not null && _anchor is null;
        _surface.SetView(Annotations, _selected, _preview, _tool is null, _scale.ScaleX, _cropSelection);
    }

    private bool Save()
    {
        if (!IsDirty)
            return true;

        try
        {
            CaptureStore.SaveEdits(_capture, _edits);
            _saved = _edits;
            SetStatus(null);
            UpdateView();
            Saved?.Invoke();
            return true;
        }
        catch (Exception error) when (Images.IsFileError(error))
        {
            SetStatus("Could not save the edits.");
            Trace.TraceWarning("Save edits: {0}", error.Message);
            return false;
        }
    }

    private async Task CopyAsync()
    {
        try
        {
            var copied = await Images.CopyAsync(Images.Compose(_image, Annotations));
            SetStatus(copied ? null : "Clipboard is busy.");
        }
        catch (Exception error) when (Images.IsFileError(error))
        {
            SetStatus("Could not copy the image.");
            Trace.TraceWarning("Copy edited image: {0}", error.Message);
        }
    }

    private void Export()
    {
        var dialog = new SaveFileDialog
        {
            FileName = "Screenshot",
            Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg;*.jpeg)|*.jpg;*.jpeg",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var path = Path.GetFullPath(dialog.FileName);
            if (string.Equals(path, _capture.OriginalPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, _capture.AnnotationPath, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("Choose a different name for the exported image.");
                return;
            }
            Action<BitmapSource, Stream>? encode = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => Images.SavePng,
                ".jpg" or ".jpeg" => Images.SaveJpeg,
                _ => null
            };
            if (encode is null)
            {
                SetStatus("Use a .png, .jpg, or .jpeg file name.");
                return;
            }

            var rendered = Images.Compose(_image, Annotations);
            CaptureStore.WriteFile(path, stream => encode(rendered, stream));
            SetStatus(null);
        }
        catch (Exception error) when (Images.IsFileError(error))
        {
            SetStatus("Could not export the image.");
            Trace.TraceWarning("Export: {0}", error.Message);
        }
    }

    private void SetStatus(string? message)
    {
        StatusText.Text = message;
        StatusText.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void EditorKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.S: Save(); break;
                case Key.C: _ = CopyAsync(); break;
                case Key.Z: Restore(_undo, _redo); break;
                case Key.Y: Restore(_redo, _undo); break;
                default: return;
            }
        }
        else if (e.Key == Key.Enter && IsCropping && _cropSelection is not null && _anchor is null)
        {
            ApplyCrop();
        }
        else if (e.Key == Key.Delete && _anchor is null)
        {
            DeleteSelection();
        }
        else if (e.Key == Key.Escape)
        {
            if (IsCropping)
                ExitCropMode();
            else
            {
                ClearGesture();
                _selected = -1;
                SyncStyle();
                UpdateView();
            }
        }
        else
        {
            return;
        }
        e.Handled = true;
    }

    private void FitToViewport()
    {
        if (_fit && Viewport.ViewportWidth > 32 && Viewport.ViewportHeight > 32)
        {
            var widthRatio = (Viewport.ViewportWidth - 32) / _image.PixelWidth;
            var heightRatio = (Viewport.ViewportHeight - 32) / _image.PixelHeight;
            SetZoom(Math.Min(1, Math.Min(widthRatio, heightRatio)));
        }
    }

    private void SetZoom(double level)
    {
        var scale = Math.Clamp(level, 0.02, 8);
        if (Math.Abs(scale - _scale.ScaleX) < 0.0001)
            return;
        _scale.ScaleX = _scale.ScaleY = scale;
        UpdateView();
    }

    private void ZoomBy(double factor)
    {
        _fit = false;
        SetZoom(_scale.ScaleX * factor);
    }

    private void ViewportChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0)
            FitToViewport();
    }

    private void FitClicked(object sender, RoutedEventArgs e)
    {
        _fit = true;
        FitToViewport();
    }

    private void ZoomOutClicked(object sender, RoutedEventArgs e) => ZoomBy(0.8);
    private void ZoomInClicked(object sender, RoutedEventArgs e) => ZoomBy(1.25);
    private void UndoClicked(object sender, RoutedEventArgs e) => Restore(_undo, _redo);
    private void RedoClicked(object sender, RoutedEventArgs e) => Restore(_redo, _undo);
    private void SaveClicked(object sender, RoutedEventArgs e) => Save();
    private async void CopyClicked(object sender, RoutedEventArgs e) => await CopyAsync();
    private void ApplyCropClicked(object sender, RoutedEventArgs e) => ApplyCrop();
    private void CancelCropClicked(object sender, RoutedEventArgs e) => ExitCropMode();
    private void ExportClicked(object sender, RoutedEventArgs e) => Export();

    private void ExitCropMode()
    {
        CropButton.IsChecked = false;
        CropButton.Focus();
    }

    private sealed record ColorOption(string Name, uint Argb)
    {
        public Brush Brush { get; } = new SolidColorBrush(AnnotationGraphics.ToColor(Argb));
    }
}
