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
            new(AnnotationKind.Arrow, [new(0, 1.5), new(42, 18)], 0xFFC6FA78, 4,
                Pattern: StrokePattern.Dashed, Arrowhead: ArrowheadStyle.Open, PresetIndex: 5),
            new(AnnotationKind.Line, [new(1, 2), new(3, 4)], 0xFF2057BA, 4),
            new(AnnotationKind.Freehand, [new(0, 0), new(5, 10), new(8, 12)], 0xFFDC2633, 4),
            new(AnnotationKind.Highlight, [new(120, 40), new(230, 70)], 0x66FFD43B, 4),
            new(AnnotationKind.Text, [new(30, 90)], 0xFFFFFFFF, 24, "Open\nSave…", Bold: true, PresetIndex: 0)
        ];

        Assert.Empty(CaptureStore.LoadEdits(capture).Annotations);
        CaptureStore.SaveEdits(capture, new CaptureEdits(annotations));
        var reopened = Assert.Single(new CaptureStore(_root).Recent(36));
        var restored = CaptureStore.LoadEdits(reopened).Annotations;

        Assert.Equal(annotations.Length, restored.Length);
        for (var index = 0; index < annotations.Length; index++)
        {
            Assert.Equal(annotations[index].Points, restored[index].Points);
            Assert.Equal(annotations[index], restored[index] with { Points = annotations[index].Points });
        }
        Assert.Equal("original", File.ReadAllText(reopened.OriginalPath));
    }

    [Fact]
    public void CroppingAnExistingCaptureKeepsItsAnnotationsAligned()
    {
        var capture = new CaptureStore(_root).SaveCapture(stream => stream.Write("original"u8));
        File.WriteAllText(capture.AnnotationPath, """
            [{"kind":"Text","points":[{"x":45,"y":35}],"argb":4279572252,"size":24,"text":"Lorem ipsum"}]
            """);

        var original = CaptureStore.LoadEdits(capture);
        var firstCrop = original.CropTo(new PixelRect(10, 5, 120, 80));
        var secondCrop = firstCrop.CropTo(new PixelRect(20, 10, 80, 50));
        CaptureStore.SaveEdits(capture, secondCrop);

        var restored = CaptureStore.LoadEdits(capture);
        Assert.Null(original.Crop);
        Assert.Equal(new ImagePoint(45, 35), Assert.Single(Assert.Single(original.Annotations).Points));
        Assert.Equal(new PixelRect(30, 15, 80, 50), restored.Crop);
        var mark = Assert.Single(restored.Annotations);
        Assert.Equal("Lorem ipsum", mark.Text);
        Assert.Equal(new ImagePoint(15, 20), Assert.Single(mark.Points));
        Assert.Equal(new AnnotationStyle(0xFF15171C, 24), mark.Style);
        Assert.Null(mark.PresetIndex);
        Assert.Equal("original", File.ReadAllText(capture.OriginalPath));
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
