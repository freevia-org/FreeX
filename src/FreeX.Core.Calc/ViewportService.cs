using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc;

/// <summary>
/// Implementation of IViewportService that prepares data for the UI.
/// Handles coordinate mapping, sparse data retrieval, and conditional formatting.
/// </summary>
public sealed partial class ViewportService : IViewportService
{
    private const int MaxViewportListCapacityHint = 65_536;

    public ViewportModel GetViewport(Workbook workbook, SheetId sheetId, ViewportRequest request)
    {
        var sheet = workbook.GetSheet(sheetId);
        if (sheet == null)
        {
            return new ViewportModel([], [], [], null, []);
        }

        var rowMetrics = BuildFrozenAwareRowMetrics(sheet, request.TopRow, request.AvailableHeight);
        var colMetrics = BuildFrozenAwareColMetrics(sheet, request.LeftCol, request.AvailableWidth);
        var hasAnyCellComments = HasAnyCellComments(sheet);
        var hasAnyStyleOnlyCells = sheet.HasStyleOnlyCells;

        // Pre-compute CF rule order and aggregates once per frame rather than per cell.
        var cfContext = BuildConditionalFormatContext(sheet);
        var hasConditionalFormats = cfContext.RulesByPriority.Count != 0;
        var hasConditionalIcons = cfContext.IconRulesByPriority.Count != 0;
        var cells = new List<DisplayCell>(EstimateDisplayCellCapacity(
            rowMetrics.Count,
            colMetrics.Count,
            sheet,
            hasAnyCellComments,
            hasAnyStyleOnlyCells));

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
                    ConditionalFormatIcon? cfIcon = null;
                    var hasComment = false;

                    if (hasConditionalFormats || hasAnyCellComments)
                        ApplyConditionalVisualsAndComments(
                            sheet,
                            sheetId,
                            rowMetric.Row,
                            colMetric.Col,
                            cell.Value,
                            workbook,
                            cfContext,
                            hasConditionalFormats,
                            hasConditionalIcons,
                            hasAnyCellComments,
                            ref style,
                            out cfIcon,
                            out hasComment);

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
                        hasComment
                    ));
                }
                else
                {
                    if (!hasAnyStyleOnlyCells && !hasAnyCellComments)
                        continue;

                    var styleOnlyId = sheet.GetStyleOnly(rowMetric.Row, colMetric.Col);
                    if (styleOnlyId.HasValue)
                    {
                        var style = workbook.GetStyle(styleOnlyId.Value);
                        ConditionalFormatIcon? cfIcon = null;
                        var hasComment = false;

                        if (hasConditionalFormats || hasAnyCellComments)
                            ApplyConditionalVisualsAndComments(
                                sheet,
                                sheetId,
                                rowMetric.Row,
                                colMetric.Col,
                                BlankValue.Instance,
                                workbook,
                                cfContext,
                                hasConditionalFormats,
                                hasConditionalIcons,
                                hasAnyCellComments,
                                ref style,
                                out cfIcon,
                                out hasComment);

                        cells.Add(new DisplayCell(
                            rowMetric.Row, colMetric.Col,
                            BlankValue.Instance,
                            "",
                            null,
                            styleOnlyId.Value,
                            null,
                            style,
                            cfIcon,
                            hasComment
                        ));
                    }
                    else if (hasAnyCellComments &&
                             HasCellComment(sheet, new CellAddress(sheetId, rowMetric.Row, colMetric.Col), hasAnyCellComments))
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
                BuildSplitPaneCells(workbook, sheet, sheetId, splitTopRows, splitLeftColumns, bottomLeftRows, topRightColumns, request.IncludeFormulas, cfContext, hasAnyCellComments),
                topRightColumns,
                bottomLeftRows)
            : null;

        var chartDataCells = request.IncludeObjects
            ? BuildChartDataCells(workbook, sheet)
            : [];

        return new ViewportModel(cells, rowMetrics, colMetrics, frozenPanes, [], splitPanes, chartDataCells);
    }

    private static int EstimateDisplayCellCapacity(
        int rowMetricCount,
        int colMetricCount,
        Sheet sheet,
        bool hasAnyCellComments,
        bool hasAnyStyleOnlyCells)
    {
        var visibleSlots = EstimateVisibleCellSlots(rowMetricCount, colMetricCount);
        if (visibleSlots == 0)
            return 0;

        if (hasAnyStyleOnlyCells)
            return ClampCapacityHint(visibleSlots);

        var possibleCells = sheet.CellCount;
        if (hasAnyCellComments)
            possibleCells = SaturatingAdd(possibleCells, sheet.Comments.Count + sheet.ThreadedComments.Count);

        return ClampCapacityHint(Math.Min(visibleSlots, possibleCells));
    }

    private static int EstimateVisibleCellSlots(int rowMetricCount, int colMetricCount)
    {
        if (rowMetricCount <= 0 || colMetricCount <= 0)
            return 0;

        var slots = (long)rowMetricCount * colMetricCount;
        return ClampCapacityHint(slots > int.MaxValue ? int.MaxValue : (int)slots);
    }

    private static int SaturatingAdd(int left, int right)
    {
        var result = (long)left + right;
        return result > int.MaxValue ? int.MaxValue : (int)result;
    }

    private static int ClampCapacityHint(int capacity) =>
        Math.Clamp(capacity, 0, MaxViewportListCapacityHint);

    private static void ApplyConditionalVisualsAndComments(
        Sheet sheet,
        SheetId sheetId,
        uint row,
        uint col,
        ScalarValue value,
        Workbook workbook,
        CfEvaluationContext cfContext,
        bool hasConditionalFormats,
        bool hasConditionalIcons,
        bool hasAnyCellComments,
        ref CellStyle style,
        out ConditionalFormatIcon? cfIcon,
        out bool hasComment)
    {
        cfIcon = null;
        hasComment = false;

        var addr = new CellAddress(sheetId, row, col);
        if (hasConditionalFormats)
        {
            var cfStyle = EvaluateConditionalFormats(sheet, addr, value, workbook, cfContext);
            if (cfStyle != null)
                style = MergeStyles(style, cfStyle);
            if (hasConditionalIcons)
                cfIcon = EvaluateConditionalIcon(sheet, addr, value, workbook, cfContext);
        }

        if (hasAnyCellComments)
            hasComment = HasCellComment(sheet, addr, hasAnyCellComments);
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
        CfEvaluationContext cfContext,
        bool hasAnyCellComments)
    {
        var dedupeCells = SplitPaneRegionsCanOverlap(topRows, leftColumns, bottomLeftRows, topRightColumns);
        HashSet<(uint Row, uint Col)>? seen = null;
        var hasAnyStyleOnlyCells = sheet.HasStyleOnlyCells;
        var hasConditionalFormats = cfContext.RulesByPriority.Count != 0;
        var hasConditionalIcons = cfContext.IconRulesByPriority.Count != 0;
        var cells = new List<DisplayCell>(EstimateSplitPaneCellCapacity(
            topRows,
            leftColumns,
            bottomLeftRows,
            topRightColumns,
            sheet,
            hasAnyCellComments,
            hasAnyStyleOnlyCells));

        foreach (var row in topRows)
        {
            foreach (var column in leftColumns)
                AddDisplayCell(cells, ref seen, dedupeCells, workbook, sheet, sheetId, row.Row, column.Col, EstimateCharacterWidth(column.Width), includeFormulas, cfContext, hasAnyCellComments, hasAnyStyleOnlyCells, hasConditionalFormats, hasConditionalIcons);
            foreach (var column in topRightColumns)
                AddDisplayCell(cells, ref seen, dedupeCells, workbook, sheet, sheetId, row.Row, column.Col, EstimateCharacterWidth(column.Width), includeFormulas, cfContext, hasAnyCellComments, hasAnyStyleOnlyCells, hasConditionalFormats, hasConditionalIcons);
        }

        foreach (var row in bottomLeftRows)
        {
            foreach (var column in leftColumns)
                AddDisplayCell(cells, ref seen, dedupeCells, workbook, sheet, sheetId, row.Row, column.Col, EstimateCharacterWidth(column.Width), includeFormulas, cfContext, hasAnyCellComments, hasAnyStyleOnlyCells, hasConditionalFormats, hasConditionalIcons);
        }

        return cells;
    }

    private static int EstimateSplitPaneCellCapacity(
        IReadOnlyList<RowMetric> topRows,
        IReadOnlyList<ColMetric> leftColumns,
        IReadOnlyList<RowMetric> bottomLeftRows,
        IReadOnlyList<ColMetric> topRightColumns,
        Sheet sheet,
        bool hasAnyCellComments,
        bool hasAnyStyleOnlyCells)
    {
        var visibleSlots = SaturatingAdd(
            EstimateVisibleCellSlots(topRows.Count, SaturatingAdd(leftColumns.Count, topRightColumns.Count)),
            EstimateVisibleCellSlots(bottomLeftRows.Count, leftColumns.Count));
        if (visibleSlots == 0)
            return 0;

        if (hasAnyStyleOnlyCells)
            return ClampCapacityHint(visibleSlots);

        var possibleCells = sheet.CellCount;
        if (hasAnyCellComments)
            possibleCells = SaturatingAdd(possibleCells, sheet.Comments.Count + sheet.ThreadedComments.Count);

        return ClampCapacityHint(Math.Min(visibleSlots, possibleCells));
    }

    private static void AddDisplayCell(
        List<DisplayCell> cells,
        ref HashSet<(uint Row, uint Col)>? seen,
        bool dedupeCells,
        Workbook workbook,
        Sheet sheet,
        SheetId sheetId,
        uint row,
        uint col,
        int targetWidthCharacters,
        bool includeFormulas,
        CfEvaluationContext cfContext,
        bool hasAnyCellComments,
        bool hasAnyStyleOnlyCells,
        bool hasConditionalFormats,
        bool hasConditionalIcons)
    {
        if (dedupeCells && !AddSeenCell(ref seen, row, col))
            return;

        var cell = sheet.GetCell(row, col);
        if (cell is null)
        {
            if (!hasAnyStyleOnlyCells && !hasAnyCellComments)
                return;

            var styleOnlyId = sheet.GetStyleOnly(row, col);
            if (!styleOnlyId.HasValue)
            {
                if (hasAnyCellComments &&
                    HasCellComment(sheet, new CellAddress(sheetId, row, col), hasAnyCellComments))
                {
                    cells.Add(new DisplayCell(
                        row,
                        col,
                        BlankValue.Instance,
                        "",
                        null,
                        StyleId.Default,
                        null,
                        workbook.GetStyle(StyleId.Default),
                        null,
                        true));
                }

                return;
            }

            var style = workbook.GetStyle(styleOnlyId.Value);
            ConditionalFormatIcon? cfIcon = null;
            var hasComment = false;
            if (hasConditionalFormats || hasAnyCellComments)
                ApplyConditionalVisualsAndComments(
                    sheet,
                    sheetId,
                    row,
                    col,
                    BlankValue.Instance,
                    workbook,
                    cfContext,
                    hasConditionalFormats,
                    hasConditionalIcons,
                    hasAnyCellComments,
                    ref style,
                    out cfIcon,
                    out hasComment);

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
                hasComment));
            return;
        }

        {
        var style = workbook.GetStyle(cell.StyleId);
        ConditionalFormatIcon? cfIcon = null;
        var hasComment = false;
        if (hasConditionalFormats || hasAnyCellComments)
            ApplyConditionalVisualsAndComments(
                sheet,
                sheetId,
                row,
                col,
                cell.Value,
                workbook,
                cfContext,
                hasConditionalFormats,
                hasConditionalIcons,
                hasAnyCellComments,
                ref style,
                out cfIcon,
                out hasComment);

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
            hasComment));
        }
    }

    private static bool AddSeenCell(ref HashSet<(uint Row, uint Col)>? seen, uint row, uint col)
    {
        seen ??= [];
        return seen.Add((row, col));
    }

    private static bool SplitPaneRegionsCanOverlap(
        IReadOnlyList<RowMetric> topRows,
        IReadOnlyList<ColMetric> leftColumns,
        IReadOnlyList<RowMetric> bottomLeftRows,
        IReadOnlyList<ColMetric> topRightColumns) =>
        (topRows.Count > 0 && ColumnsOverlap(leftColumns, topRightColumns)) ||
        (leftColumns.Count > 0 && RowsOverlap(topRows, bottomLeftRows));

    private static bool RowsOverlap(IReadOnlyList<RowMetric> first, IReadOnlyList<RowMetric> second)
    {
        for (var firstIndex = 0; firstIndex < first.Count; firstIndex++)
        {
            var row = first[firstIndex].Row;
            for (var secondIndex = 0; secondIndex < second.Count; secondIndex++)
            {
                var otherRow = second[secondIndex].Row;
                if (otherRow == row)
                    return true;
                if (otherRow > row)
                    break;
            }
        }

        return false;
    }

    private static bool ColumnsOverlap(IReadOnlyList<ColMetric> first, IReadOnlyList<ColMetric> second)
    {
        for (var firstIndex = 0; firstIndex < first.Count; firstIndex++)
        {
            var col = first[firstIndex].Col;
            for (var secondIndex = 0; secondIndex < second.Count; secondIndex++)
            {
                var otherCol = second[secondIndex].Col;
                if (otherCol == col)
                    return true;
                if (otherCol > col)
                    break;
            }
        }

        return false;
    }

    private static bool HasAnyCellComments(Sheet sheet) =>
        sheet.Comments.Count != 0 ||
        sheet.ThreadedComments.Count != 0;

    private static bool HasCellComment(Sheet sheet, CellAddress address, bool hasAnyCellComments) =>
        hasAnyCellComments &&
        (sheet.Comments.ContainsKey(address) ||
         sheet.ThreadedComments.ContainsKey(address));

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
            workbook.IndexedColors,
            workbook.Theme);
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
