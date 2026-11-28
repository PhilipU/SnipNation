using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnipNation.Core;

public sealed record CaptureFile(string DirectoryPath)
{
    public string Id => Path.GetFileName(DirectoryPath);
    public string OriginalPath => Path.Combine(DirectoryPath, "original.png");
    public string AnnotationPath => Path.Combine(DirectoryPath, "annotations.json");
}

public sealed class CaptureStore(string root)
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IgnoreReadOnlyProperties = true,
        Converters =
        {
            new JsonStringEnumConverter<AnnotationKind>(),
            new JsonStringEnumConverter<StrokePattern>(),
            new JsonStringEnumConverter<ArrowheadStyle>(),
            new JsonStringEnumConverter<AnnotationFont>(),
            new JsonStringEnumConverter<FrameShape>(),
            new JsonStringEnumConverter<NumberShape>(),
            new JsonStringEnumConverter<ConnectorStyle>()
        }
    };

    public CaptureFile SaveCapture(Action<Stream> writePng)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var directory = Directory.CreateDirectory(Path.Combine(root, $"{timestamp}_{Guid.NewGuid():N}"));
        var capture = new CaptureFile(directory.FullName);
        WriteFile(capture.OriginalPath, writePng);
        return capture;
    }

    public IReadOnlyList<CaptureFile> Recent(int limit)
    {
        if (!Directory.Exists(root))
            return [];

        return Directory.EnumerateDirectories(root)
            .Select(path => new CaptureFile(path))
            .Where(capture => File.Exists(capture.OriginalPath))
            .OrderByDescending(capture => capture.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    public static CaptureEdits LoadEdits(CaptureFile capture)
    {
        try
        {
            using var stream = File.Open(capture.AnnotationPath, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var content = document.RootElement;
            var edits = content.ValueKind == JsonValueKind.Array
                ? new CaptureEdits(content.Deserialize<Annotation[]>(JsonOptions)!)
                : content.Deserialize<CaptureEdits>(JsonOptions);
            if (edits?.Annotations is null || edits.Annotations.Any(annotation => !IsValid(annotation)) ||
                edits.Crop is { } crop && (crop.X < 0 || crop.Y < 0 || crop.IsEmpty))
                throw new InvalidDataException("Invalid screenshot edits.");
            return edits;
        }
        catch (FileNotFoundException)
        {
            return new CaptureEdits([]);
        }
    }

    public static void SaveEdits(CaptureFile capture, CaptureEdits edits) =>
        WriteFile(capture.AnnotationPath, stream =>
        {
            if (edits.Crop is null)
                JsonSerializer.Serialize(stream, edits.Annotations, JsonOptions);
            else
                JsonSerializer.Serialize(stream, edits, JsonOptions);
        });

    public static void WriteFile(string path, Action<Stream> write)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                write(stream);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static bool IsValid(Annotation? annotation)
    {
        if (annotation is null || annotation.Points is null || annotation.Text is null ||
            !annotation.Style.IsValid || annotation.PresetIndex is < 0 or >= AnnotationPresetStore.SlotCount ||
            annotation.Points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            return false;

        return annotation.Kind switch
        {
            AnnotationKind.Arrow or AnnotationKind.Line or AnnotationKind.Highlight or AnnotationKind.Frame =>
                annotation.Points.Length == 2,
            AnnotationKind.Freehand => annotation.Points.Length >= 2,
            AnnotationKind.Text => annotation.Points.Length == 1,
            AnnotationKind.Number => (annotation.Points.Length is 1 or 2) && annotation.Number > 0,
            _ => false
        };
    }
}
