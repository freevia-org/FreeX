namespace FreeP.App.Compositor;

public readonly record struct SlideShowSegmentedFrontEdges(double Leading, double Trailing);

/// <summary>Portable geometry primitives shared by slideshow transition planners.</summary>
public static class SlideShowTransitionGeometry
{
    public static double SmoothStep(double value) => value * value * (3 - 2 * value);

    public static IReadOnlyList<SlideShowMaskPoint> BuildRectangle(double width, double height) =>
        new[]
        {
            new SlideShowMaskPoint(0, 0),
            new SlideShowMaskPoint(width, 0),
            new SlideShowMaskPoint(width, height),
            new SlideShowMaskPoint(0, height)
        };

    public static IReadOnlyList<SlideShowMaskPolygon> BuildSegmentedFront(
        double width,
        double height,
        int segmentCount,
        bool horizontalAxis,
        bool reverse,
        Func<double, SlideShowSegmentedFrontEdges?> resolveEdges)
    {
        ArgumentNullException.ThrowIfNull(resolveEdges);

        var segments = Math.Max(2, segmentCount);
        var polygons = new List<SlideShowMaskPolygon>(segments);
        var segmentExtent = (horizontalAxis ? height : width) / segments;
        for (var segment = 0; segment < segments; segment++)
        {
            var normalized = (segment + 0.5) / segments;
            var edges = resolveEdges(normalized);
            if (edges is null)
                continue;

            var start = segment * segmentExtent;
            var end = (segment + 1) * segmentExtent;
            polygons.Add(new(horizontalAxis
                ? BuildHorizontalSegment(width, start, end, edges.Value, reverse)
                : BuildVerticalSegment(height, start, end, edges.Value, reverse)));
        }

        return polygons;
    }

    private static IReadOnlyList<SlideShowMaskPoint> BuildHorizontalSegment(
        double width,
        double y0,
        double y1,
        SlideShowSegmentedFrontEdges edges,
        bool reverse) =>
        reverse
            ? new[]
            {
                new SlideShowMaskPoint(edges.Leading, y0),
                new SlideShowMaskPoint(width, y0),
                new SlideShowMaskPoint(width, y1),
                new SlideShowMaskPoint(edges.Trailing, y1)
            }
            : new[]
            {
                new SlideShowMaskPoint(0, y0),
                new SlideShowMaskPoint(edges.Leading, y0),
                new SlideShowMaskPoint(edges.Trailing, y1),
                new SlideShowMaskPoint(0, y1)
            };

    private static IReadOnlyList<SlideShowMaskPoint> BuildVerticalSegment(
        double height,
        double x0,
        double x1,
        SlideShowSegmentedFrontEdges edges,
        bool reverse) =>
        reverse
            ? new[]
            {
                new SlideShowMaskPoint(x0, edges.Leading),
                new SlideShowMaskPoint(x1, edges.Leading),
                new SlideShowMaskPoint(x1, height),
                new SlideShowMaskPoint(x0, edges.Trailing)
            }
            : new[]
            {
                new SlideShowMaskPoint(x0, 0),
                new SlideShowMaskPoint(x1, 0),
                new SlideShowMaskPoint(x1, edges.Trailing),
                new SlideShowMaskPoint(x0, edges.Leading)
            };
}
