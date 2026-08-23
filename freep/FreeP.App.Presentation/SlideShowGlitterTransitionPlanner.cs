using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowGlitterTransitionPlan(
    int ColumnCount,
    int RowCount,
    double RevealWindow,
    int Seed,
    double JitterFactor);

/// <summary>
/// Shared deterministic sparkle-cell reveal geometry for the Glitter transition.
/// Each cell starts as a small star-like polygon and grows to its full cell,
/// avoiding host-specific random state while preserving the glitter silhouette.
/// </summary>
public static class SlideShowGlitterTransitionPlanner
{
    public const int DefaultColumnCount = 12;
    public const int DefaultRowCount = 8;
    public const double DefaultRevealWindow = 0.18;
    public const int DefaultSeed = 0x4F1A;
    public const double DefaultJitterFactor = 0.18;

    public static SlideShowGlitterTransitionPlan Plan(SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        return new(
            DefaultColumnCount,
            DefaultRowCount,
            DefaultRevealWindow,
            DefaultSeed,
            DefaultJitterFactor);
    }

    public static IReadOnlyList<SlideShowMaskPolygon> BuildPolygons(
        double width,
        double height,
        double progress,
        SlideShowGlitterTransitionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        width = Math.Max(0, width);
        height = Math.Max(0, height);
        progress = Math.Clamp(progress, 0, 1);
        if (width <= 0 || height <= 0 || progress <= 0)
            return Array.Empty<SlideShowMaskPolygon>();
        if (progress >= 1)
            return new[] { new SlideShowMaskPolygon(SlideShowTransitionGeometry.BuildRectangle(width, height)) };

        var columns = Math.Max(1, plan.ColumnCount);
        var rows = Math.Max(1, plan.RowCount);
        var cellWidth = width / columns;
        var cellHeight = height / rows;
        var revealWindow = Math.Max(0.01, plan.RevealWindow);
        var polygons = new List<SlideShowMaskPolygon>(columns * rows);

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var index = row * columns + column;
                var order = StableUnit(index, plan.Seed);
                var localProgress = Math.Clamp(
                    (progress - order) / revealWindow,
                    0,
                    1);
                if (localProgress <= 0)
                    continue;

                var eased = localProgress * localProgress * (3 - 2 * localProgress);
                var centerX = (column + 0.5) * cellWidth;
                var centerY = (row + 0.5) * cellHeight;
                var jitter = 1 - eased;
                centerX += (StableUnit(index + 101, plan.Seed) - 0.5)
                    * cellWidth * plan.JitterFactor * jitter;
                centerY += (StableUnit(index + 211, plan.Seed) - 0.5)
                    * cellHeight * plan.JitterFactor * jitter;

                var halfWidth = cellWidth * 0.5 * eased;
                var halfHeight = cellHeight * 0.5 * eased;
                polygons.Add(new(BuildSparkle(centerX, centerY, halfWidth, halfHeight)));
            }
        }

        return polygons;
    }

    private static IReadOnlyList<SlideShowMaskPoint> BuildSparkle(
        double centerX,
        double centerY,
        double halfWidth,
        double halfHeight)
    {
        return new[]
        {
            new SlideShowMaskPoint(centerX, centerY - halfHeight),
            new SlideShowMaskPoint(centerX + halfWidth, centerY - halfHeight),
            new SlideShowMaskPoint(centerX + halfWidth, centerY),
            new SlideShowMaskPoint(centerX + halfWidth, centerY + halfHeight),
            new SlideShowMaskPoint(centerX, centerY + halfHeight),
            new SlideShowMaskPoint(centerX - halfWidth, centerY + halfHeight),
            new SlideShowMaskPoint(centerX - halfWidth, centerY),
            new SlideShowMaskPoint(centerX - halfWidth, centerY - halfHeight)
        };
    }

    private static double StableUnit(int index, int seed)
    {
        var value = unchecked((uint)(index * 747796405 + seed * 2891336453));
        value ^= value >> 16;
        value *= 2246822519;
        value ^= value >> 13;
        return value / (double)uint.MaxValue;
    }

}
