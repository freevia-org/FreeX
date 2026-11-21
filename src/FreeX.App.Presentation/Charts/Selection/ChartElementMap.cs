using Free.Shared.Drawing;

namespace FreeX.App.Presentation.Charts.Selection;

/// <summary>One selectable chart element and the pixel regions it occupies.</summary>
public sealed record ChartElementRegion(ChartElementRef Element, IReadOnlyList<ChartHitShape> Shapes)
{
    public bool Contains(LayoutPoint point)
    {
        foreach (var shape in Shapes)
        {
            if (shape.Contains(point))
                return true;
        }

        return false;
    }
}

/// <summary>
/// The hit-test map of a laid-out chart: every selectable element's regions in top-most-first order,
/// in the chart object's own pixel space (origin at its top-left). Built by
/// <see cref="ChartElementMapPlanner"/>; hosts only translate pointer positions into this space.
/// </summary>
public sealed class ChartElementMap
{
    private const int MaxPerShapeHighlights = 64;

    public ChartElementMap(double width, double height, IReadOnlyList<ChartElementRegion> regions)
    {
        Width = width;
        Height = height;
        Regions = regions;
    }

    /// <summary>Display names of the laid-out series, keyed by series index (for status text).</summary>
    public IReadOnlyDictionary<int, string?> SeriesNames { get; init; } = new Dictionary<int, string?>();

    public double Width { get; }

    public double Height { get; }

    /// <summary>Regions in hit-test priority order (first match wins; the chart area is always last).</summary>
    public IReadOnlyList<ChartElementRegion> Regions { get; }

    /// <summary>The topmost element under <paramref name="point"/>, or null when the point is outside the chart.</summary>
    public ChartElementRef? HitTest(LayoutPoint point)
    {
        if (point.X < 0 || point.Y < 0 || point.X > Width || point.Y > Height)
            return null;

        foreach (var region in Regions)
        {
            if (region.Contains(point))
                return region.Element;
        }

        return ChartElementRef.ChartArea;
    }

    public bool Contains(ChartElementRef element) =>
        element.IsChartArea || Regions.Any(r => r.Element == element);

    /// <summary>
    /// The rectangles to outline when <paramref name="element"/> is selected: the chart area is the
    /// whole object (hosts draw their normal chart handles); a series outlines each of its marks (bars,
    /// slices) up to a cap, otherwise the union of its marks; every other element outlines its bounds.
    /// </summary>
    public IReadOnlyList<LayoutRect> GetHighlightBounds(ChartElementRef element)
    {
        if (element.IsChartArea)
            return [new LayoutRect(0, 0, Width, Height)];

        var region = Regions.FirstOrDefault(r => r.Element == element);
        if (region is null || region.Shapes.Count == 0)
            return [];

        if (element.Kind == ChartElementKind.Series && region.Shapes.Count <= MaxPerShapeHighlights &&
            region.Shapes.All(s => s is RectHitShape or ArcHitShape))
        {
            return region.Shapes.Select(s => s.Bounds).ToList();
        }

        return [Union(region.Shapes.Select(s => s.Bounds))];
    }

    private static LayoutRect Union(IEnumerable<LayoutRect> rects)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        foreach (var r in rects)
        {
            if (r.Width <= 0 && r.Height <= 0)
                continue;
            minX = Math.Min(minX, r.Left);
            minY = Math.Min(minY, r.Top);
            maxX = Math.Max(maxX, r.Right);
            maxY = Math.Max(maxY, r.Bottom);
        }

        return double.IsInfinity(minX) ? new LayoutRect(0, 0, 0, 0) : new LayoutRect(minX, minY, maxX - minX, maxY - minY);
    }
}
