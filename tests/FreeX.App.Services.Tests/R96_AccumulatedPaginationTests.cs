using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R96-services-print-pagination-exact: the Avalonia/portable-PDF print-plan path
/// (<see cref="WorkbookExportPrintPlanner.CreatePlanFromPageSetup"/>, backed by
/// <see cref="SheetPdfPageSetupResolver.ResolvePagination"/>) must break pages on the real
/// ACCUMULATED per-row height / per-column width -- matching Excel and FreeX's own WPF
/// <c>PagePaginationPlanner.BuildPlan</c> -- instead of a fixed items-per-page count derived from the
/// AVERAGE row height / column width across the whole print range.
///
/// Pre-fix, <c>SheetPdfPageSetupResolver.ResolveCapacity</c> fed a single average-derived
/// rows/columns-per-page COUNT into <c>PrintLayoutPlanner.BuildRowPlans/BuildColumnPlans</c>, which
/// slices by plain count with no size accumulation. A print range with one wildly oversized row (or
/// column) dragged the AVERAGE size so far up that the resolved capacity collapsed to 1 item per page --
/// putting every row on its own page, wildly over-paginating the sheet -- instead of Excel's real
/// behavior: the oversized row alone gets a page, and every other (normal-sized) row packs normally
/// onto the following pages.
/// </summary>
public sealed class R96_AccumulatedPaginationTests
{
    // -----------------------------------------------------------------------
    // Row axis: the failing case (bug) + fixed behavior
    // -----------------------------------------------------------------------

    [Fact]
    public void CreatePlanFromPageSetup_OneOversizedRowAmongDefaultRows_PaginatesByAccumulatedHeight()
    {
        var workbook = new Workbook("W");
        var sheet = workbook.AddSheet("S");
        sheet.PaperSize = WorksheetPaperSize.A4;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.ScaleToFit = WorksheetScaleToFit.Default;

        // Discover the sheet's natural (uniform, default-height) rows-per-page capacity N using a
        // large uniform probe range -- the same approach R20/R50 use.
        var probeRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 500, 1));
        var natural = SheetPdfPageSetupResolver.ResolveCapacity(sheet, probeRange);
        var n = natural.RowsPerPage;
        n.Should().BeGreaterThan(2u, "the test needs several natural default-height rows per page");

        // Build a real print range of exactly N rows: row 1 is monstrously oversized (a wrapped-text
        // or picture-anchor row in the real product), rows 2..N are all default height (20px, unset).
        for (var row = 1u; row <= n; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));
        sheet.RowHeights[1] = 5_000_000.0; // dwarfs the printable page height on its own.

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

        var plan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent, WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeTrue(plan.StatusText);
        plan.SheetPlans.Should().HaveCount(1);

        // Real Excel (and FreeX's WPF PagePaginationPlanner path) accumulate real row heights: row 1
        // alone exceeds the printable body height and must sit alone on page 1; the remaining N-1
        // default-height rows all comfortably fit together on page 2 (their total height is strictly
        // less than what N default rows -- the whole printable height -- would take). Total = 2 pages.
        plan.SheetPlans[0].RowPageCount.Should().Be(2,
            "the oversized row 1 must get its own page and the remaining N-1 default rows must pack " +
            "onto a single following page, matching Excel's real accumulated-size pagination -- not " +
            "the average-height-derived fixed count, which (dragged up by the one huge row) collapses " +
            $"to 1 row per page and would wrongly produce {n} pages (one per row)");

        // The first page must contain ONLY row 1 (the oversized row), not any of the following rows.
        var firstPageRows = plan.SheetPlans[0].RowPagePlans[0].BodyRows;
        firstPageRows.Should().ContainSingle().Which.Should().Be(1u);

        // The second page must contain all the remaining rows (2..N) together.
        var secondPageRows = plan.SheetPlans[0].RowPagePlans[1].BodyRows;
        secondPageRows.Should().HaveCount((int)(n - 1));
        secondPageRows.Should().BeEquivalentTo(Enumerable.Range(2, (int)(n - 1)).Select(r => (uint)r));
    }

    // -----------------------------------------------------------------------
    // Column axis sibling: identical bug pattern on the column axis
    // -----------------------------------------------------------------------

    [Fact]
    public void CreatePlanFromPageSetup_OneOversizedColumnAmongDefaultColumns_PaginatesByAccumulatedWidth()
    {
        var workbook = new Workbook("W");
        var sheet = workbook.AddSheet("S");
        sheet.PaperSize = WorksheetPaperSize.A4;
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.ScaleToFit = WorksheetScaleToFit.Default;

        var probeRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 500));
        var natural = SheetPdfPageSetupResolver.ResolveCapacity(sheet, probeRange);
        var n = natural.ColumnsPerPage;
        n.Should().BeGreaterThan(2u, "the test needs several natural default-width columns per page");

        for (var col = 1u; col <= n; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new TextValue($"C{col}"));
        sheet.ColumnWidths[1] = 500_000.0; // character-width units; dwarfs the printable page width.

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

        var plan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent, WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeTrue(plan.StatusText);
        plan.SheetPlans.Should().HaveCount(1);

        plan.SheetPlans[0].ColumnPageCount.Should().Be(2,
            "the oversized column 1 must get its own page and the remaining N-1 default columns must " +
            $"pack onto a single following page -- not the average-width-derived fixed count, which " +
            $"would wrongly produce {n} pages (one per column)");

        var firstPageColumns = plan.SheetPlans[0].ColumnPagePlans[0].BodyColumns;
        firstPageColumns.Should().ContainSingle().Which.Should().Be(1u);

        var secondPageColumns = plan.SheetPlans[0].ColumnPagePlans[1].BodyColumns;
        secondPageColumns.Should().HaveCount((int)(n - 1));
    }

    // -----------------------------------------------------------------------
    // No-regression sibling: uniform sheet keeps the SAME page count as before the fix
    // -----------------------------------------------------------------------

    [Fact]
    public void CreatePlanFromPageSetup_UniformRows_PageCountUnchangedFromAverageBasedResult()
    {
        var workbook = new Workbook("W");
        var sheet = workbook.AddSheet("S");
        sheet.PaperSize = WorksheetPaperSize.A4;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.ScaleToFit = WorksheetScaleToFit.Default;

        var probeRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 500, 1));
        var natural = SheetPdfPageSetupResolver.ResolveCapacity(sheet, probeRange);
        var n = natural.RowsPerPage;
        n.Should().BeGreaterThan(2u);

        // Exactly 3 natural pages' worth of uniform, default-height rows -- no oversized outliers.
        var totalRows = n * 3;
        for (var row = 1u; row <= totalRows; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

        var plan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent, WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeTrue(plan.StatusText);
        plan.SheetPlans.Should().HaveCount(1);

        // For a perfectly uniform sheet, average*count == accumulated sum, so the accumulation-based
        // fix must produce the EXACT SAME page count as the pre-fix average-based count: 3 pages.
        plan.SheetPlans[0].RowPageCount.Should().Be(3,
            "a uniform sheet (no oversized rows) must paginate identically whether pages are sliced " +
            "by the average-derived fixed count or the real accumulated size -- the fix must not " +
            "change behavior for the common case");
    }
}
