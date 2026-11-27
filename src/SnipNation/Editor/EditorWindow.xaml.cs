using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
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
    private readonly BitmapSource _image;
    private readonly EditorSurface _surface;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Stack<Annotation[]> _undo = new();
    private readonly Stack<Annotation[]> _redo = new();
    private readonly List<ImagePoint> _stroke = [];
    private Annotation[] _annotations;
    private Annotation[] _saved;
    private AnnotationKind? _tool;
    private int _selected = -1;
    private ImagePoint? _anchor;
    private Annotation? _preview;
    private bool _syncingStyle;
    private bool _fit = true;

    internal EditorWindow(CaptureFile capture, BitmapSource image, Annotation[] annotations)
    {
        _capture = capture;
        _image = image;
        _annotations = _saved = annotations;
        _surface = new EditorSurface(image) { LayoutTransform = _scale, Cursor = Cursors.Arrow };
        InitializeComponent();

        CanvasHost.Child = _surface;
        _surface.MouseLeftButtonDown += BeginGesture;
        _surface.MouseMove += MovePointer;
        _surface.MouseLeftButtonUp += EndGesture;
        _surface.LostMouseCapture += (_, _) => ClearGesture();
        ColorPicker.ItemsSource = Palette;
        Loaded += (_, _) => FitToViewport();
        SyncStyle();
        UpdateView();
    }

    internal event Action? Saved;

    private bool IsDirty => _annotations != _saved;

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
        if (ColorPicker is null || !IsLoaded)
            return;

        ClearGesture();
        _tool = Enum.TryParse<AnnotationKind>((string?)((RadioButton)sender).Tag, out var kind) ? kind : null;
        _selected = -1;
        _surface.Cursor = _tool switch
        {
            AnnotationKind.Text => Cursors.IBeam,
            null => Cursors.Arrow,
            _ => Cursors.Cross
        };
        SyncStyle();
        UpdateView();
    }

    private void BeginGesture(object sender, MouseButtonEventArgs e)
    {
        if (_anchor is not null)
            return;

        _surface.Focus();
        var point = ImagePosition(e);
        if (_tool == AnnotationKind.Text)
        {
            if (TextDialog.Edit(this, "") is { Length: > 0 } text)
                Add(new Annotation(AnnotationKind.Text, [point], CurrentColor(AnnotationKind.Text), CurrentSize(), text));
            e.Handled = true;
            return;
        }

        if (_tool is null)
        {
            var cursor = new Point(point.X, point.Y);
            _selected = Array.FindLastIndex(_annotations, mark => AnnotationGraphics.HitTest(mark, cursor, 5 / _scale.ScaleX));
            SyncStyle();
            UpdateView();
            if (_selected < 0)
                return;
            if (e.ClickCount == 2 && _annotations[_selected].Kind == AnnotationKind.Text)
            {
                var mark = _annotations[_selected];
                if (TextDialog.Edit(this, mark.Text) is { Length: > 0 } text && text != mark.Text)
                    ReplaceSelected(mark with { Text = text });
                e.Handled = true;
                return;
            }
        }

        if (_surface.CaptureMouse())
        {
            _anchor = point;
            _stroke.Clear();
            _stroke.Add(point);
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
        if (_tool is not { } kind)
        {
            _preview = moved ? _annotations[_selected].Offset(dx, dy) : null;
        }
        else if (kind == AnnotationKind.Freehand)
        {
            var last = _stroke[^1];
            if (Math.Abs(last.X - point.X) >= 0.5 || Math.Abs(last.Y - point.Y) >= 0.5)
                _stroke.Add(point);
            _preview = _stroke.Count >= 2 ? new Annotation(kind, _stroke.ToArray(), CurrentColor(kind), CurrentSize()) : null;
        }
        else
        {
            var valid = kind == AnnotationKind.Highlight ? Math.Abs(dx) >= 1 && Math.Abs(dy) >= 1 : moved;
            _preview = valid ? new Annotation(kind, [start, point], CurrentColor(kind), CurrentSize()) : null;
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
        return new ImagePoint(Math.Clamp(point.X, 0, _image.PixelWidth - 1),
            Math.Clamp(point.Y, 0, _image.PixelHeight - 1));
    }

    private uint CurrentColor(AnnotationKind kind)
    {
        if (ColorPicker.SelectedItem is not ColorOption && _selected >= 0)
            return _annotations[_selected].Argb;

        var color = ColorPicker.SelectedItem is ColorOption option ? option.Argb : DefaultColor(kind);
        return kind == AnnotationKind.Highlight ? (color & 0x00FFFFFF) | 0x66000000 : color;
    }

    private double CurrentSize() => SizePicker.SelectedItem is double size ? size : 4;

    private static uint DefaultColor(AnnotationKind? kind) =>
        Brand.Argb(kind == AnnotationKind.Text ? "Background" : "Accent");

    private void SyncStyle()
    {
        var selected = _selected >= 0 ? _annotations[_selected] : null;
        var kind = selected?.Kind ?? _tool;
        var color = selected is not null ? selected.Argb | 0xFF000000 : DefaultColor(kind);
        var size = selected?.Size ?? (kind == AnnotationKind.Text ? 24 : 4);
        var sizes = kind == AnnotationKind.Text ? TextSizes : StrokeSizes;

        _syncingStyle = true;
        ColorPicker.IsEnabled = kind is not null;
        ColorPicker.SelectedItem = Array.Find(Palette, option => option.Argb == color);
        SizePicker.IsEnabled = kind is not null and not AnnotationKind.Highlight;
        SizePicker.ItemsSource = sizes.Contains(size) ? sizes : sizes.Append(size).Order().ToArray();
        SizePicker.SelectedItem = size;
        _syncingStyle = false;
    }

    private void StyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingStyle || _selected < 0 || !IsLoaded)
            return;
        var mark = _annotations[_selected];
        var styled = mark with { Argb = CurrentColor(mark.Kind), Size = CurrentSize() };
        if (mark != styled)
            ReplaceSelected(styled);
    }

    private void Add(Annotation annotation) => Commit([.. _annotations, annotation]);

    private void ReplaceSelected(Annotation annotation)
    {
        var changed = (Annotation[])_annotations.Clone();
        changed[_selected] = annotation;
        Commit(changed);
    }

    private void DeleteSelection()
    {
        if (_selected < 0)
            return;
        var index = _selected;
        _selected = -1;
        Commit(_annotations.Where((_, position) => position != index).ToArray());
        SyncStyle();
    }

    private void Commit(Annotation[] next)
    {
        _undo.Push(_annotations);
        _annotations = next;
        _redo.Clear();
        UpdateView();
    }

    private void Restore(Stack<Annotation[]> source, Stack<Annotation[]> destination)
    {
        if (!source.TryPop(out var previous))
            return;

        ClearGesture();
        destination.Push(_annotations);
        _annotations = previous;
        _selected = -1;
        SyncStyle();
        UpdateView();
    }

    private void UpdateView()
    {
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
        SaveButton.IsEnabled = IsDirty;
        _surface.SetView(_annotations, _selected, _preview, _tool is null, _scale.ScaleX);
    }

    private bool Save()
    {
        if (!IsDirty)
            return true;

        try
        {
            CaptureStore.SaveAnnotations(_capture, _annotations);
            _saved = _annotations;
            SetStatus(null);
            UpdateView();
            Saved?.Invoke();
            return true;
        }
        catch (Exception error) when (Images.IsFileError(error))
        {
            SetStatus("Could not save the annotations.");
            Trace.TraceWarning("Save annotations: {0}", error.Message);
            return false;
        }
    }

    private async Task CopyAsync()
    {
        try
        {
            var copied = await Images.CopyAsync(Images.Compose(_image, _annotations));
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
            DefaultExt = ".png",
            Filter = "PNG image (*.png)|*.png",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            if (string.Equals(Path.GetFullPath(dialog.FileName), _capture.OriginalPath, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("Choose a different name for the exported image.");
                return;
            }
            var rendered = Images.Compose(_image, _annotations);
            CaptureStore.WriteFile(dialog.FileName, stream => Images.SavePng(rendered, stream));
            SetStatus(null);
        }
        catch (Exception error) when (Images.IsFileError(error))
        {
            SetStatus("Could not export the PNG.");
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
        else if (e.Key == Key.Delete && _anchor is null)
        {
            DeleteSelection();
        }
        else if (e.Key == Key.Escape)
        {
            ClearGesture();
            _selected = -1;
            SyncStyle();
            UpdateView();
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
    private void ExportClicked(object sender, RoutedEventArgs e) => Export();

    private sealed record ColorOption(string Name, uint Argb)
    {
        public Brush Brush { get; } = new SolidColorBrush(AnnotationGraphics.ToColor(Argb));
    }
}
