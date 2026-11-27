using System.Diagnostics;
using System.Windows.Media;
using SnipNation.Branding;
using SnipNation.Core;
using SnipNation.Imaging;

namespace SnipNation.Editor;

internal sealed class ToolPresets(string directory)
{
    private static readonly Lazy<StyleColor[]> Colors = new(() =>
    [
        new("Lime green", Brand.Argb("Accent")),
        new("Dark green", Brand.Argb("LightAccent")),
        new("Charcoal", Brand.Argb("Background")),
        new("White", Brand.Argb("Foreground")),
        new("Light gray", Brand.Argb("Muted")),
        new("Dark gray", Brand.Argb("LightMuted")),
        new("Off-white", Brand.Argb("LightBackground"))
    ]);
    private static readonly Lazy<Dictionary<AnnotationKind, AnnotationStyle[]>> Defaults = new(CreateDefaults);
    private readonly AnnotationPresetStore _store = new(directory);

    internal static IReadOnlyList<StyleColor> Palette => Colors.Value;

    internal AnnotationStyle Get(AnnotationKind kind, int index)
    {
        try
        {
            if (_store.Load(kind, index) is { } saved)
                return saved;
        }
        catch (Exception error) when (Images.IsFileError(error))
        {
            Trace.TraceWarning("Load annotation preset: {0}", error.Message);
        }
        return Defaults.Value[kind][index];
    }

    internal void Save(AnnotationKind kind, int index, AnnotationStyle style) => _store.Save(kind, index, style);

    internal static string ArrowheadLabel(ArrowheadStyle shape) => shape switch
    {
        ArrowheadStyle.Open => "Open",
        ArrowheadStyle.DoubleEnded => "Double-ended",
        _ => "Filled"
    };

    internal static string OpacityLabel(byte alpha) => FormattableString.Invariant($"{Math.Round(alpha * 100.0 / 255)}%");

    internal static string Describe(AnnotationKind kind, int index, AnnotationStyle style)
    {
        var color = Palette.FirstOrDefault(option => option.Argb == (style.Argb | 0xFF000000))?.Name ?? "Custom color";
        var size = FormattableString.Invariant($"{style.Size} px");
        var details = kind switch
        {
            AnnotationKind.Arrow => $"{size}, {style.Pattern}, {ArrowheadLabel(style.Arrowhead)}",
            AnnotationKind.Highlight => OpacityLabel((byte)(style.Argb >> 24)),
            AnnotationKind.Text => $"{size}, {(style.Bold ? "Bold" : "Regular")}",
            _ => $"{size}, {style.Pattern}"
        };
        return $"Preset {index + 1}, {color}, {details}";
    }

    private static Dictionary<AnnotationKind, AnnotationStyle[]> CreateDefaults()
    {
        var lime = Brand.Argb("Accent");
        var green = Brand.Argb("LightAccent");
        var charcoal = Brand.Argb("Background");
        var white = Brand.Argb("Foreground");
        var lightGray = Brand.Argb("Muted");
        var darkGray = Brand.Argb("LightMuted");
        var paper = Brand.Argb("LightBackground");

        return new Dictionary<AnnotationKind, AnnotationStyle[]>
        {
            [AnnotationKind.Arrow] =
            [
                new(lime, 4),
                new(white, 3, Arrowhead: ArrowheadStyle.Open),
                new(green, 5, StrokePattern.Dashed),
                new(charcoal, 3, Arrowhead: ArrowheadStyle.DoubleEnded),
                new(paper, 6, StrokePattern.Dotted, ArrowheadStyle.DoubleEnded),
                new(lightGray, 4, StrokePattern.Dashed, ArrowheadStyle.Open)
            ],
            [AnnotationKind.Line] =
            [
                new(lime, 4),
                new(white, 2, StrokePattern.Dashed),
                new(green, 5, StrokePattern.Dotted),
                new(charcoal, 8),
                new(lightGray, 4, StrokePattern.Dotted),
                new(darkGray, 6, StrokePattern.Dashed)
            ],
            [AnnotationKind.Freehand] =
            [
                new(lime, 4),
                new(white, 2),
                new(green, 6),
                new(lightGray, 8, StrokePattern.Dashed),
                new(charcoal, 3, StrokePattern.Dotted),
                new(paper, 10)
            ],
            [AnnotationKind.Highlight] =
            [
                Highlight(lime, 102),
                Highlight(green, 76),
                Highlight(charcoal, 64),
                Highlight(white, 153),
                Highlight(darkGray, 115),
                Highlight(lightGray, 179)
            ],
            [AnnotationKind.Text] =
            [
                new(charcoal, 24),
                new(lime, 24, Bold: true),
                new(green, 28, Bold: true),
                new(white, 16),
                new(lightGray, 32),
                new(darkGray, 36, Bold: true)
            ]
        };
    }

    private static AnnotationStyle Highlight(uint color, byte alpha) =>
        new((color & 0x00FFFFFF) | ((uint)alpha << 24), 4);

    internal sealed record StyleColor(string Name, uint Argb)
    {
        public Brush Swatch { get; } = new SolidColorBrush(AnnotationGraphics.ToColor(Argb));
    }
}
