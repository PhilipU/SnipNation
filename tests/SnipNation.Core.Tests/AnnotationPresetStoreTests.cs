using SnipNation.Core;
using Xunit;

namespace SnipNation.Core.Tests;

public sealed class AnnotationPresetStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("snipnation-presets-").FullName;

    [Fact]
    public void SavingADefaultReplacesOnlyItsToolAndSlot()
    {
        var directory = Path.Combine(_root, "Presets");
        var defaults = new AnnotationPresetStore(directory);
        var original = new AnnotationStyle(0xFFC6FA78, 4);
        var edited = new AnnotationStyle(0xFFA0A0A0, 8, StrokePattern.Dotted, ArrowheadStyle.DoubleEnded);
        var line = new AnnotationStyle(0xFFFFFFFF, 3, StrokePattern.Dashed);
        const int slot = 2;

        Assert.Null(defaults.Load(AnnotationKind.Arrow, slot));
        defaults.Save(AnnotationKind.Arrow, slot, original);
        defaults.Save(AnnotationKind.Line, slot, line);
        defaults.Save(AnnotationKind.Arrow, slot + 1, original);

        var placed = original.CreateAnnotation(AnnotationKind.Arrow, [new(0, 0), new(30, 20)], slot);
        defaults.Save(AnnotationKind.Arrow, slot, edited);

        var reopened = new AnnotationPresetStore(directory);
        var nextStyle = Assert.IsType<AnnotationStyle>(reopened.Load(AnnotationKind.Arrow, slot));
        var next = nextStyle.CreateAnnotation(AnnotationKind.Arrow, placed.Points, slot);
        Assert.Equal(edited, next.Style);
        Assert.Equal(slot, next.PresetIndex);
        Assert.Equal(original, placed.Style);
        Assert.Equal(line, reopened.Load(AnnotationKind.Line, slot));
        Assert.Equal(original, reopened.Load(AnnotationKind.Arrow, slot + 1));
        Assert.Null(reopened.Load(AnnotationKind.Arrow, 0));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
