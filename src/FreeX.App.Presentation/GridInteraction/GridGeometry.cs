namespace FreeX.App.Presentation.GridInteraction;

/// <summary>
/// A pointer position in the grid surface's pixel space (origin top-left, y grows downward — the
/// convention the desktop hosts' drawing surfaces use). Pure doubles, no platform types.
/// </summary>
public readonly record struct GridPoint(double X, double Y);

/// <summary>
/// An axis-aligned rectangle in grid pixel space, given by its left/top corner plus size. The grid
/// interaction layout never produces negative width/height, so consumers may assume
/// <c>Width &gt;= 0</c> and <c>Height &gt;= 0</c>.
/// </summary>
public readonly record struct GridRect(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;

    /// <summary>Builds a rectangle from two opposite corners.</summary>
    public static GridRect FromEdges(double left, double top, double right, double bottom) =>
        new(left, top, right - left, bottom - top);
}
