namespace FreeX.App.Presentation.Charts;

/// <summary>
/// A point in the chart's pixel space (origin top-left, y grows downward — the convention the
/// desktop hosts' drawing surfaces use). Pure doubles, no platform types.
/// </summary>
public readonly record struct LayoutPoint(double X, double Y);

/// <summary>
/// An axis-aligned rectangle in pixel space, given by its top-left corner plus size. Negative
/// width/height are not produced by the layout engine; consumers may assume <c>Width &gt;= 0</c>
/// and <c>Height &gt;= 0</c>.
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

/// <summary>
/// The plot rectangle the chart body is laid out inside, in pixel space. This is the input region
/// the layout engine maps data into (after the title/legend/axis gutters have been reserved, when
/// the caller chooses to reserve them via the layout result's reported gutters).
/// </summary>
public readonly record struct PlotRect(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public LayoutRect ToRect() => new(X, Y, Width, Height);
}

/// <summary>
/// A pie/doughnut slice arc, described the way the desktop hosts draw pie wedges: a center, an
/// outer radius, an optional inner radius (for doughnuts), and a start/sweep angle in degrees.
/// Angles are measured clockwise from 12 o'clock (the Excel convention), matching the source
/// renderer's pie geometry.
/// </summary>
public readonly record struct LayoutArc(
    LayoutPoint Center,
    double OuterRadius,
    double InnerRadius,
    double StartAngleDegrees,
    double SweepAngleDegrees)
{
    public double EndAngleDegrees => StartAngleDegrees + SweepAngleDegrees;

    /// <summary>The mid-angle of the slice, useful for label placement and exploded offsets.</summary>
    public double MidAngleDegrees => StartAngleDegrees + (SweepAngleDegrees / 2);
}
