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
                Pattern: StrokePattern.Dashed, Arrowhead: ArrowheadStyle.Curved, PresetIndex: 1),
            new(AnnotationKind.Line, [new(1, 2), new(3, 4)], 0xFF2057BA, 4),
            new(AnnotationKind.Freehand, [new(0, 0), new(5, 10), new(8, 12)], 0xFFDC2633, 4),
            new(AnnotationKind.Highlight, [new(120, 40), new(230, 70)], 0x66FFD43B, 4),
            new(AnnotationKind.Text, [new(30, 90)], 0xFFFFFFFF, 24, "Open\nSave…",
                Bold: true, PresetIndex: 5, Font: AnnotationFont.Monospace, Outline: true),
            new(AnnotationKind.Frame, [new(4, 4), new(90, 37)], 0xFFFFCF22, 4,
                Pattern: StrokePattern.Dotted, PresetIndex: 3, Frame: FrameShape.Cloud),
            new(AnnotationKind.Number, [new(22, 35)], 0xFF278747, 28, PresetIndex: 0, Number: 1)
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
    public void ConnectedNumbersKeepTheirTargetAndSequenceAfterCropping()
    {
        var capture = new CaptureStore(_root).SaveCapture(stream => stream.Write("original"u8));
        var style = new AnnotationStyle(0xFFDE3749, 28, Marker: NumberShape.Diamond, Connector: ConnectorStyle.Curved);
        var mark = style.CreateAnnotation(AnnotationKind.Number, [new(40, 50), new(96, 30)], 2, number: 4);
        var original = new CaptureEdits([mark]);
        Assert.Equal(1, new CaptureEdits([]).NextNumber);
        Assert.Equal(5, original.NextNumber);

        CaptureStore.SaveEdits(capture, original.CropTo(new PixelRect(12, 10, 110, 80)));
        var reopened = CaptureStore.LoadEdits(capture);
        var number = Assert.Single(reopened.Annotations);
        Assert.Equal(new ImagePoint[] { new(28, 40), new(84, 20) }, number.Points);
        Assert.Equal(style, number.Style);
        Assert.Equal(4, number.Number);
        Assert.Equal(2, number.PresetIndex);
        Assert.Equal(5, reopened.NextNumber);
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

    [Fact]
    public void AChosenFolderListsOnlyItsSnipNationCaptures()
    {
        var defaultFolder = Path.Combine(_root, "Default");
        var chosenFolder = Path.Combine(_root, "Screenshots for guides");
        var previous = new CaptureStore(defaultFolder).SaveCapture(stream => stream.Write("previous"u8));
        var current = new CaptureStore(chosenFolder).SaveCapture(stream => stream.Write("current"u8));
        var unrelated = Directory.CreateDirectory(Path.Combine(chosenFolder, "Reference images"));
        File.WriteAllText(Path.Combine(unrelated.FullName, "original.png"), "reference");

        Assert.Equal(chosenFolder, Path.GetDirectoryName(current.DirectoryPath));
        Assert.Equal(current, Assert.Single(new CaptureStore(chosenFolder).Recent(36)));
        Assert.Equal(previous, Assert.Single(new CaptureStore(defaultFolder).Recent(36)));
    }

    [Fact]
    public async Task ConcurrentEditsDoNotOverwriteASavedRevision()
    {
        var store = new CaptureStore(_root);
        var capture = store.SaveCapture(stream => stream.Write("original"u8));
        var original = CaptureStore.ReadEdits(capture);
        PixelRect[] crops = [new(0, 0, 100, 100), new(1, 1, 80, 80)];

        var revisions = await Task.WhenAll(crops.Select(crop => Task.Run(() =>
        {
            try
            {
                return CaptureStore.SaveEdits(capture, new CaptureEdits([], crop), original.Revision);
            }
            catch (CaptureEditConflictException)
            {
                return null;
            }
        })));

        var current = CaptureStore.ReadEdits(capture);
        Assert.Equal(current.Revision, Assert.Single(revisions.OfType<string>()));
        Assert.Contains(current.Edits.Crop!.Value, crops);
        Assert.Equal("original", File.ReadAllText(capture.OriginalPath));
    }

    [Fact]
    public void CaptureLookupAndPaginationUseOnlyIdsInTheCurrentFolder()
    {
        var store = new CaptureStore(_root);
        var first = store.SaveCapture(stream => stream.Write("first"u8));
        var second = store.SaveCapture(stream => stream.Write("second"u8));
        var page = Assert.Single(store.Recent(1));
        var remaining = Assert.Single(store.Recent(1, page.Id));

        Assert.Equal(new[] { first, second }.OrderBy(item => item.Id), new[] { page, remaining }.OrderBy(item => item.Id));
        Assert.Equal(page, store.Find(page.Id));
        Assert.Null(store.Find(Path.Combine("..", page.Id)));
        Assert.Null(new CaptureStore(Path.Combine(_root, "missing")).Find(page.Id));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
