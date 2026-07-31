using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// Tests for PR1 (real row heights / column widths in pagination) and PR5 (header+footer margin
/// subtraction from body height). Also includes a regression guard that verifies a default-sized sheet
/// (20px rows, 8.43-char columns, zero header/footer margins) produces the same page count as before
/// the fix.
/// </summary>
public sealed class PagePaginationAccuracyTests
{
    // ── shared helpers ────────────────────────────────────────────────────────────────────────────

    private static readonly Dictionary<uint, double> EmptyDict = new();

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheetId = SheetId.New();
        return new GridRange(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
    }

    /// <summary>
    /// Builds a uniform row-height dictionary: every row in [startRow, endRow] → heightPx.
    /// </summary>
    private static Dictionary<uint, double> UniformRowHeights(uint startRow, uint endRow, double heightPx)
    {
        var dict = new Dictionary<uint, double>();
        for (var r = startRow; r <= endRow; r++)
            dict[r] = heightPx;
        return dict;
    }

    /// <summary>
    /// Builds a uniform column-width dictionary: every column in [startCol, endCol] → widthChars.
    /// </summary>
    private static Dictionary<uint, double> UniformColWidths(uint startCol, uint endCol, double widthChars)
    {
        var dict = new Dictionary<uint, double>();
        for (var c = startCol; c <= endCol; c++)
            dict[c] = widthChars;
        return dict;
    }

    // ── PR1: tall rows produce more pages ─────────────────────────────────────────────────────────

    [Fact]
    public void CalculatePageCapacity_TallRowsYieldFewerRowsPerPage()
    {
        // A4 portrait, narrow margins.
        // Default rows (20 px): floor(printableH / 20).
        // Tall rows (60 px): floor(printableH / 60) — should be 1/3 as many rows per page.
        var range = Range(1, 1, 200, 5);
        var margins = WorksheetPageMargins.Narrow;
        var noScale = WorksheetScaleToFit.Default;
        var defaultColWidth = ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth);

        var defaultCapacity = PagePaginationPlanner.CalculatePageCapacity(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            margins,
            rowHeights: EmptyDict,
            defaultRowHeight: 20.0,
            columnWidths: EmptyDict,
            defaultColumnWidth: defaultColWidth,
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        var tallCapacity = PagePaginationPlanner.CalculatePageCapacity(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            margins,
            rowHeights: UniformRowHeights(1, 200, 60.0),
            defaultRowHeight: 60.0,
            columnWidths: EmptyDict,
            defaultColumnWidth: defaultColWidth,
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        // 60px rows give 1/3 as many rows per page as 20px rows.
        tallCapacity.RowsPerPage.Should().BeLessThan(defaultCapacity.RowsPerPage);
        // Approximately 3× fewer rows per page at 60px vs 20px.
        ((double)defaultCapacity.RowsPerPage / tallCapacity.RowsPerPage).Should().BeApproximately(3.0, precision: 0.5);
    }

    [Fact]
    public void Paginate_TallRowsProduceMorePagesThanDefaultRows()
    {
        // 60 rows at 60px per row vs 60 rows at 20px per row (default).
        // Expect more row pages when rows are taller.
        var range = Range(1, 1, 60, 5);
        var margins = WorksheetPageMargins.Narrow;
        var noScale = new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: null, FitToPagesTall: null);
        var defaultColWidth = ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth);

