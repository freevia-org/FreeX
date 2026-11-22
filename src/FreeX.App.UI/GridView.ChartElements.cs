using System.Windows;
using System.Windows.Media;
using Free.Shared.Drawing;
using FreeX.App.Presentation.Charts.Selection;
using FreeX.Core.Model;

namespace FreeX.App.UI;

/// <summary>
/// In-chart element selection for the WPF grid: a click on a chart's title, legend, axes, axis titles,
/// gridlines, plot area, data labels or series selects that element (highlighted over the chart bitmap)
/// instead of only the whole chart object. The hit geometry comes from
/// <see cref="ChartRenderer.BuildElementMap"/>; the host owns Delete and Format Selection.
/// </summary>
public partial class GridView
{
    private const int ChartElementMapCacheLimit = 16;
    private const double ChartElementHandleSize = 6;
    private const double ChartElementHighlightInflate = 2;

    private static readonly Pen ChartElementHighlightPen = CreateChartElementHighlightPen();

    private readonly Dictionary<(ChartModel Chart, long Fingerprint, double Scale), ChartElementMap> _chartElementMapCache = new();

    private ChartElementRef? _selectedChartElement;
    private Guid _selectedChartElementChartId;
    private string? _selectedChartElementSeriesName;

    /// <summary>Raised when the selected chart element changes (the chart area itself when the inner selection is cleared).</summary>
    public event Action<Guid, ChartElementRef, string?>? ChartElementSelected;

    /// <summary>Raised when the user double-clicks the already-selected inner chart element (Format Selection).</summary>
    public event Action<Guid, ChartElementRef>? ChartElementFormatRequested;

    /// <summary>The selected inner element of the selected chart, or null when the whole chart (or nothing) is selected.</summary>
    public ChartElementRef? SelectedChartElement =>
        SelectedObjectKind == ObjectKind.Chart &&
        SelectedObjectId != Guid.Empty &&
        SelectedObjectId == _selectedChartElementChartId
            ? _selectedChartElement
            : null;

    /// <summary>The plotted series' name for a selected series / data-label element (status text).</summary>
    public string? SelectedChartElementSeriesName => SelectedChartElement is null ? null : _selectedChartElementSeriesName;

    /// <summary>Drops the inner-element selection (the whole chart stays selected).</summary>
    public void ClearSelectedChartElement()
    {
        if (_selectedChartElement is null)
            return;

        _selectedChartElement = null;
        _selectedChartElementSeriesName = null;
        InvalidateVisual();

        // Lets an open Format pane fall back to the Chart Area (the whole chart is selected again).
        ChartElementSelected?.Invoke(_selectedChartElementChartId, ChartElementRef.ChartArea, null);
    }

    private void OnSelectedObjectIdChangedForChartElement(Guid newId)
    {
        if (_selectedChartElement is not null && newId != _selectedChartElementChartId)
        {
            _selectedChartElement = null;
            _selectedChartElementSeriesName = null;
        }
    }

    /// <summary>
    /// Resolves the element under <paramref name="pos"/> on the currently selected chart and, when it is
    /// an inner element, selects it (or requests Format Selection on a double-click of the already
    /// selected one). Returns true when the press was consumed; false when it hit the chart area (any
    /// inner selection is cleared) so the caller proceeds with the normal chart move/resize drag.
    /// </summary>
    private bool TrySelectChartElementAt(Point pos, bool isDoubleClick)
    {
        if (Viewport is null || SelectedObjectKind != ObjectKind.Chart || FindChart(SelectedObjectId) is not { } chart)
            return false;

        var rect = CreateChartRect(chart);
        if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0 || chart.Width <= 0 || chart.Height <= 0)
            return false;

        var map = GetChartElementMap(chart);
        var local = new LayoutPoint(
            (pos.X - rect.Left) * chart.Width / rect.Width,
            (pos.Y - rect.Top) * chart.Height / rect.Height);
        if (isDoubleClick && SelectedChartElement is { } current && map.IsDoubleClickOnSelection(local, current))
        {
            ChartElementFormatRequested?.Invoke(chart.Id, current);
            return true;
        }

        // A press on a grip of the selected plot area starts a resize instead of re-selecting.
        if (!isDoubleClick &&
            SelectedChartElement is { } selectedForResize &&
            ChartElementDragPlanner.CanResize(selectedForResize) &&
            HitTestSelectedPlotHandle(chart, map, local) is var handle and not ChartResizeHandle.None &&
            TryBeginChartElementDrag(chart, map, selectedForResize, local, handle))
        {
            InvalidateVisual();
            return true;
        }

        var element = map.ResolveClick(local, SelectedChartElement) ?? ChartElementRef.ChartArea;

        if (element.IsChartArea)
        {
            ClearSelectedChartElement();
            return false;
        }

        var seriesName = map.GetDisplayName(element);

        _selectedChartElementChartId = chart.Id;
        _selectedChartElement = element;
        _selectedChartElementSeriesName = seriesName;
        InvalidateVisual();
        ChartElementSelected?.Invoke(chart.Id, element, seriesName);

