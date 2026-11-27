namespace SnipNation.Core;

public sealed record CaptureEdits(Annotation[] Annotations, PixelRect? Crop = null)
{
    public CaptureEdits CropTo(PixelRect selection) => new(
        Annotations.Select(mark => mark.Offset(-selection.X, -selection.Y)).ToArray(),
        new PixelRect((Crop?.X ?? 0) + selection.X, (Crop?.Y ?? 0) + selection.Y,
            selection.Width, selection.Height));
}
