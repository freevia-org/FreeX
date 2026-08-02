using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    /// <summary>
    /// True when the given sheet should be presented as a full-window chart rather than a cell grid.
    /// </summary>
    private static bool IsChartsheet(Sheet? sheet) => sheet is { IsChartsheet: true };

    /// <summary>
    /// Switches the viewport between the normal cell-grid <see cref="SheetGrid"/> and the full-window
    /// <see cref="ChartsheetView"/> based on the active sheet's kind, and (when a chartsheet) renders
    /// its chart to fill the available window. Called from <see cref="UpdateViewport"/>.
    /// </summary>
    private void UpdateChartsheetPresentation(Sheet? sheet)
    {
        if (ChartsheetView is null || SheetGrid is null)
            return;

        if (!IsChartsheet(sheet))
        {
            if (ChartsheetView.Visibility != Visibility.Collapsed)
            {
                ChartsheetView.Visibility = Visibility.Collapsed;
                ChartsheetView.Source = null;
            }

            if (SheetGrid.Visibility != Visibility.Visible)
                SheetGrid.Visibility = Visibility.Visible;
            return;
        }

        // A chartsheet has no cell grid: hide the grid and show the chart full-window.
        SheetGrid.Visibility = Visibility.Collapsed;
        ChartsheetView.Visibility = Visibility.Visible;
        RenderActiveChartsheet(sheet!);
    }

    private void RenderActiveChartsheet(Sheet chartsheet)
    {
        if (ChartsheetView is null || _viewportService is null)
            return;

        var chart = chartsheet.ChartsheetChart;
        if (chart is null)
        {
            ChartsheetView.Source = null;
            return;
        }

        var width = ChartsheetView.ActualWidth;
        var height = ChartsheetView.ActualHeight;
        if (width < 1 || height < 1)
        {
            // The Image has not been measured yet; ChartsheetView_SizeChanged re-renders once it has.
            return;
        }

        // The chart's data lives on its own worksheet (the chartsheet itself has no grid). Build a
        // viewport from that data sheet so the renderer can resolve series cells by row/col.
        var dataSheetId = chart.DataRange.Start.Sheet;
        var dataSheet = _workbook.GetSheet(dataSheetId) ?? chartsheet;
        var viewport = BuildChartsheetDataViewport(dataSheet);

        // Size the chart to fill the window. ChartRenderer renders at chart.Width × chart.Height and
        // the Image stretches Uniform, so use the available pixel size for a crisp full-page chart.
        var dpi = VisualTreeHelper.GetDpi(ChartsheetView);
        var renderWidth = width;
        var renderHeight = height;
        var previousWidth = chart.Width;
        var previousHeight = chart.Height;
        chart.Width = renderWidth;
        chart.Height = renderHeight;
        try
        {
            var renderScale = Math.Clamp(Math.Max(dpi.DpiScaleX, dpi.DpiScaleY), 0.25, 4.0);
            ChartsheetView.Source = FreeX.App.UI.ChartRenderer.Render(
                chart, viewport, _workbook.Theme, renderScale);
        }
        finally
        {
            chart.Width = previousWidth;
            chart.Height = previousHeight;
        }
    }

    private FreeX.Core.Model.ViewportModel BuildChartsheetDataViewport(Sheet dataSheet)
    {
        var usedRange = dataSheet.GetUsedRange();
        var lastRow = usedRange?.End.Row ?? 1u;
        var lastCol = usedRange?.End.Col ?? 1u;

        // A generous available area ensures the data range is fully materialized into the viewport;
        // the renderer only consumes the cells it needs by row/col.
        var request = new ViewportRequest(
            TopRow: 1,
            LeftCol: 1,
            AvailableHeight: Math.Max(1, lastRow) * dataSheet.DefaultRowHeight + 64,
            AvailableWidth: Math.Max(1, lastCol) * (dataSheet.DefaultColumnWidth + 1) * 8 + 64,
            IncludeObjects: false,
            SplitPaneOffsets: null);

        return _viewportService.GetViewport(_workbook, dataSheet.Id, request);
    }

    private void ChartsheetView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (IsChartsheet(sheet) && ChartsheetView.Visibility == Visibility.Visible)
            RenderActiveChartsheet(sheet!);
    }

    /// <summary>
    /// Re-rasterizes the active chartsheet's chart after a WM_DPICHANGED notification (see
    /// <see cref="MainWindow_WndProc"/>). A per-monitor DPI change that happens without the window's
    /// DIP size changing (dragging across monitors while docked/maximized) never raises
    /// <see cref="ChartsheetView_SizeChanged"/>, so without this the baked bitmap keeps the stale,
    /// pre-move DPI scale and renders blurry/pixelated until the user manually resizes the window or
    /// navigates away from and back to the chartsheet.
    /// </summary>
    private void RefreshChartsheetForDpiChange()
    {
        if (ChartsheetView is null)
            return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (IsChartsheet(sheet) && ChartsheetView.Visibility == Visibility.Visible)
            RenderActiveChartsheet(sheet!);
    }
}