        // Pressing on a draggable element and moving repositions it (Excel); a plain click never moves.
        if (!isDoubleClick)
            TryBeginChartElementDrag(chart, map, element, local, ChartResizeHandle.None);
        return true;
    }

    /// <summary>
    /// Arrow-key navigation between chart elements (Excel behaviour): with an inner element selected,
    /// Up/Down walk the chart's top-level elements and Left/Right walk the points of a selected series or
    /// the entries of a selected legend. Returns false when no inner element is selected so the caller
    /// keeps its normal arrow handling (nudging the chart object).
    /// </summary>
    public bool TryNavigateChartElement(ChartNavigationKey key)
    {
        if (SelectedChartElement is not { } current ||
            Viewport is null ||
            FindChart(SelectedObjectId) is not { } chart)
        {
            return false;
        }

        var map = GetChartElementMap(chart);
        var next = map.Navigate(current, key);
        if (next == current)
            return true;

        if (next.IsChartArea)
        {
            ClearSelectedChartElement();
            return true;
        }

        var name = map.GetDisplayName(next);
        _selectedChartElement = next;
        _selectedChartElementSeriesName = name;
        InvalidateVisual();
        ChartElementSelected?.Invoke(chart.Id, next, name);
        return true;
    }

    internal ChartElementMap GetChartElementMap(ChartModel chart)
    {
        var scale = CurrentChartRenderScale();
        var key = (chart, ComputeChartDataFingerprint(chart, Viewport!), scale);
        if (_chartElementMapCache.TryGetValue(key, out var cached))
            return cached;

        if (_chartElementMapCache.Count >= ChartElementMapCacheLimit)
            _chartElementMapCache.Clear();

        var map = ChartRenderer.BuildElementMap(chart, Viewport!, WorkbookTheme, scale);
        _chartElementMapCache[key] = map;
        return map;
    }

    private double CurrentChartRenderScale()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;
        return Math.Clamp(Math.Max(dpi.DpiScaleX, dpi.DpiScaleY) * zoom, 0.25, 4.0);
    }

    /// <summary>
    /// Draws the inner-element highlight over the selected chart instead of the whole-object handles.
    /// Returns false when no inner element is selected so the caller draws the regular handles.
    /// </summary>
    private bool TryDrawSelectedChartElementHighlight(DrawingContext dc, Rect chartRect)
    {
        if (SelectedChartElement is not { } element ||
            Viewport is null ||
            FindChart(SelectedObjectId) is not { } chart ||
            chart.Width <= 0 ||
            chart.Height <= 0 ||
            chartRect.IsEmpty)
        {
            return false;
        }

        var map = GetChartElementMap(chart);
        if (!map.Contains(element))
        {
            // The element no longer exists (e.g. it was just deleted): fall back to the chart handles.
            _selectedChartElement = null;
            return false;
        }

        var sx = chartRect.Width / chart.Width;
        var sy = chartRect.Height / chart.Height;
        var bounds = map.GetHighlightBounds(element);
        foreach (var b in bounds)
        {
            dc.DrawRectangle(
                null,
                ChartElementHighlightPen,
                new Rect(
                    chartRect.Left + (b.Left * sx) - ChartElementHighlightInflate,
                    chartRect.Top + (b.Top * sy) - ChartElementHighlightInflate,
                    Math.Max(1, (b.Width * sx) + (2 * ChartElementHighlightInflate)),
                    Math.Max(1, (b.Height * sy) + (2 * ChartElementHighlightInflate))));
        }

        if (GetChartElementDragPreview() is { } ghost)
        {
            dc.DrawRectangle(
                null,
                ChartElementHighlightPen,
                new Rect(chartRect.Left + (ghost.Left * sx), chartRect.Top + (ghost.Top * sy), Math.Max(1, ghost.Width * sx), Math.Max(1, ghost.Height * sy)));
        }

        if (bounds.Count == 1)
        {
            var b = bounds[0];
            var half = ChartElementHandleSize / 2;
            var grips = new List<(double, double)> { (b.Left, b.Top), (b.Right, b.Top), (b.Left, b.Bottom), (b.Right, b.Bottom) };
            if (ChartElementDragPlanner.CanResize(element))
            {
                var mx = b.Left + (b.Width / 2);
                var my = b.Top + (b.Height / 2);
                grips.AddRange([(mx, b.Top), (mx, b.Bottom), (b.Left, my), (b.Right, my)]);
            }

            foreach (var (cx, cy) in grips)
            {
                dc.DrawRectangle(
                    HandleFill,
                    HandlePen,
                    new Rect(chartRect.Left + (cx * sx) - half, chartRect.Top + (cy * sy) - half, ChartElementHandleSize, ChartElementHandleSize));
            }
        }

        return true;
    }

    private static Pen CreateChartElementHighlightPen()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x20, 0x7A, 0xC5));
        brush.Freeze();
        var pen = new Pen(brush, 1.5) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        return pen;
    }
}
