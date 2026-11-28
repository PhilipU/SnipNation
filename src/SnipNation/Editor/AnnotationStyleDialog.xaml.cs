using System.Windows;
using System.Windows.Controls;
using SnipNation.Branding;
using SnipNation.Core;

namespace SnipNation.Editor;

public partial class AnnotationStyleDialog : Window
{
    private readonly Annotation _annotation;
    private bool _ready;

    internal AnnotationStyleDialog(Window owner, Annotation annotation)
    {
        _annotation = annotation;
        InitializeComponent();
        Owner = owner;

        var isText = annotation.Kind == AnnotationKind.Text;
        var isHighlight = annotation.Kind == AnnotationKind.Highlight;
        var textVisibility = isText ? Visibility.Visible : Visibility.Collapsed;
        SizeRow.Visibility = isHighlight ? Visibility.Collapsed : Visibility.Visible;
        ArrowShapeRow.Visibility = annotation.Kind == AnnotationKind.Arrow ? Visibility.Visible : Visibility.Collapsed;
        OpacityRow.Visibility = isHighlight ? Visibility.Visible : Visibility.Collapsed;
        FontRow.Visibility = textVisibility;
        BoldBox.Visibility = textVisibility;
        OutlineBox.Visibility = textVisibility;
        DefaultBox.Visibility = annotation.PresetIndex.HasValue ? Visibility.Visible : Visibility.Collapsed;
        SizeLabel.Content = isText ? "Font size" : "Width";

        var colors = ToolPresets.Palette.ToList();
        var color = annotation.Argb | 0xFF000000;
        if (!colors.Any(option => option.Argb == color))
            colors.Add(new ToolPresets.StyleColor("Current color", color));
        ColorBox.ItemsSource = colors;
        ColorBox.SelectedItem = colors.First(option => option.Argb == color);

        double[] sizes = isText ? [16, 20, 24, 28, 32, 36, 44, 52] : [2, 3, 4, 5, 6, 8, 10, 12];
        var widths = sizes.Append(annotation.Size).Distinct().Order();
        SetOptions(SizeBox, widths, annotation.Size, size => FormattableString.Invariant($"{size} px"));
        SetOptions(PatternBox, Enum.GetValues<StrokePattern>(), annotation.Pattern, pattern => pattern.ToString());
        SetOptions(ArrowShapeBox, Enum.GetValues<ArrowheadStyle>(), annotation.Arrowhead, ToolPresets.ArrowShapeLabel);
        SetOptions(FontBox, Enum.GetValues<AnnotationFont>(), annotation.Font, ToolPresets.FontLabel);
        var alpha = (byte)(annotation.Argb >> 24);
        byte[] opacities = [64, 76, 102, 115, 153, 179, 204];
        SetOptions(OpacityBox, opacities.Append(alpha).Distinct().Order(), alpha, ToolPresets.OpacityLabel);
        BoldBox.IsChecked = annotation.Bold;
        OutlineBox.IsChecked = annotation.Outline;

        _ready = true;
        RefreshPreview();
        Loaded += (_, _) => ColorBox.Focus();
    }

    internal bool SaveAsDefault => DefaultBox.IsChecked == true;

    internal AnnotationStyle SelectedStyle
    {
        get
        {
            var color = ColorBox.SelectedItem as ToolPresets.StyleColor;
            var rgb = (color?.Argb ?? _annotation.Argb) & 0x00FFFFFF;
            var alpha = SelectedValue(OpacityBox, (byte)(_annotation.Argb >> 24));
            return new AnnotationStyle(rgb | ((uint)alpha << 24), SelectedValue(SizeBox, _annotation.Size),
                SelectedValue(PatternBox, _annotation.Pattern), SelectedValue(ArrowShapeBox, _annotation.Arrowhead),
                BoldBox.IsChecked == true, SelectedValue(FontBox, _annotation.Font), OutlineBox.IsChecked == true);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Brand.ApplyCaption(this);
    }

    private void ApplyClicked(object sender, RoutedEventArgs e) => DialogResult = true;

    private void StyleChanged(object sender, RoutedEventArgs e)
    {
        if (_ready)
            RefreshPreview();
    }

    private void RefreshPreview()
    {
        var style = SelectedStyle;
        var showsPattern = _annotation.Kind is AnnotationKind.Line or AnnotationKind.Freehand ||
            (_annotation.Kind == AnnotationKind.Arrow && ToolPresets.UsesStrokePattern(style.Arrowhead));
        PatternRow.Visibility = showsPattern ? Visibility.Visible : Visibility.Collapsed;
        PreviewImage.Source = AnnotationPreview.Render(_annotation.Kind, style);
    }

    private static void SetOptions<T>(ComboBox box, IEnumerable<T> options, T selected, Func<T, string> label)
    {
        foreach (var option in options)
        {
            var item = new ComboBoxItem { Content = label(option), Tag = option };
            box.Items.Add(item);
            if (EqualityComparer<T>.Default.Equals(option, selected))
                box.SelectedItem = item;
        }
    }

    private static T SelectedValue<T>(ComboBox box, T fallback) =>
        box.SelectedItem is ComboBoxItem { Tag: T value } ? value : fallback;
}