        var defaultResult = PagePaginationPlanner.Paginate(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            margins,
            rowHeights: EmptyDict,
            defaultRowHeight: 20.0,
            columnWidths: EmptyDict,
            defaultColumnWidth: defaultColWidth,
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        var tallResult = PagePaginationPlanner.Paginate(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            margins,
            rowHeights: UniformRowHeights(1, 60, 60.0),
            defaultRowHeight: 60.0,
            columnWidths: EmptyDict,
            defaultColumnWidth: defaultColWidth,
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        tallResult.RowPageCount.Should().BeGreaterThan(defaultResult.RowPageCount,
            because: "60px rows take 3x more vertical space so they produce more pages");
    }

    [Fact]
    public void Paginate_TallRowsPageCountMatchesExpectedFromRealHeight()
    {
        // A4 portrait, narrow (0.5") margins.
        // Paper: 11.69", margins 0.5"+0.5" = 1.0"; printable = 10.69" = 1026.24px (at 96dpi).
        // Row height: 60px. RowsPerPage = floor(1026.24 / 60) = 17.
        // 60 rows / 17 per page = ceil(60/17) = 4 pages.
        const uint rowCount = 60;
        const double rowHeightPx = 60.0;
        var range = Range(1, 1, rowCount, 5);
        var margins = WorksheetPageMargins.Narrow; // 0.5 each side
        var noScale = new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: null, FitToPagesTall: null);

        // Compute expected: printableHeight for A4 portrait, narrow margins.
        const double pageHeightIn = 11.69;
        const double topMarginIn = 0.5;
        const double bottomMarginIn = 0.5;
        const double dpi = 96.0;
        var printableHeightPx = (pageHeightIn - topMarginIn - bottomMarginIn) * dpi;
        var rowsPerPage = (uint)Math.Floor(printableHeightPx / rowHeightPx);
        var expectedRowPages = (int)Math.Ceiling(rowCount / (double)rowsPerPage);

        var result = PagePaginationPlanner.Paginate(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            margins,
            rowHeights: UniformRowHeights(1, rowCount, rowHeightPx),
            defaultRowHeight: rowHeightPx,
            columnWidths: EmptyDict,
            defaultColumnWidth: ColumnWidthPixelMapper.PixelsToColumnWidth(40.0),
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        result.RowPageCount.Should().Be(expectedRowPages,
            because: $"{rowCount} rows at {rowHeightPx}px each fit {rowsPerPage} per page and need {expectedRowPages} pages");
    }

    // ── PR1: wide columns produce more pages horizontally ─────────────────────────────────────────

    [Fact]
    public void Paginate_WideColumnsProduceMoreColumnPages()
    {
        // Narrow cols (8.43 chars ~ 64px) vs very wide cols (40 chars ~ 285px).
        var range = Range(1, 1, 5, 20);
        var margins = WorksheetPageMargins.Narrow;
        var noScale = new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: null, FitToPagesTall: null);

        var narrowResult = PagePaginationPlanner.Paginate(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            margins,
            rowHeights: EmptyDict,
            defaultRowHeight: 20.0,
            columnWidths: UniformColWidths(1, 20, 8.43),
            defaultColumnWidth: 8.43,
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        var wideResult = PagePaginationPlanner.Paginate(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            margins,
            rowHeights: EmptyDict,
            defaultRowHeight: 20.0,
            columnWidths: UniformColWidths(1, 20, 40.0),
            defaultColumnWidth: 40.0,
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        wideResult.ColumnPageCount.Should().BeGreaterThan(narrowResult.ColumnPageCount,
            because: "very wide columns occupy much more horizontal space and need more column pages");
    }

    [Fact]
    public void Paginate_FitToOneWideScaleAccountsForRealTotalColumnWidth()
    {
        // When FitToPagesWide=1, the planner tries to collapse all columns onto one page -- but only
        // within Excel's [10%, 400%] scale range (R103-print-pagination-scale-bound-1). Here 30
        // columns @ 285px each (40-char width) against an A4/Narrow ~745.92px body and an 18-
        // columns/page baseline would need an 18/30 = 6.67% shrink to literally hit 1 page, which is
        // below Excel's 10% floor -- so real Excel (and this planner, post-fix) floors the scale at
        // 10% and lets the sheet spread across 2 pages instead of crushing all 30 columns onto one
        // unreadable page. This is the exact defect scenario the fix addresses, just with a wide-
        // column axis instead of a wide-range axis.
        var range = Range(1, 1, 5, 30);
        var margins = WorksheetPageMargins.Narrow;
        var fitWide1 = new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: 1, FitToPagesTall: null);

        var result = PagePaginationPlanner.Paginate(
            range,
            fitWide1,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            margins,
            rowHeights: EmptyDict,
            defaultRowHeight: 20.0,
            columnWidths: UniformColWidths(1, 30, 40.0), // very wide
            defaultColumnWidth: 40.0,
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        result.ColumnPageCount.Should().Be(2,
            because: "the literal fit-to-1-wide request would need an unbounded 6.67% shrink below " +
            "Excel's 10% floor, so the scale is floored at 10% and the sheet correctly spills onto a " +
            "2nd column page instead of crushing all 30 wide columns onto a single unreadable page");
        // Even floored at 10%, the effective scale (derived from the ACTUAL resulting page count) is
        // still <= 100 -- it is a shrink, just not as extreme as the literal (unbounded) request.
        result.EffectiveScalePercent.Should().BeLessThanOrEqualTo(100.0,
            because: "fit-to-1-wide must shrink content that exceeds a single page width");
    }

    // ── PR1: AverageRowHeightPixels / AverageColumnWidthPixels helpers ────────────────────────────

    [Fact]
    public void AverageRowHeightPixels_AllDefaultRows_ReturnsDefaultHeight()
    {
        // No overrides -> average = defaultRowHeight.
        var avg = PagePaginationPlanner.AverageRowHeightPixels(1, 10, EmptyDict, defaultRowHeight: 25.0);
        avg.Should().Be(25.0);
    }

    [Fact]
    public void AverageRowHeightPixels_MixOfOverridesAndDefaults_ReturnsWeightedAverage()
    {
        // Rows 1-5: override to 60px. Rows 6-10: default 20px.
        // Average = (5*60 + 5*20) / 10 = 40px.
        var overrides = UniformRowHeights(1, 5, 60.0);
        var avg = PagePaginationPlanner.AverageRowHeightPixels(1, 10, overrides, defaultRowHeight: 20.0);
        avg.Should().Be(40.0);
    }

    [Fact]
    public void AverageColumnWidthPixels_AllDefaultCols_ReturnsPixelsOfDefaultWidth()
    {
        // 8.43 chars -> 8.43*7+5 = 64.01 ~ 64px (rounded away from zero).
        var expectedPx = ColumnWidthPixelMapper.ColumnWidthToPixels(8.43);
        var avg = PagePaginationPlanner.AverageColumnWidthPixels(1, 10, EmptyDict, defaultColumnWidth: 8.43);
        avg.Should().BeApproximately(expectedPx, precision: 0.01);
    }

    [Fact]
    public void AverageColumnWidthPixels_WideOverrides_ReflectsActualPixelWidth()
    {
        // Override all columns to 40 chars -> 40*7+5 = 285px; minimum clamp = 40px; result = 285px.
        var widePx = Math.Max(40.0, ColumnWidthPixelMapper.ColumnWidthToPixels(40.0));
        var overrides = UniformColWidths(1, 5, 40.0);
        var avg = PagePaginationPlanner.AverageColumnWidthPixels(1, 5, overrides, defaultColumnWidth: 8.43);
        avg.Should().BeApproximately(widePx, precision: 0.1);
    }

    // ── PR5: header+footer margins reduce per-page body capacity ──────────────────────────────────

    [Fact]
    public void CalculatePageCapacity_LargeHeaderFooterMarginsReduceRowsPerPage()
    {
        // A4 portrait, narrow margins.
        // With header+footer = 1" each (192px total), body height shrinks -> fewer rows per page.
        var range = Range(1, 1, 200, 5);
        var margins = WorksheetPageMargins.Narrow;
        var noScale = WorksheetScaleToFit.Default;

        var noHeaderFooter = PagePaginationPlanner.CalculatePageCapacity(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            margins,
            rowHeights: EmptyDict,
            defaultRowHeight: 20.0,
            columnWidths: EmptyDict,
            defaultColumnWidth: 8.43,
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        var withHeaderFooter = PagePaginationPlanner.CalculatePageCapacity(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            margins,
            rowHeights: EmptyDict,
            defaultRowHeight: 20.0,
            columnWidths: EmptyDict,
            defaultColumnWidth: 8.43,
            headerMarginInches: 1.0,
            footerMarginInches: 1.0);

        withHeaderFooter.RowsPerPage.Should().BeLessThan(noHeaderFooter.RowsPerPage,
            because: "header+footer margin of 1 inch each reserves 192px of vertical space, leaving less room for rows");
    }

    [Fact]
    public void Paginate_LargeHeaderFooterMarginsProduceMoreRowPages()
    {
        // 48 rows at 20px each. A4 portrait narrow margins (0.75"+0.75" top/bottom) give a printable
        // body of 978.24px, so with zero header/footer margin all 48 rows fit on 1 page
        // (floor(978.24/20) = 48 rows/page). With 1"+1" header/footer margin, body height drops to
        // 786.24px -> only 39 rows/page fit, so the same 48 rows need 2 pages.
        const uint rowCount = 48;
        var range = Range(1, 1, rowCount, 5);
        var margins = WorksheetPageMargins.Narrow;
        var noScale = new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: null, FitToPagesTall: null);

        var noMarginResult = PagePaginationPlanner.Paginate(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            margins,
            rowHeights: EmptyDict,
            defaultRowHeight: 20.0,
            columnWidths: EmptyDict,
            defaultColumnWidth: 8.43,
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        var largeMarginResult = PagePaginationPlanner.Paginate(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            margins,
            rowHeights: EmptyDict,
            defaultRowHeight: 20.0,
            columnWidths: EmptyDict,
            defaultColumnWidth: 8.43,
            headerMarginInches: 1.0,
            footerMarginInches: 1.0);

        largeMarginResult.RowPageCount.Should().BeGreaterThan(noMarginResult.RowPageCount,
            because: "large header+footer margins eat into body height so fewer rows fit per page and more pages are needed");
    }

    [Fact]
    public void CalculatePageCapacity_HeaderFooterMarginsSubtractedCorrectly()
    {
        // R88-services-page-setup-margins-5-1: corrected to Excel's actual margin-guide model. The
        // header/footer margin is the distance from the page edge to the header/footer band, which
        // sits WITHIN the top/bottom margin band as long as it doesn't exceed it -- Excel does not
        // reserve additional space on top of the top/bottom margins for it.
        //
        // A4 portrait, narrow (Excel: 0.25" left/right, 0.75" top/bottom) margins.
        // Header = 0.3", Footer = 0.3" -- both SMALLER than the 0.75" top/bottom margins, so they fit
        // entirely within the margin band and reserve nothing extra.
        // Body height = paper height - top margin - bottom margin = 11.69 - 0.75 - 0.75 = 10.19" = 978.24px.
        // Rows per page (at 20px) = floor(978.24 / 20) = 48.
        const double pageH = 11.69;
        const double topMarginIn = 0.75;
        const double bottomMarginIn = 0.75;
        const double headerIn = 0.3;
        const double footerIn = 0.3;
        const double dpi = 96.0;
        const double rowH = 20.0;

        var bodyTopIn = Math.Max(topMarginIn, headerIn);
        var bodyBottomIn = Math.Max(bottomMarginIn, footerIn);
        var bodyH = (pageH - bodyTopIn - bodyBottomIn) * dpi;
        var expectedRows = (uint)Math.Floor(bodyH / rowH);

        var capacity = PagePaginationPlanner.CalculatePageCapacity(
            Range(1, 1, 200, 5),
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeights: EmptyDict,
            defaultRowHeight: rowH,
            columnWidths: EmptyDict,
            defaultColumnWidth: 8.43,
            headerMarginInches: headerIn,
            footerMarginInches: footerIn);

        capacity.RowsPerPage.Should().Be(expectedRows,
            because: $"body height = {bodyH:F2}px; at {rowH}px/row -> {expectedRows} rows fit");
    }

    // ── Regression: default-sized sheet page count is unchanged ───────────────────────────────────

    [Fact]
    public void CalculatePageCapacity_DefaultSizedSheet_SameResultAsOriginalConstantBehavior()
    {
        // The old (short) overload uses NominalRowHeight=20 and MinimumPrintColumnWidth=40 with no
        // header/footer margin. The new overload with the same effective parameters must give the same
        // capacity. This is the regression guard.
        // A4 portrait narrow -- matches existing test: 51 rows, 17 cols.
        var range = Range(1, 1, 100, 100);
        var margins = WorksheetPageMargins.Narrow;
        var noScale = WorksheetScaleToFit.Default;

        var legacyCapacity = PagePaginationPlanner.CalculatePageCapacity(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            margins);

        // New overload with exactly the same effective sizes.
        var newCapacity = PagePaginationPlanner.CalculatePageCapacity(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            margins,
            rowHeights: EmptyDict,
            defaultRowHeight: PagePaginationPlanner.NominalRowHeight,         // 20.0px
            columnWidths: EmptyDict,
            defaultColumnWidth: ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth), // chars that round-trip to 40px
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        newCapacity.Should().Be(legacyCapacity,
            because: "the new overload with default sizes and zero header/footer margins must reproduce the old fixed-constant result exactly");
    }

    [Fact]
    public void Paginate_DefaultSheet_LegacyAndNewOverloadProduceSamePageGrid()
    {
        // Verify that Paginate (the short, no-sizing overload) and the new sizing overload with
        // default sizes produce identical page grids for a default-sized sheet.
        var range = Range(1, 1, 70, 20);
        var margins = WorksheetPageMargins.Narrow;
        var noScale = new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: null, FitToPagesTall: null);

        var legacyResult = PagePaginationPlanner.Paginate(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            margins);

        var newResult = PagePaginationPlanner.Paginate(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            margins,
            rowHeights: EmptyDict,
            defaultRowHeight: PagePaginationPlanner.NominalRowHeight,
            columnWidths: EmptyDict,
            defaultColumnWidth: ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth),
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        newResult.RowPageCount.Should().Be(legacyResult.RowPageCount);
        newResult.ColumnPageCount.Should().Be(legacyResult.ColumnPageCount);
        newResult.PageCount.Should().Be(legacyResult.PageCount);
        newResult.RowSegments.Should().BeEquivalentTo(legacyResult.RowSegments);
        newResult.ColumnSegments.Should().BeEquivalentTo(legacyResult.ColumnSegments);
        newResult.EffectiveScalePercent.Should().Be(legacyResult.EffectiveScalePercent);
    }
}
