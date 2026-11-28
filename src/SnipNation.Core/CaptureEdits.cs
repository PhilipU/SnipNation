namespace SnipNation.Core;

public sealed record CaptureEdits(Annotation[] Annotations, PixelRect? Crop = null)
{
    public int NextNumber => (int)Math.Min(int.MaxValue,
        1L + Annotations.Where(mark => mark.Kind == AnnotationKind.Number).Select(mark => mark.Number).DefaultIfEmpty().Max());

    public CaptureEdits CropTo(PixelRect selection) => new(
        Annotations.Select(mark => mark.Offset(-selection.X, -selection.Y)).ToArray(),
        new PixelRect((Crop?.X ?? 0) + selection.X, (Crop?.Y ?? 0) + selection.Y,
            selection.Width, selection.Height));
}
