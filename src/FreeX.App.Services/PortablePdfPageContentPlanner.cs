using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum PortablePdfPageContentPlanStatus
{
    Ready,
    PageRequestUnavailable,
    SheetUnavailable
}

public enum PortablePdfPageAxisRole
{
    Title,
    Body
}

public sealed record PortablePdfPageRow(uint Row, PortablePdfPageAxisRole Role);

public sealed record PortablePdfPageColumn(uint Column, PortablePdfPageAxisRole Role);

public sealed record PortablePdfPageCell(
    uint Row,
    uint Column,
    string DisplayText,
    StyleId StyleId,
    bool IsTitleRow,
    bool IsTitleColumn,
    CellColor? ConditionalFillColor = null)
{
    public bool IsTitle => IsTitleRow || IsTitleColumn;
    public bool IsBody => !IsTitle;
}

public sealed record PortablePdfPageContentPlan(
    PortablePdfPageContentPlanStatus Status,
    string StatusText,
    PortablePdfExportPageRequest? PageRequest,
    IReadOnlyList<PortablePdfPageRow> Rows,
    IReadOnlyList<PortablePdfPageColumn> Columns,
    IReadOnlyList<PortablePdfPageCell> Cells)
{
    public bool IsReady => Status == PortablePdfPageContentPlanStatus.Ready;
    public int RowCount => Rows.Count;
    public int ColumnCount => Columns.Count;
}

public static class PortablePdfPageContentPlanner
{
    public static PortablePdfPageContentPlan CreatePlan(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        int exportPageNumber)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(exportPlan);

        PortablePdfExportPageRequest? pageRequest = null;
        foreach (var request in exportPlan.PageRequests)
        {
            if (request.ExportPageNumber != exportPageNumber)
                continue;

            pageRequest = request;
            break;
        }

