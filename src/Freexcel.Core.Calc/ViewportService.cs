using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

/// <summary>
/// Implementation of IViewportService that prepares data for the UI.
/// Handles coordinate mapping, sparse data retrieval, and conditional formatting.
/// </summary>
public sealed partial class ViewportService : IViewportService
{
    public ViewportModel GetViewport(Workbook workbook, SheetId sheetId, ViewportRequest request)
    {
        var sheet = workbook.GetSheet(sheetId);
        if (sheet == null)
        {
            return new ViewportModel([], [], [], null, []);
        }

        var cells = new List<DisplayCell>();
        var rowMetrics = BuildFrozenAwareRowMetrics(sheet, request.TopRow, request.AvailableHeight);
        var colMetrics = BuildFrozenAwareColMetrics(sheet, request.LeftCol, request.AvailableWidth);

        // Pre-compute CF rule order and aggregates once per frame rather than per cell.
        var cfContext = BuildConditionalFormatContext(sheet);

        // Calculate Row Metrics — iterate until we've filled the available height, skipping hidden rows
        // Calculate Column Metrics — iterate until we've filled the available width
        // Retrieve Cells in Viewport
        foreach (var rowMetric in rowMetrics)
        {
            foreach (var colMetric in colMetrics)
            {
                var cell = sheet.GetCell(rowMetric.Row, colMetric.Col);
                if (cell != null)
                {
                    var style = workbook.GetStyle(cell.StyleId);

                    // Evaluate conditional formats and merge any triggered CF style on top
                    var addr = new CellAddress(sheetId, rowMetric.Row, colMetric.Col);
                    var cfStyle = EvaluateConditionalFormats(sheet, addr, cell.Value, workbook, cfContext);
                    if (cfStyle != null)
                        style = MergeStyles(style, cfStyle);
                    var cfIcon = EvaluateConditionalIcon(sheet, addr, cell.Value, cfContext);
                    var displayText = cfIcon?.ShowValue == false
                        ? ""
                        : GetDisplayText(sheet, cell, style, EstimateCharacterWidth(colMetric.Width));

                    cells.Add(new DisplayCell(
                        rowMetric.Row, colMetric.Col,
                        cell.Value,
                        displayText,
                        request.IncludeFormulas ? cell.FormulaText : null,
                        cell.StyleId,
                        null,
                        style,
                        cfIcon,
                        HasCellComment(sheet, addr)
                    ));
                }
                else
                {
                    var styleOnlyId = sheet.GetStyleOnly(rowMetric.Row, colMetric.Col);
                    var addr = new CellAddress(sheetId, rowMetric.Row, colMetric.Col);
                    if (styleOnlyId.HasValue)
                    {
                        var style = workbook.GetStyle(styleOnlyId.Value);
                        var cfStyle = EvaluateConditionalFormats(sheet, addr, BlankValue.Instance, workbook, cfContext);
                        if (cfStyle != null)
                            style = MergeStyles(style, cfStyle);
                        var cfIcon = EvaluateConditionalIcon(sheet, addr, BlankValue.Instance, cfContext);

                        cells.Add(new DisplayCell(
                            rowMetric.Row, colMetric.Col,
                            BlankValue.Instance,
                            "",
                            null,
                            styleOnlyId.Value,
                            null,
                            style,
                            cfIcon,
                            HasCellComment(sheet, addr)
                        ));
                    }
                    else if (HasCellComment(sheet, addr))
                    {
                        cells.Add(new DisplayCell(
                            rowMetric.Row,
                            colMetric.Col,
                            BlankValue.Instance,
                            "",
                            null,
                            StyleId.Default,
                            null,
                            workbook.GetStyle(StyleId.Default),
                            null,
                            true));
                    }
                }
            }
        }

        var frozenPanes = (sheet.FrozenRows > 0 || sheet.FrozenCols > 0)
            ? new FrozenPaneState(sheet.FrozenRows, sheet.FrozenCols)
            : null;
        var splitTopRows = sheet.SplitRow is { } splitRow
            ? BuildRowMetrics(sheet, 1, splitRow - 1, request.AvailableHeight)
            : [];
        var splitLeftColumns = sheet.SplitColumn is { } splitColumn
            ? BuildColMetrics(sheet, 1, splitColumn - 1, request.AvailableWidth)
            : [];
        var topRightColumns = sheet.SplitColumn.HasValue
            ? BuildColMetrics(sheet, request.SplitPaneOffsets?.TopRightLeftCol ?? request.LeftCol, CellAddress.MaxCol, request.AvailableWidth)
            : colMetrics;
        var bottomLeftRows = sheet.SplitRow.HasValue
            ? BuildRowMetrics(sheet, request.SplitPaneOffsets?.BottomLeftTopRow ?? request.TopRow, CellAddress.MaxRow, request.AvailableHeight)
            : rowMetrics;
        var splitPanes = (sheet.SplitRow.HasValue || sheet.SplitColumn.HasValue)
            ? new SplitPaneState(
                sheet.SplitRow,
                sheet.SplitColumn,
                splitTopRows,
                splitLeftColumns,
                BuildSplitPaneCells(workbook, sheet, sheetId, splitTopRows, splitLeftColumns, bottomLeftRows, topRightColumns, request.IncludeFormulas, cfContext),
                topRightColumns,
                bottomLeftRows)
            : null;

        return new ViewportModel(cells, rowMetrics, colMetrics, frozenPanes, [], splitPanes);
    }


