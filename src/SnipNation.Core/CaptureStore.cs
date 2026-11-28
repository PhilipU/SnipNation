using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnipNation.Core;

public sealed record CaptureFile(string DirectoryPath)
{
    public string Id => Path.GetFileName(DirectoryPath);
    public string OriginalPath => Path.Combine(DirectoryPath, "original.png");
    public string AnnotationPath => Path.Combine(DirectoryPath, "annotations.json");
}

public sealed record SavedCaptureEdits(CaptureEdits Edits, string Revision);

public sealed class CaptureEditConflictException() : IOException("Screenshot edits changed. Read the current revision and try again.");

public sealed class CaptureStore(string root)
{
    private const string TimestampFormat = "yyyyMMdd'T'HHmmssfff'Z'";

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

    public string DirectoryPath { get; } = root;

    public CaptureFile SaveCapture(Action<Stream> writePng)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var directory = Directory.CreateDirectory(Path.Combine(DirectoryPath, $"{timestamp}_{Guid.NewGuid():N}"));
        var capture = new CaptureFile(directory.FullName);
        WriteFile(capture.OriginalPath, writePng);
        return capture;
    }

    public CaptureFile? Find(string id)
    {
        if (!IsCaptureId(id))
            return null;

        var capture = new CaptureFile(Path.Combine(DirectoryPath, id));
        return File.Exists(capture.OriginalPath) ? capture : null;
    }

    public IReadOnlyList<CaptureFile> Recent(int limit, string? before = null)
    {
        if (!Directory.Exists(DirectoryPath))
            return [];

        return Directory.EnumerateDirectories(DirectoryPath)
            .Select(path => new CaptureFile(path))
            .Where(capture => IsCaptureId(capture.Id) && (before is null || string.CompareOrdinal(capture.Id, before) < 0) &&
                File.Exists(capture.OriginalPath))
            .OrderByDescending(capture => capture.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    public static bool IsCaptureId(string id)
    {
        var span = id.AsSpan();
        var separator = span.IndexOf('_');
        return separator > 0 && span.Length == separator + 33 &&
            Guid.TryParseExact(span[(separator + 1)..], "N", out _) &&
            DateTimeOffset.TryParseExact(span[..separator], TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out _);
    }

    public static CaptureEdits LoadEdits(CaptureFile capture) => ReadEdits(capture).Edits;

    public static SavedCaptureEdits ReadEdits(CaptureFile capture)
    {
        try
        {
            using var stream = File.Open(capture.AnnotationPath, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            var revision = Convert.ToHexString(SHA256.HashData(stream));
            stream.Position = 0;
            using var document = JsonDocument.Parse(stream);
            var content = document.RootElement;
            var edits = content.ValueKind == JsonValueKind.Array
                ? new CaptureEdits(content.Deserialize<Annotation[]>(JsonOptions)!)
                : content.Deserialize<CaptureEdits>(JsonOptions);
            if (edits?.Annotations is null || edits.Annotations.Any(annotation => !IsValidAnnotation(annotation)) ||
                edits.Crop is { } crop && (crop.X < 0 || crop.Y < 0 || crop.IsEmpty))
                throw new InvalidDataException("Invalid screenshot edits.");
            return new SavedCaptureEdits(edits, revision);
        }
        catch (FileNotFoundException)
        {
            return new SavedCaptureEdits(new CaptureEdits([]), "0");
        }
    }

    public static string SaveEdits(CaptureFile capture, CaptureEdits edits, string? expectedRevision = null)
    {
        using var mutex = new Mutex(false, $"SnipNation.Capture.{capture.Id.ToUpperInvariant()}");
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(5)))
                throw new IOException("Another process is saving this screenshot.");
        }
        catch (AbandonedMutexException)
        {
            // This process owns the mutex after an interrupted writer.
        }

        try
        {
            if (expectedRevision is not null && ReadEdits(capture).Revision != expectedRevision)
                throw new CaptureEditConflictException();

            var bytes = edits.Crop is null
                ? JsonSerializer.SerializeToUtf8Bytes(edits.Annotations, JsonOptions)
                : JsonSerializer.SerializeToUtf8Bytes(edits, JsonOptions);
            WriteFile(capture.AnnotationPath, stream => stream.Write(bytes));
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    public static void WriteFile(string path, Action<Stream> write, bool overwrite = true)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                write(stream);
            File.Move(tempPath, path, overwrite);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public static bool IsValidAnnotation(Annotation? annotation)
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
