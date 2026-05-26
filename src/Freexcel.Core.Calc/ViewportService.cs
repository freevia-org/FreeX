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
                        : GetDisplayText(workbook, sheet, cell, ref style, EstimateCharacterWidth(colMetric.Width));

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

        var chartDataCells = request.IncludeObjects
            ? BuildChartDataCells(workbook, sheet)
            : [];

        return new ViewportModel(cells, rowMetrics, colMetrics, frozenPanes, [], splitPanes, chartDataCells);
    }

    private static IReadOnlyList<ChartDataCell> BuildChartDataCells(Workbook workbook, Sheet sheet)
    {
        if (sheet.Charts.Count == 0)
            return [];

        var chartCells = new List<ChartDataCell>();
        var seen = new HashSet<(SheetId SheetId, uint Row, uint Col)>();
        foreach (var chart in sheet.Charts)
        {
            var sourceSheet = workbook.GetSheet(chart.DataRange.Start.Sheet);
            if (sourceSheet is null)
                continue;

            for (uint row = chart.DataRange.Start.Row; row <= chart.DataRange.End.Row; row++)
            {
                for (uint col = chart.DataRange.Start.Col; col <= chart.DataRange.End.Col; col++)
                {
                    if (!seen.Add((sourceSheet.Id, row, col)))
                        continue;

                    var cell = sourceSheet.GetCell(row, col);
                    if (cell is null)
                    {
                        chartCells.Add(new ChartDataCell(sourceSheet.Id, row, col, ""));
                        continue;
                    }

                    var style = workbook.GetStyle(cell.StyleId);
                    chartCells.Add(new ChartDataCell(
                        sourceSheet.Id,
                        row,
                        col,
                        GetDisplayText(
                            workbook,
                            sourceSheet,
                            cell,
                            ref style,
                            EstimateCharacterWidth(sourceSheet.ColumnWidths.GetValueOrDefault(col, sourceSheet.DefaultColumnWidth)))));
                }
            }
        }

        return chartCells;
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
            : GetDisplayText(workbook, sheet, cell, ref style, targetWidthCharacters);

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
    private static string GetDisplayText(
        Workbook workbook,
        Sheet sheet,
        Cell cell,
        ref CellStyle style,
        int targetWidthCharacters)
    {
        if (sheet.ShowFormulas && cell.FormulaText is not null)
            return "=" + cell.FormulaText;

        var result = NumberFormatter.FormatWithColor(
            cell.Value,
            style.NumberFormat,
            targetWidthCharacters,
            workbook.IndexedColors);
        if (TryParseHexColor(result.ColorHex, out var color))
            style.FontColor = color;

        return result.Text;
    }

    private static bool TryParseHexColor(string? hex, out CellColor color)
    {
        color = default;
        if (hex is null ||
            hex.Length != 7 ||
            hex[0] != '#' ||
            !byte.TryParse(hex.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) ||
            !byte.TryParse(hex.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) ||
            !byte.TryParse(hex.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = CellColor.FromArgb(r, g, b);
        return true;
    }

    private static int EstimateCharacterWidth(double pixelWidth) =>
        Math.Max(1, (int)Math.Round(pixelWidth / 8.0, MidpointRounding.AwayFromZero));

}
