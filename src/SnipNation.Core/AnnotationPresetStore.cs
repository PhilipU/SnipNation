using System.Text.Json;

namespace SnipNation.Core;

public sealed class AnnotationPresetStore(string root)
{
    public const int SlotCount = 6;

    public AnnotationStyle? Load(AnnotationKind kind, int index)
    {
        var path = PathFor(kind, index);
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var style = JsonSerializer.Deserialize<AnnotationStyle>(stream, CaptureStore.JsonOptions);
            if (style is not { IsValid: true })
                throw new InvalidDataException("Invalid annotation preset.");
            return style;
        }
        catch (IOException error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    public void Save(AnnotationKind kind, int index, AnnotationStyle style)
    {
        var path = PathFor(kind, index);
        if (!style.IsValid)
            throw new ArgumentException("Invalid annotation style.", nameof(style));
        Directory.CreateDirectory(root);
        CaptureStore.WriteFile(path, stream => JsonSerializer.Serialize(stream, style, CaptureStore.JsonOptions));
    }

    private string PathFor(AnnotationKind kind, int index)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (index is < 0 or >= SlotCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        return Path.Combine(root, FormattableString.Invariant($"{kind}-{index + 1}.json"));
    }
}
