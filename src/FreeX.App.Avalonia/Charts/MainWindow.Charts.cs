using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using FreeX.App.Avalonia.Charts;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Presentation.DrawingInteraction;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static readonly AvaloniaTextMeasurer ChartTextMeasurer = new();

    /// <summary>
    /// Adds chart visuals for the active sheet's <see cref="ChartModel"/>s to the drawing-object
    /// overlay. Charts are not projected into <see cref="ViewportModel.DrawingObjects"/> by the
    /// viewport service, so they are resolved here directly from the sheet, laid out by the portable
    /// <see cref="ChartLayoutEngine"/>, painted by <see cref="AvaloniaChartRenderer"/>, and made
    /// selectable like other drawing objects.
    /// </summary>
    private void AddChartOverlays(Canvas overlay, ViewportModel viewport)
    {
        var sheet = _session.ActiveSheet;
        if (sheet.Charts.Count == 0)
            return;

        var showHeadings = sheet.ShowHeadings;
        var zoomFactor = GetActiveZoomFactor();
        var accessor = ChartViewportCellAccessorBuilder.BuildValueAccessor(viewport, sheet.Id);
        var headerLeft = showHeadings ? GetRowHeaderWidth(viewport, zoomFactor) : 0;
        var headerTop = showHeadings ? GetColumnHeaderHeight(viewport, zoomFactor) : 0;

        foreach (var chart in sheet.Charts)
        {
            if (!chart.IsVisible || !ChartLayoutEngine.IsSupported(chart.Type))
                continue;

            var width = Math.Max(1, chart.Width * zoomFactor);
            var height = Math.Max(1, chart.Height * zoomFactor);

            // The chart's on-sheet pixel box maps into a local canvas (origin 0,0). Inset the plot
            // area so axis tick labels and the title have room inside the box.
            var inset = Math.Min(28 * zoomFactor, Math.Min(width, height) / 4);
            var plotArea = new PlotRect(inset, inset, Math.Max(1, width - (2 * inset)), Math.Max(1, height - (2 * inset)));

            var request = ChartLayoutRequestBuilder.TryBuild(chart, plotArea, accessor, ChartTextMeasurer);
            if (request is null)
                continue;

            Control visual;
            ChartLayout layout;
            try
            {
                layout = ChartLayoutEngine.Layout(request);
                var renderer = new AvaloniaChartRenderer(chart, _session.Workbook.Theme);
                visual = renderer.Render(layout, width, height);
            }
            catch (NotSupportedException)
            {
                continue;
            }

            var container = CreateSelectableChartContainer(chart, layout, visual, width, height);
            // Chart Left/Top are sheet-absolute pixels; positioning is exact at the scroll origin and
            // tracks the grid origin otherwise (the viewport does not expose the scrolled sheet offset).
            Canvas.SetLeft(container, headerLeft + (chart.Left * zoomFactor));
            Canvas.SetTop(container, headerTop + (chart.Top * zoomFactor));
            overlay.Children.Add(container);
        }
    }

    private Control CreateSelectableChartContainer(
        ChartModel chart,
        ChartLayout layout,
        Control visual,
        double width,
        double height)
    {
        var selected = IsSelectedChart(chart);
        var container = new AvaloniaGrid
        {
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            Background = Brushes.Transparent,
            ClipToBounds = false,
            Cursor = Cursor.Default,
            Focusable = true,
        };

        AutomationProperties.SetAutomationId(container, $"Chart{chart.Id:N}");
        AutomationProperties.SetName(container, UiText.Format("Chart_AutomationNameFormat", ChartDisplayName(chart)));
        AutomationProperties.SetHelpText(container, UiText.Get("ChartLoc_SelectChartHelpText"));
        AutomationProperties.SetItemStatus(
            container,
            UiText.Get(selected ? "Automation_Selected" : "Automation_NotSelected"));

        // WPF shows a move cursor over a chart body and directional resize cursors over the
        // selected chart's handles. Keep the hover affordance driven by the same portable hit-test
        // used by TryBeginChartDrag so Linux never advertises a different interaction than a press
        // can actually start.
        container.PointerMoved += (_, args) =>
        {
            var point = args.GetCurrentPoint(container).Position;
            var kind = ResolveChartHoverDragKind(
                selected,
                new LayoutPoint(point.X, point.Y),
                width,
                height);
            container.Cursor = kind == ObjectDragKind.None
                ? Cursor.Default
                : DrawingObjectDragCursor(kind);
        };
        container.PointerExited += (_, _) => container.Cursor = Cursor.Default;

        container.PointerPressed += (_, args) =>
        {
            var point = args.GetCurrentPoint(container);
            if (point.Properties.IsRightButtonPressed)
            {
                // Right-click selects the chart, then opens the per-target Chart context menu.
                HandleChartPointerContext(chart, container, args);
                return;
            }

            if (point.Properties.IsLeftButtonPressed)
            {
                // A click on the already-selected chart may begin a move/resize drag (on a resize
                // handle or the body); otherwise it just selects the chart.
                if (selected && TryBeginChartDrag(chart, container, args))
                    return;

                SelectChart(chart);
                args.Handled = true;
            }
        };
        container.KeyDown += (_, args) =>
        {
            if (args.Key is Key.Enter or Key.Space)
            {
                SelectChart(chart);
                args.Handled = true;
            }
        };

        if (selected)
            WireChartDragMoveRelease(chart, container);

        container.Children.Add(visual);
        if (BuildWaterfallPointContextOverlay(chart, layout) is { } pointOverlay)
            container.Children.Add(pointOverlay);
        if (BuildPivotChartFieldButtonOverlay(chart) is { } fieldButtons)
            container.Children.Add(fieldButtons);
        if (selected)
            container.Children.Add(CreateChartSelectionAdorner(width, height));

        return container;
    }

    internal static ObjectDragKind ResolveChartHoverDragKind(
        bool selected,
        LayoutPoint position,
        double width,
        double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return ObjectDragKind.None;

        if (!selected)
            return ObjectDragKind.Move;

        return ObjectDragPlanner.HitTestHandle(
            position,
            new LayoutRect(0, 0, width, height),
            ChartHandleSize,
            DrawingObjectHandleHitPadding);
    }

    private void SelectChart(ChartModel chart)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        _selectedDrawingObjectKind = SelectionPaneObjectKind.Chart;
        _selectedDrawingObjectId = chart.Id;
        RefreshShell(UiText.Format("ChartLoc_SelectedChart", ChartDisplayName(chart)));
    }

    private bool IsSelectedChart(ChartModel chart) =>
        _selectedDrawingObjectKind == SelectionPaneObjectKind.Chart &&
        _selectedDrawingObjectId == chart.Id;

    private static string ChartDisplayName(ChartModel chart) =>
        !string.IsNullOrWhiteSpace(chart.Name) ? chart.Name!
        : !string.IsNullOrWhiteSpace(chart.Title) ? chart.Title!
        : "Chart";

    /// <summary>
    /// Inserts a chart of <paramref name="chartType"/> over the current selection through the shared
    /// session command path, reusing the Core <see cref="FreeX.Core.Commands.AddChartCommand"/> the
    /// chart overlay paints from <c>ActiveSheet.Charts</c>. The selection is the chart's data range; the
    /// chart lands at the factory's default on-sheet position. Surfaces the Core guard message (e.g.
    /// "must include at least one data series") on failure rather than silently no-opping.
    /// </summary>
    private void InsertChartFromSelection(ChartType chartType)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        var command = ChartCommandWorkflowPlanner.BuildEmbeddedChartCommand(
            _session.ActiveSheet,
            range,
            chartType);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            RefreshShell(result.ErrorMessage ?? UiText.Get("ChartLoc_InsertChartFailed"));
            return;
        }

        ClearSelectedDrawingObject();
        RefreshShell(UiText.Format("ChartLoc_InsertedChartFrom", chartType, FormatCellReference(range.Start)));
    }

}
