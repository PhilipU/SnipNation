using SnipNation.Core;
using Xunit;

namespace SnipNation.Core.Tests;

public sealed class CaptureSelectionTests
{
    private static readonly PixelRect LeftDisplay = new(-1920, -240, 1920, 1080);
    private static readonly PixelRect RightDisplay = new(0, 0, 2560, 1440);
    private static readonly PixelRect Desktop = LeftDisplay.Enclose(RightDisplay);

    [Fact]
    public void WindowSnapUsesTopmostWindowAndClipsToDesktop()
    {
        var front = new PixelRect(-2000, -200, 900, 500);
        var back = new PixelRect(-1800, -100, 1800, 700);
        var selection = new CaptureSelection(Desktop, [front, back]);
        var cursor = new PixelPoint(-1400, 25);

        selection.Move(cursor);
        Assert.Equal(new PixelRect(-1920, -200, 820, 500), selection.Confirm());
        selection.Press(cursor, 4);
        Assert.Equal(selection.Highlight, selection.Release(cursor));
    }

    [Fact]
    public void DragAcrossMonitorsStaysSelectedUntilAnotherClickOrEnter()
    {
        var selection = new CaptureSelection(Desktop, [new PixelRect(-600, 0, 1200, 750)]);
        var start = new PixelPoint(150, 275);
        var end = new PixelPoint(-150, 125);
        var expected = new PixelRect(-150, 125, 300, 150);

        selection.Press(start, 4);
        selection.Move(end);
        Assert.Null(selection.Confirm());
        Assert.Null(selection.Release(end));
        selection.Move(new PixelPoint(1000, 1000));
        Assert.Equal(expected, selection.Confirm());

        selection.Press(end, 4);
        Assert.Equal(expected, selection.Release(end));
    }

    [Fact]
    public void LostMouseCaptureClearsUnfinishedDrag()
    {
        var selection = new CaptureSelection(Desktop, []);
        selection.Press(new PixelPoint(10, 10), 4);
        selection.Move(new PixelPoint(-10, 40));
        selection.CancelPress();

        Assert.Null(selection.Confirm());
        Assert.Null(selection.Release(new PixelPoint(-10, 40)));
    }

    [Fact]
    public void CropPiecesTranslateNegativeOriginsAndLeaveDisplayGaps()
    {
        var selection = new PixelRect(-150, -20, 300, 100);
        Assert.Equal(new CropPiece(new PixelRect(1770, 220, 150, 100), new PixelRect(0, 0, 150, 100)),
            CropPiece.ForDisplay(LeftDisplay, selection));
        Assert.Equal(new CropPiece(new PixelRect(0, 0, 150, 80), new PixelRect(150, 20, 150, 80)),
            CropPiece.ForDisplay(RightDisplay, selection));
        Assert.Null(CropPiece.ForDisplay(RightDisplay, new PixelRect(-100, 0, 100, 100)));
    }

    [Fact]
    public void AClickWithoutAWindowDoesNotCreateACapture()
    {
        var selection = new CaptureSelection(Desktop, []);
        var cursor = new PixelPoint(200, 100);
        selection.Press(cursor, 4);
        Assert.Null(selection.Release(cursor));
    }
}
