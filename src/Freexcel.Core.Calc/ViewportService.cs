using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

/// <summary>
/// Implementation of IViewportService that prepares data for the UI.
/// Handles coordinate mapping and sparse data retrieval.
/// </summary>
public sealed class ViewportService : IViewportService
{
    public ViewportModel GetViewport(Workbook workbook, SheetId sheetId, ViewportRequest request)
    {
        var sheet = workbook.GetSheet(sheetId);
        if (sheet == null)
        {
            return new ViewportModel([], [], [], null, []);
        }

        var cells = new List<DisplayCell>();
        var rowMetrics = new List<RowMetric>();
        var colMetrics = new List<ColMetric>();

        // Calculate Row Metrics — iterate until we've filled the available height, skipping filter-hidden rows
        const uint MaxRows = 1_048_576;
        double topOffset = 0;
        for (uint r = request.TopRow; r <= request.TopRow + MaxRows; r++)
        {
            if (sheet.HiddenRows.Contains(r)) continue;
            double height = sheet.RowHeights.GetValueOrDefault(r, sheet.DefaultRowHeight);
            rowMetrics.Add(new RowMetric(r, height, topOffset));
            topOffset += height;
            if (topOffset > request.AvailableHeight) break;
        }

        // Calculate Column Metrics — iterate until we've filled the available width
        const uint MaxCols = 16_384;
        double leftOffset = 0;
        for (uint c = request.LeftCol; c <= request.LeftCol + MaxCols; c++)
        {
            double width = sheet.ColumnWidths.GetValueOrDefault(c, sheet.DefaultColumnWidth) * 8;
            colMetrics.Add(new ColMetric(c, width, leftOffset));
            leftOffset += width;
            if (leftOffset > request.AvailableWidth) break;
        }

        // Retrieve Cells in Viewport
        foreach (var rowMetric in rowMetrics)
        {
            foreach (var colMetric in colMetrics)
            {
                var cell = sheet.GetCell(rowMetric.Row, colMetric.Col);
                if (cell != null)
                {
                    var style = workbook.GetStyle(cell.StyleId);
                    cells.Add(new DisplayCell(
                        rowMetric.Row, colMetric.Col,
                        cell.Value,
                        NumberFormatter.Format(cell.Value, style.NumberFormat),
                        request.IncludeFormulas ? cell.FormulaText : null,
                        cell.StyleId,
                        null,
                        style
                    ));
                }
            }
        }

        var frozenPanes = (sheet.FrozenRows > 0 || sheet.FrozenCols > 0)
            ? new FrozenPaneState(sheet.FrozenRows, sheet.FrozenCols)
            : null;

        return new ViewportModel(cells, rowMetrics, colMetrics, frozenPanes, []);
    }

    public CellAddress? HitTest(Workbook workbook, SheetId sheetId, double x, double y, double zoom)
    {
        var sheet = workbook.GetSheet(sheetId);
        if (sheet == null) return null;

        // Apply zoom to incoming coordinates
        double targetX = x / zoom;
        double targetY = y / zoom;

        // Very simple hit testing (assuming fixed sizes for now to keep it fast)
        // In a real app we'd binary search the accumulated metrics
        uint col = 1 + (uint)(targetX / (sheet.DefaultColumnWidth * 8));
        uint row = 1 + (uint)(targetY / sheet.DefaultRowHeight);

        return new CellAddress(sheetId, row, col);
    }

}
