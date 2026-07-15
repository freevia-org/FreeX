using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

/// <summary>
/// Describes which edge-pairs of a bevel should receive the highlight or shade colour,
/// and the bevel geometry parameters. Used by SlideCanvas and unit tests.
/// </summary>
public sealed class BevelEdgeSet
{
    public bool Top    { get; set; }
    public bool Left   { get; set; }
    public bool Bottom { get; set; }
    public bool Right  { get; set; }
    public double BevelW { get; set; }
    public double BevelH { get; set; }
    public LayoutRect Bounds { get; set; }
}

/// <summary>
/// Pure geometry helper for bevel highlight/shade edge computation.
/// Extracted here (outside SlideCanvas) so unit tests can exercise the logic
/// without taking a WPF dependency.
/// </summary>
public static class BevelGeometryHelper
{
    /// <summary>
    /// Converts a DrawingML bevel surface dimension to its conservative 2-D
    /// raster footprint. PowerPoint shades only part of the declared bevel face
    /// in a front-on rendering, so using the raw dimension overstates the edge.
    /// </summary>
    public static (double WidthDip, double HeightDip) GetRenderDimensions(
        LayoutRect bounds,
        double bevelWidthDip,
        double bevelHeightDip)
    {
        const double visibleSurfaceFraction = 0.4;
        return (
            Math.Min(bevelWidthDip * visibleSurfaceFraction, bounds.Width / 3),
            Math.Min(bevelHeightDip * visibleSurfaceFraction, bounds.Height / 3));
    }

    /// <summary>
    /// Computes which edges of a rectangular shape should be rendered as highlight
    /// or shade for a bevel effect, given the light direction.
    ///
    /// <paramref name="lightDirDeg"/> is the angle (degrees) that identifies which face
    /// of the shape is illuminated, using the same mapping as ResolveLightDir:
    ///   270° = top face lit ("t"),  315° = top-left,  90° = bottom face lit ("b"),
    ///   0° = left face lit,  180° = right face lit, etc.
    /// Pass -1 to use the default (315° = top-left illumination, PowerPoint default).
    ///
    /// Returns (<c>highlight</c>, <c>shade</c>) edge sets. Each set has Top/Left/Bottom/Right
    /// flags indicating which trapezoidal wedge should use that colour.
    /// </summary>
    public static (BevelEdgeSet highlight, BevelEdgeSet shade) ComputeBevelRegions(
        LayoutRect bounds, ResolvedBevel bevel, double lightDirDeg)
    {
        // Default: top-left illumination (PowerPoint default)
        double ld = lightDirDeg < 0 ? 315 : lightDirDeg;

        // ResolveLightDir encodes "light FROM direction" as: 270=above, 90=below, 0=left, 180=right.
        // Decompose into which shape faces are illuminated using:
        //   srcLx = -cos(ld°)  → <0 = light from left, >0 = from right
        //   srcLy =  sin(ld°)  → <0 = light from above, >0 = from below  (y-down screen coords)
        // Verified: 270(top)→srcLx=0,srcLy=-1→top✓; 90(bottom)→srcLy=+1→bottom✓;
        //           0(left)→srcLx=-1→left✓; 180(right)→srcLx=+1→right✓;
        //           315(tl)→srcLx=-0.707,srcLy=-0.707→top+left✓.
        double lRad = ld * Math.PI / 180.0;
        double srcLx = -Math.Cos(lRad);
        double srcLy =  Math.Sin(lRad);

        bool topHighlight    = srcLy < 0;
        bool bottomHighlight = srcLy > 0;
        bool leftHighlight   = srcLx < 0;
        bool rightHighlight  = srcLx > 0;

        double bw = bevel.WidthDip;
        double bh = bevel.HeightDip;

        var highlight = new BevelEdgeSet
        {
            Top    = topHighlight,
            Left   = leftHighlight,
            Bottom = bottomHighlight,
            Right  = rightHighlight,
            BevelW = bw,
            BevelH = bh,
            Bounds = bounds
        };
        var shade = new BevelEdgeSet
        {
            Top    = !topHighlight,
            Left   = !leftHighlight,
            Bottom = !bottomHighlight,
            Right  = !rightHighlight,
            BevelW = bw,
            BevelH = bh,
            Bounds = bounds
        };

        return (highlight, shade);
    }
}
