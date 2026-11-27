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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter<AnnotationKind>() }
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

    public static Annotation[] LoadAnnotations(CaptureFile capture)
    {
        try
        {
            using var stream = File.Open(capture.AnnotationPath, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            var annotations = JsonSerializer.Deserialize<Annotation[]>(stream, JsonOptions);
            if (annotations is null || annotations.Any(annotation => !IsValid(annotation)))
                throw new InvalidDataException("Invalid annotations.");
            return annotations;
        }
        catch (FileNotFoundException)
        {
            return [];
        }
    }

    public static void SaveAnnotations(CaptureFile capture, IReadOnlyList<Annotation> annotations) =>
        WriteFile(capture.AnnotationPath, stream => JsonSerializer.Serialize(stream, annotations, JsonOptions));

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
            !double.IsFinite(annotation.Size) || annotation.Size <= 0 ||
            annotation.Points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            return false;

        return annotation.Kind switch
        {
            AnnotationKind.Arrow or AnnotationKind.Line or AnnotationKind.Highlight => annotation.Points.Length == 2,
            AnnotationKind.Freehand => annotation.Points.Length >= 2,
            AnnotationKind.Text => annotation.Points.Length == 1,
            _ => false
        };
    }
}