    private static bool IsRowHidden(Sheet sheet, uint row) =>
        sheet.IsRowEffectivelyHidden(row);

    private static List<RowMetric> BuildFrozenAwareRowMetrics(Sheet sheet, uint startRow, double availableHeight)
    {
        var frozenRows = Math.Min(sheet.FrozenRows, CellAddress.MaxRow);
        if (frozenRows == 0)
            return BuildRowMetrics(sheet, startRow, CellAddress.MaxRow, availableHeight);

        var pinnedRows = BuildRowMetrics(sheet, 1, frozenRows, availableHeight);
        var pinnedHeight = pinnedRows.Sum(row => row.Height);
        var remainingHeight = Math.Max(0, availableHeight - pinnedHeight);
        var bodyStart = Math.Max(startRow, frozenRows + 1);
        var bodyRows = remainingHeight > 0 && bodyStart <= CellAddress.MaxRow
            ? OffsetRows(BuildRowMetrics(sheet, bodyStart, CellAddress.MaxRow, remainingHeight), pinnedHeight)
            : [];

        return pinnedRows.Concat(bodyRows).ToList();
    }

    private static List<ColMetric> BuildFrozenAwareColMetrics(Sheet sheet, uint startCol, double availableWidth)
    {
        var frozenCols = Math.Min(sheet.FrozenCols, CellAddress.MaxCol);
        if (frozenCols == 0)
            return BuildColMetrics(sheet, startCol, CellAddress.MaxCol, availableWidth);

        var pinnedColumns = BuildColMetrics(sheet, 1, frozenCols, availableWidth);
        var pinnedWidth = pinnedColumns.Sum(column => column.Width);
        var remainingWidth = Math.Max(0, availableWidth - pinnedWidth);
        var bodyStart = Math.Max(startCol, frozenCols + 1);
        var bodyColumns = remainingWidth > 0 && bodyStart <= CellAddress.MaxCol
            ? OffsetColumns(BuildColMetrics(sheet, bodyStart, CellAddress.MaxCol, remainingWidth), pinnedWidth)
            : [];

        return pinnedColumns.Concat(bodyColumns).ToList();
    }

    private static List<RowMetric> OffsetRows(IReadOnlyList<RowMetric> rows, double topOffset) =>
        rows.Select(row => row with { TopOffset = row.TopOffset + topOffset }).ToList();

    private static List<ColMetric> OffsetColumns(IReadOnlyList<ColMetric> columns, double leftOffset) =>
        columns.Select(column => column with { LeftOffset = column.LeftOffset + leftOffset }).ToList();

