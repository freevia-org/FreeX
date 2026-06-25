namespace Free.Shared.Drawing;

/// <summary>
/// A point in layout space (origin top-left, y grows downward). Pure doubles, no platform types.
/// Ported from FreeX.App.Presentation.Charts.LayoutPoint.
/// </summary>
public readonly record struct LayoutPoint(double X, double Y);

/// <summary>
/// An axis-aligned rectangle in layout space, given by its top-left corner plus size.
/// Ported from FreeX.App.Presentation.Charts.LayoutRect.
/// </summary>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public LayoutPoint Center => new(X + (Width / 2), Y + (Height / 2));

    /// <summary>Builds a rectangle from two opposite corners, normalizing so size is non-negative.</summary>
    public static LayoutRect FromCorners(double x0, double y0, double x1, double y1)
    {
        var left = Math.Min(x0, x1);
        var top = Math.Min(y0, y1);
        return new LayoutRect(left, top, Math.Abs(x1 - x0), Math.Abs(y1 - y0));
    }
}
