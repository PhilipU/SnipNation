using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using SnipNation.Branding;
using SnipNation.Core;
using SnipNation.Imaging;

namespace SnipNation.Editor;

public partial class EditorWindow
{
    private readonly ToolPresets _presets;
    private RadioButton? _presetAnchor;
    private int _activePreset;

    private void ToolClicked(object sender, RoutedEventArgs e)
    {
        if (IsCropping || _anchor is not null || _tool is not { } kind)
            return;

        _presetAnchor = (RadioButton)sender;
        PresetButtons.Children.Clear();
        for (var index = 0; index < AnnotationPresetStore.SlotCount; index++)
        {
            var style = _presets.Get(kind, index);
            var label = ToolPresets.Describe(kind, index, style);
            var button = new Button
            {
                Style = (Style)FindResource("BrandButton"),
                Width = 100,
                Height = 64,
                Margin = new Thickness(3),
                Padding = new Thickness(4),
                Content = new Image { Source = AnnotationPreview.Render(kind, style) },
                ToolTip = label,
                Tag = index
            };
            if (index == _activePreset)
                button.BorderBrush = Brand.BrushFor("AccentBrush");
            AutomationProperties.SetName(button, label);
            button.Click += PresetClicked;
            PresetButtons.Children.Add(button);
        }

        PresetPanel.Visibility = Visibility.Visible;
        PositionPresets();
        ((Button)PresetButtons.Children[_activePreset]).Focus();
    }

    private void PresetClicked(object sender, RoutedEventArgs e)
    {
        _activePreset = (int)((Button)sender).Tag;
        ClosePresets();
        _surface.Focus();
    }

    private void ClosePresets()
    {
        if (_presetAnchor is null)
            return;

        _presetAnchor = null;
        PresetPanel.Visibility = Visibility.Collapsed;
    }

    private void PresetAreaChanged(object sender, RoutedEventArgs e) => PositionPresets();

    private void PositionPresets()
    {
        if (_presetAnchor is not { } anchor)
            return;

        PresetPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var origin = anchor.TranslatePoint(new Point(anchor.ActualWidth + 12, 0), PresetLayer);
        var size = PresetPanel.DesiredSize;
        Canvas.SetLeft(PresetPanel, Math.Clamp(origin.X, 8, Math.Max(8, PresetLayer.ActualWidth - size.Width - 8)));
        Canvas.SetTop(PresetPanel, Math.Clamp(origin.Y, 8, Math.Max(8, PresetLayer.ActualHeight - size.Height - 8)));
    }

    private void EditorMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_presetAnchor is null || PresetPanel.IsMouseOver)
            return;

        ClosePresets();
        if (e.ChangedButton == MouseButton.Left && _surface.IsMouseOver)
        {
            _surface.Focus();
            e.Handled = true;
        }
    }

    private void AnnotationMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (IsCropping || _anchor is not null)
        {
            e.Handled = true;
            return;
        }

        var index = e.CursorLeft == -1 && e.CursorTop == -1
            ? _selected
            : FindAnnotation(Mouse.GetPosition(_surface));
        if (index < 0)
        {
            e.Handled = true;
            return;
        }

        ClosePresets();
        SelectTool.IsChecked = true;
        _selected = index;
        UpdateView();
    }

    private void EditAnnotationStyle(object sender, RoutedEventArgs e)
    {
        if (_selected < 0)
            return;

        var annotation = Annotations[_selected];
        var dialog = new AnnotationStyleDialog(this, annotation);
        if (dialog.ShowDialog() != true)
            return;

        var style = dialog.SelectedStyle;
        var edited = style.CreateAnnotation(annotation.Kind, annotation.Points, annotation.PresetIndex, annotation.Text);
        if (edited != annotation)
            ReplaceSelected(edited);

        if (dialog.SaveAsDefault && annotation.PresetIndex is { } index)
        {
            try
            {
                _presets.Save(annotation.Kind, index, style);
                SetStatus(null);
            }
            catch (Exception error) when (Images.IsFileError(error))
            {
                SetStatus("Could not save the default preset.");
                Trace.TraceWarning("Save annotation preset: {0}", error.Message);
            }
        }
    }
}
