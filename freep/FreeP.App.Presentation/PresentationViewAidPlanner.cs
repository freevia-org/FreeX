namespace FreeP.App.Compositor;

/// <summary>One screen-space line in the non-interactive View canvas aids.</summary>
public readonly record struct PresentationViewAidLine(
    double StartX,
    double StartY,
    double EndX,
    double EndY);

/// <summary>Screen-space grid and center-guide geometry for the View ribbon.</summary>
public readonly record struct PresentationViewAidPlan(
    IReadOnlyList<PresentationViewAidLine> Gridlines,
    IReadOnlyList<PresentationViewAidLine> Guides)
{
    public static PresentationViewAidPlan Empty { get; } = new(
        Array.Empty<PresentationViewAidLine>(),
        Array.Empty<PresentationViewAidLine>());
}

/// <summary>
/// Produces renderer-neutral View gridline and guide geometry from the live slide transform.
/// The visible grid follows the existing PowerPoint-compatible snapping pitch while increasing
/// the visual interval at low zoom so the canvas never becomes a dense raster of sub-pixel lines.
/// </summary>
public static class PresentationViewAidPlanner
{
    private const double MinimumVisibleGridInterval = 8;
    private const int MaximumGridLinesPerAxis = 200;

    public static PresentationViewAidPlan Build(
        SlideTransformCore transform,
        PresentationViewShowState state)
    {
        if (!IsUsable(transform))
            return PresentationViewAidPlan.Empty;

        var gridlines = state.ShowGridlines
            ? BuildGridlines(transform)
            : Array.Empty<PresentationViewAidLine>();
        var guides = state.ShowGuides
            ? BuildCenterGuides(transform)
            : Array.Empty<PresentationViewAidLine>();
        return new PresentationViewAidPlan(gridlines, guides);
    }

    private static IReadOnlyList<PresentationViewAidLine> BuildGridlines(SlideTransformCore transform)
    {
        var intervalDip = SnapEngine.DefaultGridPitchDip;
        var minimumIntervalDip = MinimumVisibleGridInterval / transform.Scale;
        var maximumDensityIntervalDip = Math.Max(transform.SlideWidthDip, transform.SlideHeightDip) /
            MaximumGridLinesPerAxis;
        intervalDip *= Math.Max(1, Math.Ceiling(Math.Max(minimumIntervalDip, maximumDensityIntervalDip) / intervalDip));

        var verticalCount = Math.Max(0, (int)Math.Floor(transform.SlideWidthDip / intervalDip) - 1);
        var horizontalCount = Math.Max(0, (int)Math.Floor(transform.SlideHeightDip / intervalDip) - 1);
        if (verticalCount == 0 && horizontalCount == 0)
            return Array.Empty<PresentationViewAidLine>();

        var lines = new PresentationViewAidLine[verticalCount + horizontalCount];
        var right = transform.OffsetX + transform.SlideWidthDip * transform.Scale;
        var bottom = transform.OffsetY + transform.SlideHeightDip * transform.Scale;
        var index = 0;
        for (var column = 1; column <= verticalCount; column++)
        {
            var x = transform.OffsetX + column * intervalDip * transform.Scale;
            lines[index++] = new PresentationViewAidLine(x, transform.OffsetY, x, bottom);
        }

        for (var row = 1; row <= horizontalCount; row++)
        {
            var y = transform.OffsetY + row * intervalDip * transform.Scale;
            lines[index++] = new PresentationViewAidLine(transform.OffsetX, y, right, y);
        }

        return lines;
    }

    private static IReadOnlyList<PresentationViewAidLine> BuildCenterGuides(SlideTransformCore transform)
    {
        var right = transform.OffsetX + transform.SlideWidthDip * transform.Scale;
        var bottom = transform.OffsetY + transform.SlideHeightDip * transform.Scale;
        var centerX = (transform.OffsetX + right) / 2;
        var centerY = (transform.OffsetY + bottom) / 2;
        return
        [
            new PresentationViewAidLine(centerX, transform.OffsetY, centerX, bottom),
            new PresentationViewAidLine(transform.OffsetX, centerY, right, centerY),
        ];
    }

    private static bool IsUsable(SlideTransformCore transform) =>
        double.IsFinite(transform.Scale) &&
        double.IsFinite(transform.OffsetX) &&
        double.IsFinite(transform.OffsetY) &&
        double.IsFinite(transform.SlideWidthDip) &&
        double.IsFinite(transform.SlideHeightDip) &&
        transform.Scale > 0 &&
        transform.SlideWidthDip > 0 &&
        transform.SlideHeightDip > 0;
}
