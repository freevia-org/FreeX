using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowFractureTransitionPlan(
    int RowCount,
    int ColumnCount,
    bool Reverse,
    double RevealWindow,
    double GapFactor);

/// <summary>
/// Shared shard-grid geometry for the Fracture transition.
/// Fragments open in a deterministic center-first order and retain a small
/// inter-fragment gap until each shard has grown to its full cell.
/// </summary>
public static class SlideShowFractureTransitionPlanner
{
    public const int DefaultRowCount = 4;
    public const int DefaultColumnCount = 6;
    public const double DefaultRevealWindow = 0.46;
    public const double DefaultGapFactor = 0.16;

    public static SlideShowFractureTransitionPlan Plan(SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var reverse = transition.Direction is
            TransitionDirection.Left or
            TransitionDirection.Up or
            TransitionDirection.LeftUp or
            TransitionDirection.LeftDown;

        return new(
            DefaultRowCount,
            DefaultColumnCount,
            reverse,
            DefaultRevealWindow,
            DefaultGapFactor);
    }

    public static IReadOnlyList<SlideShowMaskPolygon> BuildPolygons(
        double width,
        double height,
        double progress,
        SlideShowFractureTransitionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        width = Math.Max(0, width);
        height = Math.Max(0, height);
        progress = Math.Clamp(progress, 0, 1);
        if (width <= 0 || height <= 0 || progress <= 0)
            return Array.Empty<SlideShowMaskPolygon>();
        if (progress >= 1)
            return new[] { new SlideShowMaskPolygon(SlideShowTransitionGeometry.BuildRectangle(width, height)) };

        var rows = Math.Max(2, plan.RowCount);
        var columns = Math.Max(2, plan.ColumnCount);
        var revealWindow = Math.Clamp(plan.RevealWindow, 0.10, 0.95);
        var gapFactor = Math.Clamp(plan.GapFactor, 0, 0.40);
        var cellWidth = width / columns;
        var cellHeight = height / rows;
        var polygons = new List<SlideShowMaskPolygon>(rows * columns);

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var x = (column + 0.5) / columns;
                var y = (row + 0.5) / rows;
                var distance = Math.Sqrt(
                    Math.Pow((x - 0.5) * 2, 2) +
                    Math.Pow((y - 0.5) * 2, 2));
                var order = Math.Clamp(distance / Math.Sqrt(2), 0, 1);
                if (plan.Reverse)
                    order = 1 - order;

                var local = SlideShowTransitionGeometry.SmoothStep(Math.Clamp(
                    (progress - order * (1 - revealWindow)) / revealWindow,
                    0,
                    1));
                if (local <= 0)
                    continue;

                var inset = Math.Min(cellWidth, cellHeight) * gapFactor
                    * (1 - local) * 0.5;
                var x0 = column * cellWidth + inset;
                var y0 = row * cellHeight + inset;
                var x1 = (column + 1) * cellWidth - inset;
                var y1 = (row + 1) * cellHeight - inset;
                polygons.Add(new(new[]
                {
                    new SlideShowMaskPoint(x0, y0),
                    new SlideShowMaskPoint(x1, y0),
                    new SlideShowMaskPoint(x1, y1),
                    new SlideShowMaskPoint(x0, y1)
                }));
            }
        }

        return polygons;
    }


}
