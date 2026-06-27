using Free.Shared.Drawing;

namespace FreeX.App.Presentation.Charts;

/// <summary>
/// The plot rectangle the chart body is laid out inside, in pixel space. This is the input region
/// the layout engine maps data into after title, legend, and axis gutters have been reserved.
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
/// outer radius, an optional inner radius, and a start/sweep angle in degrees.
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