    private static List<RowMetric> BuildRowMetrics(Sheet sheet, uint startRow, uint endRow, double availableHeight)
    {
        var rowMetrics = new List<RowMetric>();
        if (startRow < 1 || endRow < startRow)
            return rowMetrics;

        var maxRow = Math.Min(endRow, CellAddress.MaxRow);
        var terminalRows = BuildTerminalRowMetrics(sheet, startRow, maxRow, availableHeight);
        if (terminalRows is not null)
            return terminalRows;

        double topOffset = 0;
        for (uint row = startRow; row <= maxRow; row++)
        {
            if (IsRowHidden(sheet, row)) continue;
            double height = sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight);
            rowMetrics.Add(new RowMetric(row, height, topOffset));
            topOffset += height;
            if (topOffset > availableHeight) break;
        }

        return rowMetrics;
    }

    private static List<ColMetric> BuildColMetrics(Sheet sheet, uint startCol, uint endCol, double availableWidth)
    {
        var colMetrics = new List<ColMetric>();
        if (startCol < 1 || endCol < startCol)
            return colMetrics;

        var maxCol = Math.Min(endCol, CellAddress.MaxCol);
        var terminalColumns = BuildTerminalColMetrics(sheet, startCol, maxCol, availableWidth);
        if (terminalColumns is not null)
            return terminalColumns;

        double leftOffset = 0;
        for (uint col = startCol; col <= maxCol; col++)
        {
            if (sheet.IsColEffectivelyHidden(col)) continue;
            double width = sheet.ColumnWidths.GetValueOrDefault(col, sheet.DefaultColumnWidth) * 8;
            colMetrics.Add(new ColMetric(col, width, leftOffset));
            leftOffset += width;
            if (leftOffset > availableWidth) break;
        }

        return colMetrics;
    }

    private static List<RowMetric>? BuildTerminalRowMetrics(
        Sheet sheet,
        uint requestedStartRow,
        uint maxRow,
        double availableHeight)
    {
        if (availableHeight <= 0 || maxRow < CellAddress.MaxRow)
            return null;

        var rows = new List<(uint Row, double Height)>();
        double totalHeight = 0;
        for (uint row = maxRow; row >= 1; row--)
        {
            if (!IsRowHidden(sheet, row))
            {
                var height = sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight);
                rows.Add((row, height));
                totalHeight += height;
                if (totalHeight >= availableHeight)
                    break;
            }

            if (row == 1)
                break;
        }

        rows.Reverse();
        if (rows.Count == 0)
            return null;

        var firstTerminalRow = rows[0].Row;
        var terminalThreshold = firstTerminalRow > 1 ? firstTerminalRow - 1 : 1;
        if (requestedStartRow < terminalThreshold)
            return null;

        var metrics = new List<RowMetric>(rows.Count);
        var topOffset = availableHeight - totalHeight;
        foreach (var (row, height) in rows)
        {
            metrics.Add(new RowMetric(row, height, topOffset));
            topOffset += height;
        }

        return metrics;
    }

    private static List<ColMetric>? BuildTerminalColMetrics(
        Sheet sheet,
        uint requestedStartCol,
        uint maxCol,
        double availableWidth)
    {
        if (availableWidth <= 0 || maxCol < CellAddress.MaxCol)
            return null;

        var columns = new List<(uint Col, double Width)>();
        double totalWidth = 0;
        for (uint col = maxCol; col >= 1; col--)
        {
            if (!sheet.IsColEffectivelyHidden(col))
            {
                var width = sheet.ColumnWidths.GetValueOrDefault(col, sheet.DefaultColumnWidth) * 8;
                columns.Add((col, width));
                totalWidth += width;
                if (totalWidth >= availableWidth)
                    break;
            }

            if (col == 1)
                break;
        }

        columns.Reverse();
        if (columns.Count == 0)
            return null;

        var firstTerminalColumn = columns[0].Col;
        var terminalThreshold = firstTerminalColumn > 1 ? firstTerminalColumn - 1 : 1;
        if (requestedStartCol < terminalThreshold)
            return null;

        var metrics = new List<ColMetric>(columns.Count);
        var leftOffset = availableWidth - totalWidth;
        foreach (var (col, width) in columns)
        {
            metrics.Add(new ColMetric(col, width, leftOffset));
            leftOffset += width;
        }

        return metrics;
    }

    private static List<DisplayCell> BuildSplitPaneCells(
        Workbook workbook,
        Sheet sheet,
        SheetId sheetId,
        IReadOnlyList<RowMetric> topRows,
        IReadOnlyList<ColMetric> leftColumns,
        IReadOnlyList<RowMetric> bottomLeftRows,
        IReadOnlyList<ColMetric> topRightColumns,
        bool includeFormulas,
        CfEvaluationContext cfContext)
    {
        var cells = new List<DisplayCell>();
        var seen = new HashSet<(uint Row, uint Col)>();

        foreach (var row in topRows)
        {
            foreach (var column in leftColumns)
                AddDisplayCell(cells, seen, workbook, sheet, sheetId, row.Row, column.Col, EstimateCharacterWidth(column.Width), includeFormulas, cfContext);
            foreach (var column in topRightColumns)
                AddDisplayCell(cells, seen, workbook, sheet, sheetId, row.Row, column.Col, EstimateCharacterWidth(column.Width), includeFormulas, cfContext);
        }

        foreach (var row in bottomLeftRows)
        {
            foreach (var column in leftColumns)
                AddDisplayCell(cells, seen, workbook, sheet, sheetId, row.Row, column.Col, EstimateCharacterWidth(column.Width), includeFormulas, cfContext);
        }

        return cells;
    }

    private static void AddDisplayCell(
        List<DisplayCell> cells,
        HashSet<(uint Row, uint Col)> seen,
        Workbook workbook,
        Sheet sheet,
        SheetId sheetId,
        uint row,
        uint col,
        int targetWidthCharacters,
        bool includeFormulas,
        CfEvaluationContext cfContext)
    {
        if (!seen.Add((row, col)))
            return;

        var cell = sheet.GetCell(row, col);
        if (cell is null)
        {
            var styleOnlyId = sheet.GetStyleOnly(row, col);
            if (!styleOnlyId.HasValue)
                return;

            var style = workbook.GetStyle(styleOnlyId.Value);
            var addr = new CellAddress(sheetId, row, col);
            var cfStyle = EvaluateConditionalFormats(sheet, addr, BlankValue.Instance, workbook, cfContext);
            if (cfStyle != null)
                style = MergeStyles(style, cfStyle);
            var cfIcon = EvaluateConditionalIcon(sheet, addr, BlankValue.Instance, cfContext);

            cells.Add(new DisplayCell(
                row,
                col,
                BlankValue.Instance,
                "",
                null,
                styleOnlyId.Value,
                null,
                style,
                cfIcon,
                HasCellComment(sheet, addr)));
            return;
        }

        {
        var style = workbook.GetStyle(cell.StyleId);
        var addr = new CellAddress(sheetId, row, col);
        var cfStyle = EvaluateConditionalFormats(sheet, addr, cell.Value, workbook, cfContext);
        if (cfStyle != null)
            style = MergeStyles(style, cfStyle);
        var cfIcon = EvaluateConditionalIcon(sheet, addr, cell.Value, cfContext);
        var displayText = cfIcon?.ShowValue == false
            ? ""
            : GetDisplayText(sheet, cell, style, targetWidthCharacters);

        cells.Add(new DisplayCell(
            row,
            col,
            cell.Value,
            displayText,
            includeFormulas ? cell.FormulaText : null,
            cell.StyleId,
            null,
            style,
            cfIcon,
            HasCellComment(sheet, addr)));
        }
    }

    private static bool HasCellComment(Sheet sheet, CellAddress address) =>
        sheet.Comments.ContainsKey(address) ||
        sheet.ThreadedComments.ContainsKey(address);

    // ── Conditional format evaluation ─────────────────────────────────────────

    /// <summary>
    /// Evaluates all conditional format rules that cover <paramref name="addr"/> (ordered by
    /// Priority ascending = highest precedence first). Returns the first matching rule's style,
    /// or null when no rule fires.
    /// </summary>
    private static string GetDisplayText(Sheet sheet, Cell cell, CellStyle style, int targetWidthCharacters) =>
        sheet.ShowFormulas && cell.FormulaText is not null
            ? "=" + cell.FormulaText
            : NumberFormatter.Format(cell.Value, style.NumberFormat, targetWidthCharacters);

    private static int EstimateCharacterWidth(double pixelWidth) =>
        Math.Max(1, (int)Math.Round(pixelWidth / 8.0, MidpointRounding.AwayFromZero));

}
