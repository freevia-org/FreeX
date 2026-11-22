using Free.Shared.Drawing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Selection;

/// <summary>The grips the plot area can be resized by (Excel shows eight on a selected plot area).</summary>
public enum ChartResizeHandle
{
    None = 0,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>
/// Plans dragging a selected chart element to a new position (and resizing the plot area), the way
/// Excel does: the title, legend, plot area, axis titles and an individual data label can be dragged,
/// the plot area additionally resized by its handles. The result is a <see cref="ChartLayoutOptions"/>
/// carrying the manual layout, executed through <see cref="SetChartLayoutCommand"/> (one undo step).
/// Pure geometry in the chart object's own pixel space so WPF and Avalonia behave identically.
/// </summary>
public static class ChartElementDragPlanner
{
    /// <summary>Smallest drag (chart pixels) that counts as a move rather than a click.</summary>
    public const double MoveThreshold = 3;

    /// <summary>Smallest width/height a resized plot area may take.</summary>
    public const double MinPlotSize = 24;

    private const double HandleHitSlop = 5;

    /// <summary>True for the elements Excel lets the user drag.</summary>
    public static bool IsDraggable(ChartElementRef element) =>
        element.Kind is ChartElementKind.ChartTitle
            or ChartElementKind.Legend
            or ChartElementKind.PlotArea
            or ChartElementKind.CategoryAxisTitle
            or ChartElementKind.ValueAxisTitle
            or ChartElementKind.DataLabels;

    /// <summary>True when the element can also be resized by handles (the plot area).</summary>
    public static bool CanResize(ChartElementRef element) => element.Kind == ChartElementKind.PlotArea;

    /// <summary>The resize grip of the selected plot area under <paramref name="point"/>, or None.</summary>
    public static ChartResizeHandle HitTestHandle(ChartElementMap map, ChartElementRef element, LayoutPoint point)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!CanResize(element) || map.GetHighlightBounds(element) is not { Count: 1 } bounds)
            return ChartResizeHandle.None;

        var b = bounds[0];
        var cx = b.Left + (b.Width / 2);
        var cy = b.Top + (b.Height / 2);
        var candidates = new (ChartResizeHandle Handle, double X, double Y)[]
        {
            (ChartResizeHandle.TopLeft, b.Left, b.Top),
            (ChartResizeHandle.Top, cx, b.Top),
            (ChartResizeHandle.TopRight, b.Right, b.Top),
            (ChartResizeHandle.Right, b.Right, cy),
            (ChartResizeHandle.BottomRight, b.Right, b.Bottom),
            (ChartResizeHandle.Bottom, cx, b.Bottom),
            (ChartResizeHandle.BottomLeft, b.Left, b.Bottom),
            (ChartResizeHandle.Left, b.Left, cy),
        };

        foreach (var (handle, x, y) in candidates)
        {
            if (Math.Abs(point.X - x) <= HandleHitSlop && Math.Abs(point.Y - y) <= HandleHitSlop)
                return handle;
        }

        return ChartResizeHandle.None;
    }

    /// <summary>
    /// Starts a drag of <paramref name="element"/> at <paramref name="press"/>. Returns null when the
    /// element cannot be dragged (or, for data labels, no label lies under the pointer).
    /// </summary>
    public static ChartElementDragSession? Begin(
        ChartModel chart,
        ChartElementMap map,
        ChartElementRef element,
        LayoutPoint press,
        ChartResizeHandle handle = ChartResizeHandle.None)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(map);
        if (!IsDraggable(element) || !(map.Width > 0) || !(map.Height > 0))
            return null;

        if (element.Kind == ChartElementKind.DataLabels)
        {
            foreach (var box in map.DataLabelBoxes)
            {
                if (box.SeriesIndex == element.SeriesIndex &&
                    press.X >= box.Bounds.Left && press.X <= box.Bounds.Right &&
                    press.Y >= box.Bounds.Top && press.Y <= box.Bounds.Bottom)
                {
                    return new ChartElementDragSession(chart, map.Width, map.Height, element, press, box.Bounds, ChartResizeHandle.None, box.PointIndex);
                }
            }

            return null;
        }

        if (map.GetHighlightBounds(element) is not { Count: 1 } bounds || bounds[0].Width <= 0 || bounds[0].Height <= 0)
            return null;

        return new ChartElementDragSession(
            chart, map.Width, map.Height, element, press, bounds[0],
            CanResize(element) ? handle : ChartResizeHandle.None, -1);
    }
}

/// <summary>One in-progress drag/resize of a chart element. Update with the pointer, then Complete.</summary>
public sealed class ChartElementDragSession
{
    private readonly ChartModel _chart;
    private readonly double _width;
    private readonly double _height;
    private readonly LayoutPoint _press;
    private readonly ChartResizeHandle _handle;
    private readonly int _pointIndex;

    internal ChartElementDragSession(
        ChartModel chart,
        double width,
        double height,
        ChartElementRef element,
        LayoutPoint press,
        LayoutRect startBounds,
        ChartResizeHandle handle,
        int pointIndex)
    {
        _chart = chart;
        _width = width;
        _height = height;
        _press = press;
        _handle = handle;
        _pointIndex = pointIndex;
        Element = element;
        StartBounds = startBounds;
        CurrentBounds = startBounds;
    }

    public ChartElementRef Element { get; }

