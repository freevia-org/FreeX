using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Drawing;
using FreeX.App.Presentation.Charts.Selection;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.UI;

/// <summary>
/// Dragging a selected chart element (title, legend, plot area, axis title, data label) to a new
/// position, and resizing the plot area by its grips, as in Excel. The geometry and the resulting
/// manual layout come from the shared <see cref="ChartElementDragPlanner"/>; this partial only turns
/// mouse input into that session, paints the ghost outline and raises
/// <see cref="ChartElementLayoutChanged"/> so the host executes one undoable layout command.
/// </summary>
public partial class GridView
{
    private ChartElementDragSession? _chartElementDrag;
    private Guid _chartElementDragChartId;

    /// <summary>Raised on release after a chart element was dragged/resized: the chart id and its new manual layout.</summary>
    public event Action<Guid, ChartLayoutOptions>? ChartElementLayoutChanged;

    internal bool IsChartElementDragActive => _chartElementDrag is not null;

    private bool TryBeginChartElementDrag(ChartModel chart, ChartElementMap map, ChartElementRef element, LayoutPoint local, ChartResizeHandle handle)
    {
        var session = ChartElementDragPlanner.Begin(chart, map, element, local, handle);
        if (session is null)
            return false;

        _chartElementDrag = session;
        _chartElementDragChartId = chart.Id;
        CaptureMouse();
        Cursor = handle == ChartResizeHandle.None ? Cursors.SizeAll : ResizeCursor(handle);
        return true;
    }

    private static Cursor ResizeCursor(ChartResizeHandle handle) => handle switch
    {
        ChartResizeHandle.Left or ChartResizeHandle.Right => Cursors.SizeWE,
        ChartResizeHandle.Top or ChartResizeHandle.Bottom => Cursors.SizeNS,
        ChartResizeHandle.TopLeft or ChartResizeHandle.BottomRight => Cursors.SizeNWSE,
        _ => Cursors.SizeNESW,
    };

    /// <summary>True when <paramref name="pos"/> is on a resize grip of the selected plot area.</summary>
    private ChartResizeHandle HitTestSelectedPlotHandle(ChartModel chart, ChartElementMap map, LayoutPoint local) =>
        SelectedChartElement is { } selected
            ? ChartElementDragPlanner.HitTestHandle(map, selected, local)
            : ChartResizeHandle.None;

    private LayoutPoint ToChartLocal(ChartModel chart, Point pos)
    {
        var rect = CreateChartRect(chart);
        if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
            return new LayoutPoint(0, 0);

        return new LayoutPoint(
            (pos.X - rect.Left) * chart.Width / rect.Width,
            (pos.Y - rect.Top) * chart.Height / rect.Height);
    }

    private bool TryUpdateChartElementDrag(Point pos)
    {
        if (_chartElementDrag is null || FindChart(_chartElementDragChartId) is not { } chart)
            return false;

        _chartElementDrag.Update(ToChartLocal(chart, pos));
        InvalidateVisual();
        return true;
    }

    private bool TryCommitChartElementDrag()
    {
        if (_chartElementDrag is not { } session)
            return false;

        var chartId = _chartElementDragChartId;
        ClearChartElementDrag();
        if (session.Complete() is { } options)
            ChartElementLayoutChanged?.Invoke(chartId, options);
        InvalidateVisual();
        return true;
    }

    private void ClearChartElementDrag()
    {
        _chartElementDrag = null;
        _chartElementDragChartId = Guid.Empty;
        Cursor = null;
        ReleaseMouseCapture();
    }

    private void CancelChartElementDrag()
    {
        if (_chartElementDrag is null)
            return;

        ClearChartElementDrag();
        InvalidateVisual();
    }

    /// <summary>The ghost outline of the element being dragged (in chart-local pixels), or null.</summary>
    private LayoutRect? GetChartElementDragPreview() =>
        _chartElementDrag is { HasMoved: true } session ? session.CurrentBounds : null;
}
