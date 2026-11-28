using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol;
using SnipNation.Core;
using SnipNation.Editor;

namespace SnipNation.Mcp;

internal enum EditAction
{
    Add,
    Update,
    Remove,
    Crop,
    Reset
}

internal sealed record StylePatch(
    [property: Description("Color as #RRGGBB or #AARRGGBB. Alpha is optional.")] string? Color = null,
    [property: Description("Stroke width, text size, or marker diameter in image pixels.")] double? Size = null,
    StrokePattern? Pattern = null,
    ArrowheadStyle? Arrowhead = null,
    bool? Bold = null,
    AnnotationFont? Font = null,
    bool? Outline = null,
    FrameShape? Frame = null,
    NumberShape? Marker = null,
    ConnectorStyle? Connector = null)
{
    internal AnnotationStyle Apply(AnnotationStyle style)
    {
        if (Size is { } size && (!double.IsFinite(size) || size <= 0 || size > 512))
            throw new McpException("Style size must be greater than zero and at most 512 pixels.");

        return style with
        {
            Argb = Color is null ? style.Argb : ReadColor(Color, style.Argb),
            Size = Size ?? style.Size,
            Pattern = Pattern ?? style.Pattern,
            Arrowhead = Arrowhead ?? style.Arrowhead,
            Bold = Bold ?? style.Bold,
            Font = Font ?? style.Font,
            Outline = Outline ?? style.Outline,
            Frame = Frame ?? style.Frame,
            Marker = Marker ?? style.Marker,
            Connector = Connector ?? style.Connector
        };
    }

    private static uint ReadColor(string color, uint previous)
    {
        if (color.Length is not (7 or 9) || color[0] != '#' ||
            !uint.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var argb))
            throw new McpException("Use #RRGGBB or #AARRGGBB for the annotation color.");

        return color.Length == 7 ? (previous & 0xFF000000) | argb : argb;
    }

    internal static object Describe(AnnotationStyle style) => new
    {
        Color = $"#{style.Argb:X8}", style.Size, style.Pattern, style.Arrowhead, style.Bold,
        style.Font, style.Outline, style.Frame, style.Marker, style.Connector
    };
}

internal sealed record AnnotationPatch(
    [property: Description("Required for add. Keep the existing kind for update.")] AnnotationKind? Kind = null,
    [property: Description("Image pixels. Use two points for arrows, lines, highlights and frames, two or more for freehand, one for text, and a marker plus an optional target for numbering.")]
    ImagePoint[]? Points = null,
    [property: Description("Preset number from 1 to 6. Defaults to 1 for an added annotation.")] int? Preset = null,
    StylePatch? Style = null,
    string? Text = null,
    [property: Description("Optional positive step number. The next number is automatic on add.")] int? Number = null);

internal sealed record ScreenshotEdit(
    EditAction Action,
    [property: Description("Zero-based annotation index for update or remove.")] int? Index = null,
    [property: Description("Annotation fields for add or update.")] AnnotationPatch? Annotation = null,
    [property: Description("Rectangle in current image pixels for crop.")] PixelRect? Region = null);

internal static class ScreenshotEdits
{
    internal static CaptureEdits Apply(CaptureEdits edits, ScreenshotEdit[] operations, int width, int height, ToolPresets presets)
    {
        if (operations is not { Length: > 0 })
            throw new McpException("Provide at least one edit.");

        foreach (var operation in operations)
        {
            if (operation is null)
                throw new McpException("Each edit must specify an action.");

            var currentWidth = edits.Crop?.Width ?? width;
            var currentHeight = edits.Crop?.Height ?? height;
            switch (operation)
            {
                case { Action: EditAction.Add, Index: null, Annotation: { } patch, Region: null }:
                    var annotation = BuildAnnotation(patch, null, edits.NextNumber, currentWidth, currentHeight, presets);
                    edits = edits with { Annotations = [.. edits.Annotations, annotation] };
                    break;
                case { Action: EditAction.Update, Index: { } index, Annotation: { } patch, Region: null }:
                    var updated = edits.Annotations.ToArray();
                    var position = CheckedIndex(index, updated.Length);
                    updated[position] = BuildAnnotation(patch, updated[position], edits.NextNumber, currentWidth, currentHeight, presets);
                    edits = edits with { Annotations = updated };
                    break;
                case { Action: EditAction.Remove, Index: { } index, Annotation: null, Region: null }:
                    index = CheckedIndex(index, edits.Annotations.Length);
                    edits = edits with { Annotations = [.. edits.Annotations.AsSpan(0, index), .. edits.Annotations.AsSpan(index + 1)] };
                    break;
                case { Action: EditAction.Crop, Index: null, Annotation: null, Region: { } crop }:
                    if (crop.IsEmpty || crop.X < 0 || crop.Y < 0 ||
                        crop.Width > currentWidth - crop.X || crop.Height > currentHeight - crop.Y)
                        throw new McpException("The crop must be inside the current image.");
                    edits = edits.CropTo(crop);
                    break;
                case { Action: EditAction.Reset, Index: null, Annotation: null, Region: null }:
                    edits = new CaptureEdits([]);
                    break;
                default:
                    throw new McpException("Use annotation for add, index and annotation for update, index for remove, or region for crop.");
            }
        }

        return edits;
    }

    internal static int PresetIndex(int preset)
    {
        if (preset is < 1 or > AnnotationPresetStore.SlotCount)
            throw new McpException("Preset must be between 1 and 6.");
        return preset - 1;
    }

    private static int CheckedIndex(int index, int count)
    {
        if (index < 0 || index >= count)
            throw new McpException("The annotation index is outside the current list.");
        return index;
    }

    private static Annotation BuildAnnotation(AnnotationPatch patch, Annotation? previous, int nextNumber,
        int width, int height, ToolPresets presets)
    {
        var kind = patch.Kind ?? previous?.Kind ?? throw new McpException("Choose an annotation kind.");
        if (previous is not null && kind != previous.Kind)
            throw new McpException("To change an annotation kind, remove it and add a new one.");

        if (patch.Points is { } supplied && supplied.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
            point.X < 0 || point.Y < 0 || point.X > width || point.Y > height))
            throw new McpException("Annotation points must be inside the current image.");

        var points = patch.Points ?? previous?.Points ?? throw new McpException("Provide annotation points.");
        var preset = patch.Preset is { } slot ? PresetIndex(slot) : previous is null ? 0 : previous.PresetIndex;
        var style = patch.Preset is null && previous is not null ? previous.Style : presets.Get(kind, preset ?? 0);
        style = patch.Style?.Apply(style) ?? style;
        var text = patch.Text ?? previous?.Text ?? "";
        var number = kind == AnnotationKind.Number ? patch.Number ?? previous?.Number ?? nextNumber : 0;
        if (kind == AnnotationKind.Text && string.IsNullOrWhiteSpace(text))
            throw new McpException("Provide text for the text annotation.");

        var result = style.CreateAnnotation(kind, points, preset, text, number);
        if (!CaptureStore.IsValidAnnotation(result))
            throw new McpException("Check the annotation point count, style, and step number for the chosen kind.");
        return result;
    }
}