    public LayoutRect StartBounds { get; }

    /// <summary>Where the element would land if released now (draw this as the drag preview).</summary>
    public LayoutRect CurrentBounds { get; private set; }

    public bool IsResize => _handle != ChartResizeHandle.None;

    /// <summary>True once the pointer has travelled far enough to count as a drag rather than a click.</summary>
    public bool HasMoved { get; private set; }

    public LayoutRect Update(LayoutPoint pointer)
    {
        var dx = pointer.X - _press.X;
        var dy = pointer.Y - _press.Y;
        if (!HasMoved &&
            Math.Abs(dx) < ChartElementDragPlanner.MoveThreshold &&
            Math.Abs(dy) < ChartElementDragPlanner.MoveThreshold)
        {
            return CurrentBounds;
        }

        HasMoved = true;
        CurrentBounds = IsResize
            ? Resize(dx, dy)
            : ChartManualLayoutGeometry.ClampInside(
                new LayoutRect(StartBounds.Left + dx, StartBounds.Top + dy, StartBounds.Width, StartBounds.Height),
                _width,
                _height);
        return CurrentBounds;
    }

    private LayoutRect Resize(double dx, double dy)
    {
        var left = StartBounds.Left;
        var top = StartBounds.Top;
        var right = StartBounds.Right;
        var bottom = StartBounds.Bottom;
        const double min = ChartElementDragPlanner.MinPlotSize;

        if (_handle is ChartResizeHandle.TopLeft or ChartResizeHandle.Left or ChartResizeHandle.BottomLeft)
            left = Math.Clamp(left + dx, 0, right - min);
        if (_handle is ChartResizeHandle.TopRight or ChartResizeHandle.Right or ChartResizeHandle.BottomRight)
            right = Math.Clamp(right + dx, left + min, _width);
        if (_handle is ChartResizeHandle.TopLeft or ChartResizeHandle.Top or ChartResizeHandle.TopRight)
            top = Math.Clamp(top + dy, 0, bottom - min);
        if (_handle is ChartResizeHandle.BottomLeft or ChartResizeHandle.Bottom or ChartResizeHandle.BottomRight)
            bottom = Math.Clamp(bottom + dy, top + min, _height);

        return new LayoutRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// The layout change that persists the drag, or null when the pointer never moved. Execute it with
    /// <see cref="SetChartLayoutCommand"/> for a single undo step.
    /// </summary>
    public ChartLayoutOptions? Complete()
    {
        if (!HasMoved || CurrentBounds == StartBounds)
            return null;

        var origin = new LayoutPoint(CurrentBounds.Left, CurrentBounds.Top);
        switch (Element.Kind)
        {
            case ChartElementKind.ChartTitle:
                return new ChartLayoutOptions { TitleLayout = ChartManualLayoutGeometry.FromOrigin(origin, _width, _height) };
            case ChartElementKind.Legend:
                return new ChartLayoutOptions { LegendLayout = ChartManualLayoutGeometry.FromRect(CurrentBounds, _width, _height) };
            case ChartElementKind.PlotArea:
                return new ChartLayoutOptions { PlotAreaLayout = ChartManualLayoutGeometry.FromPlotArea(CurrentBounds, _width, _height) };
            case ChartElementKind.CategoryAxisTitle:
            case ChartElementKind.ValueAxisTitle:
                var layout = ChartManualLayoutGeometry.FromOrigin(origin, _width, _height);
                return ChartElementCommandPlanner.IsHorizontalAxis(_chart, Element.Kind)
                    ? new ChartLayoutOptions { XAxisTitleLayout = layout }
                    : new ChartLayoutOptions { YAxisTitleLayout = layout };
            case ChartElementKind.DataLabels:
                return PlanDataLabel();
            default:
                return null;
        }
    }

    private ChartLayoutOptions PlanDataLabel()
    {
        var existing = _chart.PointDataLabelFormats.FirstOrDefault(
            f => f.SeriesIndex == Element.SeriesIndex && f.PointIndex == _pointIndex);
        var dx = CurrentBounds.Left - StartBounds.Left;
        var dy = CurrentBounds.Top - StartBounds.Top;

        ChartManualLayoutModel layout;
        if (existing?.Layout is { } prior &&
            string.Equals(prior.XMode, ChartManualLayoutGeometry.EdgeMode, StringComparison.OrdinalIgnoreCase))
        {
            layout = ChartManualLayoutGeometry.FromOrigin(new LayoutPoint(CurrentBounds.Left, CurrentBounds.Top), _width, _height);
        }
        else
        {
            // Factor layouts are offsets from the automatic position, so a re-drag adds to the old offset.
            var priorX = existing?.Layout?.X ?? 0;
            var priorY = existing?.Layout?.Y ?? 0;
            layout = ChartManualLayoutGeometry.FromOffset((priorX * _width) + dx, (priorY * _height) + dy, _width, _height);
        }

        var formats = _chart.PointDataLabelFormats.ToList();
        var index = existing is null ? -1 : formats.IndexOf(existing);
        var updated = (existing ?? new ChartPointDataLabelFormat(Element.SeriesIndex, _pointIndex)) with { Layout = layout };
        if (index >= 0)
            formats[index] = updated;
        else
            formats.Add(updated);

        return new ChartLayoutOptions(PointDataLabelFormats: formats);
    }
}
