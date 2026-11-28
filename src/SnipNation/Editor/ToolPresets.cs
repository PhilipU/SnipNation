using System.Diagnostics;
using System.Windows.Media;
using SnipNation.Branding;
using SnipNation.Core;
using SnipNation.Imaging;

namespace SnipNation.Editor;

internal sealed class ToolPresets(string directory)
{
    private const uint Green = 0xFF278747;
    private const uint Blue = 0xFF2D70E8;
    private const uint Red = 0xFFDE3749;
    private const uint Yellow = 0xFFFFCF22;
    private const uint Orange = 0xFFED8226;
    private const uint Purple = 0xFF9051D1;

    private static readonly Lazy<StyleColor[]> Colors = new(() =>
    [
        new("Green", Green),
        new("Blue", Blue),
        new("Red", Red),
        new("Yellow", Yellow),
        new("Orange", Orange),
        new("Purple", Purple),
        new("Black", 0xFF000000),
        new("Lime green", Brand.Argb("Accent")),
        new("Dark green", Brand.Argb("LightAccent")),
        new("Charcoal", Brand.Argb("Background")),
        new("White", Brand.Argb("Foreground")),
        new("Light gray", Brand.Argb("Muted")),
        new("Dark gray", Brand.Argb("LightMuted")),
        new("Off-white", Brand.Argb("LightBackground"))
    ]);
    private static readonly Dictionary<AnnotationKind, AnnotationStyle[]> Defaults = new()
    {
        [AnnotationKind.Arrow] =
        [
            new(Green, 4),
            new(Blue, 4, Arrowhead: ArrowheadStyle.Curved),
            new(Red, 5, Arrowhead: ArrowheadStyle.Tapered),
            new(Yellow, 5, Arrowhead: ArrowheadStyle.Block),
            new(Orange, 3, Arrowhead: ArrowheadStyle.Open),
            new(Purple, 3, Arrowhead: ArrowheadStyle.DoubleEnded)
        ],
        [AnnotationKind.Line] =
        [
            new(Green, 4),
            new(Blue, 3, StrokePattern.Dashed),
            new(Red, 5, StrokePattern.Dotted),
            new(Yellow, 8),
            new(Orange, 4, StrokePattern.Dotted),
            new(Purple, 6, StrokePattern.Dashed)
        ],
        [AnnotationKind.Freehand] =
        [
            new(Green, 4),
            new(Blue, 2),
            new(Red, 6),
            new(Yellow, 8, StrokePattern.Dashed),
            new(Orange, 3, StrokePattern.Dotted),
            new(Purple, 10)
        ],
        [AnnotationKind.Highlight] =
        [
            Highlight(Green), Highlight(Blue), Highlight(Red), Highlight(Yellow), Highlight(Orange), Highlight(Purple)
        ],
        [AnnotationKind.Text] =
        [
            new(Green, 28),
            new(Blue, 28, Font: AnnotationFont.Serif),
            new(Red, 28, Font: AnnotationFont.Monospace),
            new(Yellow, 28, Outline: true),
            new(Orange, 28, Font: AnnotationFont.Serif, Outline: true),
            new(Purple, 28, Font: AnnotationFont.Monospace, Outline: true)
        ]
    };
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
        return Defaults[kind][index];
    }

    internal void Save(AnnotationKind kind, int index, AnnotationStyle style) => _store.Save(kind, index, style);

    internal static string ArrowShapeLabel(ArrowheadStyle shape) => shape switch
    {
        ArrowheadStyle.Open => "Open",
        ArrowheadStyle.DoubleEnded => "Double-ended",
        ArrowheadStyle.Tapered => "Tapered",
        ArrowheadStyle.Curved => "Curved",
        ArrowheadStyle.Block => "Block",
        _ => "Filled"
    };

    internal static bool UsesStrokePattern(ArrowheadStyle shape) =>
        shape is not (ArrowheadStyle.Tapered or ArrowheadStyle.Block);

    internal static string FontLabel(AnnotationFont font) => font switch
    {
        AnnotationFont.Serif => "Serif",
        AnnotationFont.Monospace => "Monospace",
        _ => "Sans serif"
    };

    internal static string OpacityLabel(byte alpha) => FormattableString.Invariant($"{Math.Round(alpha * 100.0 / 255)}%");

    internal static string Describe(AnnotationKind kind, int index, AnnotationStyle style)
    {
        var color = Palette.FirstOrDefault(option => option.Argb == (style.Argb | 0xFF000000))?.Name ?? "Custom color";
        var size = FormattableString.Invariant($"{style.Size} px");
        var details = kind switch
        {
            AnnotationKind.Arrow when UsesStrokePattern(style.Arrowhead) =>
                $"{ArrowShapeLabel(style.Arrowhead)}, {size}, {style.Pattern}",
            AnnotationKind.Arrow => $"{ArrowShapeLabel(style.Arrowhead)}, {size}",
            AnnotationKind.Highlight => OpacityLabel((byte)(style.Argb >> 24)),
            AnnotationKind.Text => $"{FontLabel(style.Font)}, {size}, {(style.Bold ? "Bold" : "Regular")}" +
                (style.Outline ? ", Outlined" : ""),
            _ => $"{size}, {style.Pattern}"
        };
        return $"Preset {index + 1}, {color}, {details}";
    }

    private static AnnotationStyle Highlight(uint color) => new((color & 0x00FFFFFF) | 0x73000000, 4);

    internal sealed record StyleColor(string Name, uint Argb)
    {
        public Brush Swatch { get; } = new SolidColorBrush(AnnotationGraphics.ToColor(Argb));
    }
}
