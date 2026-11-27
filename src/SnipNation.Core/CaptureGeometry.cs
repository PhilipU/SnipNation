namespace SnipNation.Core;

public readonly record struct PixelPoint(int X, int Y);

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromEdges(int left, int top, int right, int bottom) =>
        new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

    public static PixelRect FromCorners(PixelPoint first, PixelPoint second) =>
        FromEdges(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
            Math.Max(first.X, second.X), Math.Max(first.Y, second.Y));

    public bool Contains(PixelPoint point) =>
        point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    public PixelRect Intersect(PixelRect other) =>
        FromEdges(Math.Max(X, other.X), Math.Max(Y, other.Y),
            Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));

    public PixelRect Enclose(PixelRect other) =>
        FromEdges(Math.Min(X, other.X), Math.Min(Y, other.Y),
            Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));

    public PixelRect RelativeTo(PixelRect origin) => new(X - origin.X, Y - origin.Y, Width, Height);
}

public readonly record struct CropPiece(PixelRect Source, PixelRect Destination)
{
    public static CropPiece? ForDisplay(PixelRect display, PixelRect selection)
    {
        var overlap = display.Intersect(selection);
        return overlap.IsEmpty ? null : new(overlap.RelativeTo(display), overlap.RelativeTo(selection));
    }
}
