using Free.Shared.Drawing;
using FreeX.App.Presentation.Charts;

namespace FreeX.App.Presentation.Charts.Selection;

/// <summary>Arrow keys that move the chart element selection.</summary>
public enum ChartNavigationKey
{
    Up,
    Down,
    Left,
    Right,
}

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

    /// <summary>Legend entry labels keyed by entry index (series index; point index for pie charts).</summary>
    public IReadOnlyDictionary<int, string?> LegendEntryLabels { get; init; } = new Dictionary<int, string?>();

    /// <summary>Every laid-out data label with its series/point identity (drives dragging one label).</summary>
    public IReadOnlyList<DataLabelBox> DataLabelBoxes { get; init; } = [];

    /// <summary>
    /// Elements reachable only by a second click inside an already-selected parent (a data point inside
    /// its series, a legend entry inside the legend). They never take part in <see cref="HitTest"/>;
    /// <see cref="ResolveClick"/> consults them.
    /// </summary>
    public IReadOnlyList<ChartElementRegion> NestedRegions { get; init; } = [];

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

    /// <summary>
    /// True when a double-click at <paramref name="point"/> lands on the already-selected element or on
    /// the parent a selected data point / legend entry lives in (Format Selection should open).
    /// </summary>
    public bool IsDoubleClickOnSelection(LayoutPoint point, ChartElementRef selected)
    {
        if (selected.IsChartArea || HitTest(point) is not { } plain || plain.IsChartArea)
            return false;

        return plain == selected ||
               (selected.Kind == ChartElementKind.DataPoint && plain.Kind == ChartElementKind.Series && plain.SeriesIndex == selected.SeriesIndex) ||
               (selected.Kind == ChartElementKind.LegendEntry && plain.Kind == ChartElementKind.Legend);
    }

    /// <summary>The series name (or legend-entry label) to show in status text for <paramref name="element"/>.</summary>
    public string? GetDisplayName(ChartElementRef element)
    {
        string? name = null;
        if (element.Kind == ChartElementKind.LegendEntry)
            LegendEntryLabels.TryGetValue(element.SeriesIndex, out name);
        else if (element.SeriesIndex >= 0)
            SeriesNames.TryGetValue(element.SeriesIndex, out name);
        return name;
    }

    public bool Contains(ChartElementRef element) =>
        element.IsChartArea || Regions.Any(r => r.Element == element) || NestedRegions.Any(r => r.Element == element);

    /// <summary>
    /// What a click at <paramref name="point"/> selects given the current selection, following Excel: a
    /// click on a series that is already selected (or one of its points) narrows to the data point under
    /// the pointer, and a click on a legend that is already selected (or one of its entries) narrows to
    /// the legend entry under the pointer. Everything else is the plain <see cref="HitTest"/> result.
    /// </summary>
    public ChartElementRef? ResolveClick(LayoutPoint point, ChartElementRef? current)
    {
        var hit = HitTest(point);
        if (hit is not { } element || current is not { } selected)
            return hit;

        ChartElementKind? nestedKind = null;
        if (element.Kind == ChartElementKind.Series &&
            selected.Kind is ChartElementKind.Series or ChartElementKind.DataPoint &&
            selected.SeriesIndex == element.SeriesIndex)
        {
            nestedKind = ChartElementKind.DataPoint;
        }
        else if (element.Kind == ChartElementKind.Legend &&
                 selected.Kind is ChartElementKind.Legend or ChartElementKind.LegendEntry)
        {
            nestedKind = ChartElementKind.LegendEntry;
        }

        if (nestedKind is { } kind)
        {
            foreach (var region in NestedRegions)
            {
                if (region.Element.Kind == kind &&
                    (kind == ChartElementKind.LegendEntry || region.Element.SeriesIndex == element.SeriesIndex) &&
                    region.Contains(point))
                {
                    return region.Element;
                }
            }
        }

        return element;
    }

    /// <summary>
    /// The element Excel's arrow keys move to from <paramref name="current"/>: Up/Down step through the
    /// chart's top-level elements in <see cref="Regions"/> order (chart area first), Left/Right step to
    /// the neighbouring data point of the selected series or legend entry of the selected legend.
    /// Returns <paramref name="current"/> unchanged when there is nowhere to go.
    /// </summary>
    public ChartElementRef Navigate(ChartElementRef current, ChartNavigationKey key)
    {
        if (key is ChartNavigationKey.Left or ChartNavigationKey.Right)
        {
            var step = key == ChartNavigationKey.Right ? 1 : -1;
            ChartElementKind? kind = current.Kind switch
            {
                ChartElementKind.Series or ChartElementKind.DataPoint => ChartElementKind.DataPoint,
                ChartElementKind.Legend or ChartElementKind.LegendEntry => ChartElementKind.LegendEntry,
                _ => null,
            };
            if (kind is not { } nested)
                return current;

            var siblings = NestedRegions
                .Select(r => r.Element)
                .Where(e => e.Kind == nested && (nested == ChartElementKind.LegendEntry || e.SeriesIndex == current.SeriesIndex))
                .OrderBy(e => nested == ChartElementKind.LegendEntry ? e.SeriesIndex : e.PointIndex)
                .ToList();
            if (siblings.Count == 0)
                return current;

            var at = siblings.IndexOf(current);
            if (at < 0)
                return step > 0 ? siblings[0] : siblings[^1];
            return siblings[Math.Clamp(at + step, 0, siblings.Count - 1)];
        }

        // Up/Down: a point or entry first steps back out to its parent, then cycles the top-level order.
        var parentOf = current.Kind switch
        {
            ChartElementKind.DataPoint => ChartElementRef.ForSeries(current.SeriesIndex),
            ChartElementKind.LegendEntry => new ChartElementRef(ChartElementKind.Legend),
            _ => current,
        };
        var order = new List<ChartElementRef> { ChartElementRef.ChartArea };
        order.AddRange(Regions.Select(r => r.Element).Distinct().Reverse());
        var index = order.IndexOf(parentOf);
        if (index < 0)
            return current;
        var next = key == ChartNavigationKey.Down ? index + 1 : index - 1;
        return order[(next + order.Count) % order.Count];
    }

    /// <summary>
    /// The rectangles to outline when <paramref name="element"/> is selected: the chart area is the
    /// whole object (hosts draw their normal chart handles); a series outlines each of its marks (bars,
    /// slices) up to a cap, otherwise the union of its marks; every other element outlines its bounds.
    /// </summary>
    public IReadOnlyList<LayoutRect> GetHighlightBounds(ChartElementRef element)
    {
        if (element.IsChartArea)
            return [new LayoutRect(0, 0, Width, Height)];

        var region = Regions.FirstOrDefault(r => r.Element == element) ??
                     NestedRegions.FirstOrDefault(r => r.Element == element);
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
