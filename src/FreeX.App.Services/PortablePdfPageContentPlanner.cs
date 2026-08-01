using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
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
    CellColor? ConditionalFillColor = null,
    // R96-render-cf-databar-iconset-1: the resolved data-bar/icon-set conditional format for this
    // cell (first matching rule of each kind, by priority), carried the same way ConditionalFillColor
    // already is, so WorkbookPdfContentBuilder can paint the bar/glyph instead of silently dropping it
    // (see PageContentRenderModelBuilder's identical PageCellBlock.DataBar/IconSet fields, which this
    // mirrors for the PDF export path).
    DataBarLayout? DataBar = null,
    IconSetResult? IconSet = null)
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

        // R112-pdf-width-overflow-1: precompute each page column's character-width budget once
        // (from the sheet's real column width, same source ComputeActualGridSizes already reads for
        // PDF column geometry) so GetDisplayText can reproduce Excel's '#' overflow indicator for an
        // over-wide numeric/date value -- mirroring ViewportService.GetColumnWidthPixels /
        // EstimateCharacterWidth (the interactive grid) and PageContentRenderModelBuilder's identical
        // print-path estimate, which both already pass this into NumberFormatter.FormatWithColor.
        var columnWidthChars = new Dictionary<uint, int>(columns.Count);
        foreach (var column in columns)
            columnWidthChars.TryAdd(column.Column, EstimateCharacterWidth(GetColumnWidthPixels(sheet, column.Column)));

        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var address = new CellAddress(sheet.Id, row.Row, column.Column);
                var cell = sheet.GetCell(address);
                var styleId = cell?.StyleId ??
                    sheet.GetStyleOnly(row.Row, column.Column) ??
                    StyleId.Default;

                var cfResult = cfRulesByPriority.Count > 0
                    ? EvaluateConditionalFormat(
                        cfRulesByPriority, sheet, address, cell?.Value ?? BlankValue.Instance, cfStatsCache)
                    : default;

                cells.Add(new PortablePdfPageCell(
                    row.Row,
                    column.Column,
                    GetDisplayText(workbook, sheet, cell, styleId, columnWidthChars[column.Column]),
                    styleId,
                    row.Role == PortablePdfPageAxisRole.Title,
                    column.Role == PortablePdfPageAxisRole.Title,
                    cfResult.Fill,
                    cfResult.DataBar,
                    cfResult.IconSet));
            }
        }

        return cells;
    }

    // -----------------------------------------------------------------------
    // Conditional formatting (fill, data bar, icon set) — R72-render-cf-visual-4-1 / R96-render-cf-databar-iconset-1
    // -----------------------------------------------------------------------
    //
    // R72 originally scoped this to the fill color a matched rule contributes (color-scale
    // interpolated fill, or a CellValue/AboveAverage highlight rule's FillColor). R96 extends it to
    // also resolve DataBar/IconSet rule results (mirroring PageContentRenderModelBuilder's
    // EvaluateConditionalFormat) so WorkbookPdfContentBuilder can paint the bar/glyph instead of
    // silently dropping it. Font deltas from CellValue/AboveAverage rules are still not reproduced
    // (a raw-style-only residual gap versus the interactive grid); formula-driven rule types (Top10,
    // Duplicate/Unique, text-match, DateOccurring, Blanks/NoBlanks/Errors/NoErrors) remain unevaluated.

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

    /// <summary>The accumulated conditional-format result for one cell: fill, data bar, icon set.</summary>
    private readonly record struct CfCellResult(CellColor? Fill, DataBarLayout? DataBar, IconSetResult? IconSet);

    /// <summary>
    /// Evaluates every applicable rule for <paramref name="address"/> in priority order, taking the
    /// fill color of the first style-producing match (first rule to set a fill wins, matching
    /// <c>StackDifferentialStyle</c>'s "first property wins" semantics) and the first matching
    /// DataBar/IconSet rule of each kind (Excel shows at most one bar and one icon set per cell),
    /// mirroring <c>PageContentRenderModelBuilder.EvaluateConditionalFormat</c>. Stops early once a
    /// matched rule marks <see cref="ConditionalFormat.StopIfTrue"/>.
    /// </summary>
    private static CfCellResult EvaluateConditionalFormat(
        IReadOnlyList<ConditionalFormat> rulesByPriority,
        Sheet sheet,
        CellAddress address,
        ScalarValue value,
        Dictionary<ConditionalFormat, ConditionalFormatStatistics> statsCache)
    {
        CellColor? fill = null;
        DataBarLayout? dataBar = null;
        IconSetResult? iconSet = null;

        for (var i = 0; i < rulesByPriority.Count; i++)
        {
            var rule = rulesByPriority[i];
            if (!rule.AllRanges.Any(r => r.Contains(address)))
                continue;

            var conditionMet = EvaluateConditionalFormatRule(
                rule, sheet, value, statsCache, out var ruleFill, out var ruleDataBar, out var ruleIconSet);

            if (fill is null && ruleFill is { } matchedFill)
                fill = matchedFill;
            if (dataBar is null && ruleDataBar is { } matchedDataBar)
                dataBar = matchedDataBar;
            if (iconSet is null && ruleIconSet is { } matchedIconSet)
                iconSet = matchedIconSet;

            if (conditionMet && rule.StopIfTrue)
                break;
        }

        return new CfCellResult(fill, dataBar, iconSet);
    }

    private static bool EvaluateConditionalFormatRule(
        ConditionalFormat rule,
        Sheet sheet,
        ScalarValue value,
        Dictionary<ConditionalFormat, ConditionalFormatStatistics> statsCache,
        out CellColor? fill,
        out DataBarLayout? dataBar,
        out IconSetResult? iconSet)
    {
        fill = null;
        dataBar = null;
        iconSet = null;

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
            case CfRuleType.DataBar:
            {
                // A data bar always renders for every finite numeric cell in its range, matching
                // PageContentRenderModelBuilder's identical DataBar case -- the condition is
                // independent of whether a bar could actually be computed, so a Stop-If-True
                // data-bar rule still suppresses lower-priority rules even when its own bar does not
                // render.
                if (!TryGetConditionalFormatNumeric(value, out var numeric))
                    return false;
                dataBar = ConditionalFormatEvaluator.EvaluateDataBar(
                    rule, numeric, GetConditionalFormatStatistics(rule, sheet, statsCache));
                return true;
            }
            case CfRuleType.IconSet:
            {
                if (!TryGetConditionalFormatNumeric(value, out var numeric))
                    return false;
                iconSet = ConditionalFormatEvaluator.EvaluateIconSet(
                    rule, numeric, GetConditionalFormatStatistics(rule, sheet, statsCache));
                return true;
            }
            default:
                // Formula / Top10 / Duplicate-Unique / text-match / DateOccurring / Blanks / NoBlanks
                // / Errors / NoErrors -- fill-color reproduction for these rule types is a known,
                // deliberately scoped gap (see the section header above).
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
        StyleId styleId,
        int targetWidthCharacters)
    {
        if (cell is null)
            return "";

        if (sheet.ShowFormulas && cell.FormulaText is not null)
            return "=" + cell.FormulaText;

        var style = workbook.GetStyle(styleId);
        // R112-pdf-width-overflow-1: pass the page column's character-width budget (and honor
        // ShrinkToFit the same way ViewportService.GetDisplayText does -- Excel never shows '####'
        // when the cell shrinks its font to fit instead) so an over-wide numeric/date value renders
        // Excel's '#' overflow indicator here too, instead of the raw digits silently overflowing
        // into the neighboring cell on the PDF page.
        var displayText = NumberFormatter.FormatWithColor(
            cell.Value,
            style.NumberFormat,
            targetWidthCharacters,
            workbook.IndexedColors,
            workbook.Theme,
            workbook.Uses1904DateSystem,
            suppressWidthOverflowIndicator: style.ShrinkToFit).Text;

        // N47: honor Page Setup > Sheet > "Cell errors as" (blank/dashes/#N/A) the same way the WPF
        // PrintRenderer path does via PagePrintTextPlanner.FormatPrintedCellText, so error cells print
        // consistently substituted on the Avalonia/portable PDF path too.
        return PagePrintTextPlanner.FormatPrintedCellText(displayText, sheet.PrintErrorValue);
    }

    /// <summary>
    /// The page column's raw pixel width, mirroring <c>ViewportService.GetColumnWidthPixels</c> and
    /// the identical read <c>WorkbookPdfContentBuilder.ComputeActualGridSizes</c> already performs
    /// for PDF column geometry (sheet.ColumnWidths, falling back to DefaultColumnWidth).
    /// </summary>
    private static double GetColumnWidthPixels(Sheet sheet, uint col) =>
        Math.Max(1, ColumnWidthPixelMapper.ColumnWidthToPixels(sheet.ColumnWidths.GetValueOrDefault(col, sheet.DefaultColumnWidth)));

    /// <summary>
    /// Converts a column's pixel width to an approximate character-width budget, matching
    /// <c>ViewportService.EstimateCharacterWidth</c> / <c>PageContentRenderModelBuilder.EstimateCharacterWidth</c>
    /// (~7 pixels/character above 12px, else pixels/12) so the PDF export's overflow detection agrees
    /// with the interactive grid and the print path.
    /// </summary>
    private static int EstimateCharacterWidth(double pixelWidth)
    {
        if (!double.IsFinite(pixelWidth) || pixelWidth <= 0)
            return 1;

        var width = pixelWidth <= 12
            ? pixelWidth / 12.0
            : (pixelWidth - 5.0) / 7.0;
        return Math.Max(1, (int)Math.Round(width, MidpointRounding.AwayFromZero));
    }
}
