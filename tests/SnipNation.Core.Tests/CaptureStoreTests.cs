using SnipNation.Core;
using Xunit;

namespace SnipNation.Core.Tests;

public sealed class CaptureStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("snipnation-tests-").FullName;

    [Fact]
    public void MarkingsRoundTripAndTheOriginalIsPreserved()
    {
        var store = new CaptureStore(_root);
        var capture = store.SaveCapture(stream => stream.Write("original"u8));
        Annotation[] annotations =
        [
            new(AnnotationKind.Arrow, [new(0, 1.5), new(42, 18)], 0xFFDC2633, 4),
            new(AnnotationKind.Line, [new(1, 2), new(3, 4)], 0xFF2057BA, 4),
            new(AnnotationKind.Freehand, [new(0, 0), new(5, 10), new(8, 12)], 0xFFDC2633, 4),
            new(AnnotationKind.Highlight, [new(120, 40), new(230, 70)], 0x66FFD43B, 4),
            new(AnnotationKind.Text, [new(30, 90)], 0xFFFFFFFF, 24, "Open\nSave…")
        ];

        Assert.Empty(CaptureStore.LoadAnnotations(capture));
        CaptureStore.SaveAnnotations(capture, annotations);
        var reopened = Assert.Single(new CaptureStore(_root).Recent(36));
        var restored = CaptureStore.LoadAnnotations(reopened);

        Assert.Equal(annotations.Length, restored.Length);
        for (var index = 0; index < annotations.Length; index++)
        {
            Assert.Equal(annotations[index].Points, restored[index].Points);
            Assert.Equal(annotations[index], restored[index] with { Points = annotations[index].Points });
        }
        Assert.Equal("original", File.ReadAllText(reopened.OriginalPath));
    }

    [Fact]
    public void AFailedWriteLeavesThePreviousFileInPlace()
    {
        var store = new CaptureStore(_root);
        var capture = store.SaveCapture(stream => stream.Write("original"u8));
        Assert.Throws<IOException>(() => CaptureStore.WriteFile(capture.OriginalPath, stream =>
        {
            stream.Write("incomplete"u8);
            throw new IOException("Write failed");
        }));

        Assert.Equal("original", File.ReadAllText(capture.OriginalPath));
        Assert.Empty(Directory.GetFiles(capture.DirectoryPath, "*.tmp"));
    }

    [Fact]
    public void IncompleteCapturesAreNotListed()
    {
        var store = new CaptureStore(_root);
        Assert.Throws<IOException>(() => store.SaveCapture(_ => throw new IOException("Write failed")));
        Assert.Empty(store.Recent(36));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