        return pageRequest is null
            ? PageRequestUnavailable(exportPageNumber)
            : CreatePlan(workbook, pageRequest);
    }

    public static PortablePdfPageContentPlan CreatePlan(
        Workbook workbook,
        PortablePdfExportPageRequest pageRequest)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(pageRequest);

        var sheet = workbook.GetSheet(pageRequest.PrintRange.Start.Sheet);
        if (sheet is null)
        {
            return new PortablePdfPageContentPlan(
                PortablePdfPageContentPlanStatus.SheetUnavailable,
                $"Portable PDF page {pageRequest.ExportPageNumber} references a worksheet that is not available in the workbook.",
                pageRequest,
                [],
                [],
                []);
        }

        var rows = BuildRows(pageRequest.PageSpans);
        var columns = BuildColumns(pageRequest.PageSpans);
        var cells = BuildCells(workbook, sheet, rows, columns);

        return new PortablePdfPageContentPlan(
            PortablePdfPageContentPlanStatus.Ready,
            $"Ready to render portable PDF page {pageRequest.ExportPageNumber}: {rows.Count} rows, {columns.Count} columns, {cells.Count} cells.",
            pageRequest,
            rows,
            columns,
            cells);
    }

    private static PortablePdfPageContentPlan PageRequestUnavailable(int exportPageNumber) =>
        new(
            PortablePdfPageContentPlanStatus.PageRequestUnavailable,
            $"Portable PDF page {exportPageNumber} is not present in the export plan.",
            null,
            [],
            [],
            []);

    private static IReadOnlyList<PortablePdfPageRow> BuildRows(PortablePdfExportPageSpans spans) =>
        spans.TitleRows.Select(row => new PortablePdfPageRow(row, PortablePdfPageAxisRole.Title))
            .Concat(spans.BodyRows.Select(row => new PortablePdfPageRow(row, PortablePdfPageAxisRole.Body)))
            .ToArray();

    private static IReadOnlyList<PortablePdfPageColumn> BuildColumns(PortablePdfExportPageSpans spans) =>
        spans.TitleColumns.Select(column => new PortablePdfPageColumn(column, PortablePdfPageAxisRole.Title))
            .Concat(spans.BodyColumns.Select(column => new PortablePdfPageColumn(column, PortablePdfPageAxisRole.Body)))
            .ToArray();

    private static IReadOnlyList<PortablePdfPageCell> BuildCells(
        Workbook workbook,
        Sheet sheet,
        IReadOnlyList<PortablePdfPageRow> rows,
        IReadOnlyList<PortablePdfPageColumn> columns)
    {
        var cells = new List<PortablePdfPageCell>(rows.Count * columns.Count);

        // R72-render-cf-visual-4-1: precompute the sheet's conditional-format rules (priority order)
        // once for the page, and lazily cache each rule's range statistics, mirroring
        // PageContentRenderModelBuilder.BuildCellBlocks (the print-preview path) so the portable/
        // Avalonia PDF export carries the same color-scale/highlight fills the print preview and the
        // interactive grid already show, instead of falling straight back to the raw style's fill.
        var cfRulesByPriority = BuildConditionalFormatRulesByPriority(sheet);
        var cfStatsCache = new Dictionary<ConditionalFormat, ConditionalFormatStatistics>(ReferenceEqualityComparer.Instance);

        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var address = new CellAddress(sheet.Id, row.Row, column.Column);
                var cell = sheet.GetCell(address);
                var styleId = cell?.StyleId ??
                    sheet.GetStyleOnly(row.Row, column.Column) ??
                    StyleId.Default;

                var conditionalFillColor = cfRulesByPriority.Count > 0
                    ? EvaluateConditionalFormatFill(
                        cfRulesByPriority, sheet, address, cell?.Value ?? BlankValue.Instance, cfStatsCache)
                    : null;

                cells.Add(new PortablePdfPageCell(
                    row.Row,
                    column.Column,
                    GetDisplayText(workbook, sheet, cell, styleId),
                    styleId,
                    row.Role == PortablePdfPageAxisRole.Title,
                    column.Role == PortablePdfPageAxisRole.Title,
                    conditionalFillColor));
            }
        }

        return cells;
    }

    // -----------------------------------------------------------------------
    // Conditional formatting (fill only) — R72-render-cf-visual-4-1
    // -----------------------------------------------------------------------
    //
    // This is deliberately scoped to the fill color a matched rule contributes (color-scale
    // interpolated fill, or a CellValue/AboveAverage highlight rule's FillColor) -- the HIGH-severity
    // data loss the portable PDF export previously had versus the on-screen grid/print-preview. Font
    // deltas, data-bar bars, and icon-set glyphs are NOT reproduced here; that remains a known,
    // deliberately scoped residual gap (see PageContentRenderModelBuilder's own header comment for the
    // equivalent scope note on the print-preview path this mirrors).

    /// <summary>
    /// Sorts the sheet's conditional-format rules by Excel priority order (lower
    /// <see cref="ConditionalFormat.Priority"/> number = higher precedence), ties broken by original
    /// list order -- matching <c>ViewportConditionalFormatEvaluator.CopyRulesByPriority</c> and
    /// <c>PageContentRenderModelBuilder.BuildConditionalFormatRulesByPriority</c>.
    /// </summary>
    private static IReadOnlyList<ConditionalFormat> BuildConditionalFormatRulesByPriority(Sheet sheet)
    {
        if (sheet.ConditionalFormats.Count == 0)
            return [];

        var indexed = new (ConditionalFormat Rule, int Index)[sheet.ConditionalFormats.Count];
        for (var i = 0; i < sheet.ConditionalFormats.Count; i++)
            indexed[i] = (sheet.ConditionalFormats[i], i);

        Array.Sort(indexed, static (a, b) =>
        {
            var priorityOrder = a.Rule.Priority.CompareTo(b.Rule.Priority);
            return priorityOrder != 0 ? priorityOrder : a.Index.CompareTo(b.Index);
        });

        var rules = new ConditionalFormat[indexed.Length];
        for (var i = 0; i < indexed.Length; i++)
            rules[i] = indexed[i].Rule;

        return rules;
    }

    /// <summary>
    /// Evaluates every applicable rule for <paramref name="address"/> in priority order and returns
    /// the fill color of the first style-producing match (first rule to set a fill wins, matching
    /// <c>StackDifferentialStyle</c>'s "first property wins" semantics), or <c>null</c> when no rule
    /// matches. Stops early once a matched rule marks <see cref="ConditionalFormat.StopIfTrue"/>.
    /// </summary>
    private static CellColor? EvaluateConditionalFormatFill(
        IReadOnlyList<ConditionalFormat> rulesByPriority,
        Sheet sheet,
        CellAddress address,
        ScalarValue value,
        Dictionary<ConditionalFormat, ConditionalFormatStatistics> statsCache)
    {
        CellColor? fill = null;

        for (var i = 0; i < rulesByPriority.Count; i++)
        {
            var rule = rulesByPriority[i];
            if (!rule.AllRanges.Any(r => r.Contains(address)))
                continue;

            var conditionMet = EvaluateConditionalFormatRuleFill(rule, sheet, value, statsCache, out var ruleFill);

            if (fill is null && ruleFill is { } matchedFill)
                fill = matchedFill;

            if (conditionMet && rule.StopIfTrue)
                break;
        }

        return fill;
    }

    private static bool EvaluateConditionalFormatRuleFill(
        ConditionalFormat rule,
        Sheet sheet,
        ScalarValue value,
        Dictionary<ConditionalFormat, ConditionalFormatStatistics> statsCache,
        out CellColor? fill)
    {
        fill = null;

        switch (rule.RuleType)
        {
            case CfRuleType.ColorScale:
            {
                if (!TryGetConditionalFormatNumeric(value, out var numeric))
                    return false;
                var scale = ConditionalFormatEvaluator.EvaluateColorScale(
                    rule, numeric, GetConditionalFormatStatistics(rule, sheet, statsCache));
                if (scale is null)
                    return false;
                fill = scale.Value.Fill.ToCellColor();
                return true;
            }
            case CfRuleType.CellValue:
            {
                if (!TryGetConditionalFormatNumeric(value, out var numeric) ||
                    !ConditionalFormatEvaluator.MatchesCellValueNumeric(rule, numeric))
                {
                    return false;
                }
                fill = rule.FormatIfTrue?.FillColor;
                return true;
            }
            case CfRuleType.AboveAverage:
            {
                if (!TryGetConditionalFormatNumeric(value, out var numeric) ||
                    !ConditionalFormatEvaluator.MatchesAboveBelowAverage(
                        rule, numeric, GetConditionalFormatStatistics(rule, sheet, statsCache)))
                {
                    return false;
                }
                fill = rule.FormatIfTrue?.FillColor;
                return true;
            }
            default:
                // Formula / Top10 / Duplicate-Unique / text-match / DateOccurring / Blanks / NoBlanks
                // / Errors / NoErrors / DataBar / IconSet -- fill-color reproduction for these rule
                // types is a known, deliberately scoped gap (see the section header above).
                return false;
        }
    }

    private static ConditionalFormatStatistics GetConditionalFormatStatistics(
        ConditionalFormat rule,
        Sheet sheet,
        Dictionary<ConditionalFormat, ConditionalFormatStatistics> cache)
    {
        if (cache.TryGetValue(rule, out var cached))
            return cached;

        var stats = ConditionalFormatStatistics.FromValues(EnumerateConditionalFormatNumericValues(sheet, rule));
        cache[rule] = stats;
        return stats;
    }

    /// <summary>
    /// Gathers the finite numeric values across a rule's range(s) for range-statistic thresholds
    /// (AboveAverage / ColorScale automatic Min/Max/Percentile), de-duplicating cells shared between
    /// overlapping ranges in a multi-range rule. Mirrors
    /// <c>PageContentRenderModelBuilder.EnumerateConditionalFormatNumericValues</c>'s dense-range-vs-
    /// sparse-scan split so a rule applied to a huge range (e.g. a full column) does not force a
    /// million-cell scan.
    /// </summary>
    private static IEnumerable<double> EnumerateConditionalFormatNumericValues(Sheet sheet, ConditionalFormat rule)
    {
        const long denseScanLimit = 10_000;
        var ranges = rule.AllRanges.ToList();
        var seen = ranges.Count > 1 ? new HashSet<CellAddress>() : null;

        foreach (var range in ranges)
        {
            if (range.CellCount <= denseScanLimit)
            {
                foreach (var cellAddress in range.AllCells())
                {
                    if (seen is not null && !seen.Add(cellAddress))
                        continue;
                    if (TryGetConditionalFormatNumeric(sheet.GetValue(cellAddress), out var numeric))
                        yield return numeric;
                }
            }
            else
            {
                foreach (var (rangeAddress, rangeCell) in sheet.EnumerateCells())
                {
                    if (!range.Contains(rangeAddress))
                        continue;
                    if (seen is not null && !seen.Add(rangeAddress))
                        continue;
                    if (TryGetConditionalFormatNumeric(rangeCell.Value, out var numeric))
                        yield return numeric;
                }
            }
        }
    }

    private static bool TryGetConditionalFormatNumeric(ScalarValue value, out double result)
    {
        switch (value)
        {
            case NumberValue n:
                result = n.Value;
                return double.IsFinite(result);
            case DateTimeValue d:
                result = d.Value;
                return double.IsFinite(result);
            default:
                result = 0;
                return false;
        }
    }

    private static string GetDisplayText(
        Workbook workbook,
        Sheet sheet,
        Cell? cell,
        StyleId styleId)
    {
        if (cell is null)
            return "";

        if (sheet.ShowFormulas && cell.FormulaText is not null)
            return "=" + cell.FormulaText;

        var style = workbook.GetStyle(styleId);
        var displayText = NumberFormatter.FormatWithColor(
            cell.Value,
            style.NumberFormat,
            workbook.IndexedColors,
            workbook.Theme,
            workbook.Uses1904DateSystem).Text;

        // N47: honor Page Setup > Sheet > "Cell errors as" (blank/dashes/#N/A) the same way the WPF
        // PrintRenderer path does via PagePrintTextPlanner.FormatPrintedCellText, so error cells print
        // consistently substituted on the Avalonia/portable PDF path too.
        return PagePrintTextPlanner.FormatPrintedCellText(displayText, sheet.PrintErrorValue);
    }
}
