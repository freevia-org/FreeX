using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R76-services-print-pagination-4-1: <see cref="SheetPdfPageSetupResolver.ResolveCapacity"/> must add
/// the print-title row/column count back onto the fit-to-pages capacity it derives, mirroring
/// <c>PagePaginationPlanner.ApplyScaleToFitCapacity</c> (which returns <c>bodyItemsPerPage + titleCount</c>).
/// Downstream, <c>PrintLayoutPlanner.BuildAxisPlans</c> treats the resolved capacity as a GROSS per-page
/// count and subtracts the title count again (<c>titleValuesOnPage = min(titles, capacity-1)</c>), so a
/// body-only capacity gets the title count subtracted TWICE, over-paginating sheets that combine
/// Print Titles with "fit to N pages".
/// </summary>
public sealed class R76_PrintTitleFitToPagesCapacityTests
{
    // -----------------------------------------------------------------------
    // Wide-only branch (FitToPagesWide set, FitToPagesTall unset)
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveCapacity_FitToWidthOne_WithPrintTitleColumn_CapacityIncludesTitleColumn()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize   = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;

        // A1:U100 = 21 columns; column A (1) is the repeated print title -> 20 body columns.
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        sheet.ScaleToFit = new WorksheetScaleToFit(null, FitToPagesWide: 1, null);

        var range = GridRange.Parse("A1:U100", sheet.Id);
        var capacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        // Body-only capacity would be ceil(20/1) = 20; the fix adds the 1 title column back so the
        // downstream re-subtraction (capacity - 1) nets out to the correct 20-column page.
        capacity.ColumnsPerPage.Should().Be(21,
            "capacity must be bodyColsPerPage (20) + titleColumnCount (1) so BuildAxisPlans' own " +
            "title subtraction leaves exactly the 20 body columns needed for 1 column-page");
    }

    [Fact]
    public void CreatePlanFromPageSetup_FitToWidthOne_WithPrintTitleColumn_ProducesOneColumnPage()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize   = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;

        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        sheet.ScaleToFit = new WorksheetScaleToFit(null, FitToPagesWide: 1, null);
        sheet.SetPrintAreas([GridRange.Parse("A1:U100", sheet.Id)]);

        for (var col = 1u; col <= 21u; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new TextValue($"C{col}"));

        var plan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: 0),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeTrue(plan.StatusText);
        plan.SheetPlans.Should().HaveCount(1);
        plan.SheetPlans[0].ColumnPageCount.Should().Be(1,
            "Fit to 1 page wide with Print Titles must still collapse onto 1 column page, not " +
            "double-subtract the title column and over-paginate to 2 column pages");
    }

    // -----------------------------------------------------------------------
    // Tall-only branch (FitToPagesTall set, FitToPagesWide unset)
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveCapacity_FitToHeightOne_WithPrintTitleRow_CapacityIncludesTitleRow()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize   = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;

        // A1:J21 = 21 rows; row 1 is the repeated print title -> 20 body rows.
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);
        sheet.ScaleToFit = new WorksheetScaleToFit(null, null, FitToPagesTall: 1);

        var range = GridRange.Parse("A1:J21", sheet.Id);
        var capacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        capacity.RowsPerPage.Should().Be(21,
            "capacity must be bodyRowsPerPage (20) + titleRowCount (1) so BuildAxisPlans' own " +
            "title subtraction leaves exactly the 20 body rows needed for 1 row-page");
    }

    [Fact]
    public void CreatePlanFromPageSetup_FitToHeightOne_WithPrintTitleRow_ProducesOneRowPage()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize   = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;

        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);
        sheet.ScaleToFit = new WorksheetScaleToFit(null, null, FitToPagesTall: 1);
        sheet.SetPrintAreas([GridRange.Parse("A1:J21", sheet.Id)]);

        for (var row = 1u; row <= 21u; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));

        var plan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: 0),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeTrue(plan.StatusText);
        plan.SheetPlans.Should().HaveCount(1);
        plan.SheetPlans[0].RowPageCount.Should().Be(1,
            "Fit to 1 page tall with Print Titles must still collapse onto 1 row page, not " +
            "double-subtract the title row and over-paginate to 2 row pages");
    }

    // -----------------------------------------------------------------------
    // Both-constrained branch (FitToPagesWide AND FitToPagesTall both set)
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveCapacity_BothConstrained_WithPrintTitles_CapacityNeverUndershootsBodyPlusTitleCounts()
    {
        // R100-services-print-scale-uniform-both-axes: since the both-constrained branch now derives
        // ONE uniform scale (the smaller of the two per-axis scales -- see
        // SheetPdfPageSetupResolver.ResolveCapacityDetail and PagePaginationPlanner's matching
        // "wideConstrained && tallConstrained" branch) and applies it to BOTH axes, the axis that
        // needed the aggressive shrink resolves to EXACTLY bodyItemsPerPage + titleCount, while the
        // other (less-constrained) axis gets the SAME scale applied and so resolves to AT LEAST that
        // many items per page (it only ever gains capacity, never loses it, relative to the plain
        // body+title count) -- titles are still never double-subtracted on either axis.
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize   = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;

        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        sheet.PrintTitleRows    = new WorksheetRepeatRange(1, 1);
        sheet.ScaleToFit = new WorksheetScaleToFit(null, FitToPagesWide: 1, FitToPagesTall: 1);

        // 21 cols x 21 rows: 20 body columns, 20 body rows, once titles are excluded.
        var range = GridRange.Parse("A1:U21", sheet.Id);
        var capacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        capacity.ColumnsPerPage.Should().BeGreaterThanOrEqualTo(21,
            "the resolved column capacity must be at least bodyColsPerPage (20) + titleColumnCount (1) " +
            "-- never less, or the print title column would be double-subtracted downstream");
        capacity.RowsPerPage.Should().BeGreaterThanOrEqualTo(21,
            "the resolved row capacity must be at least bodyRowsPerPage (20) + titleRowCount (1) -- " +
            "never less, or the print title row would be double-subtracted downstream");
    }

    [Fact]
    public void CreatePlanFromPageSetup_BothConstrained_WithPrintTitles_ProducesOnePageEachAxis()
    {
        // Real-entry-point regression for the same both-constrained + print-titles scenario: the
        // uniform-scale coupling must still let "Fit to 1 wide x 1 tall" collapse the titled sheet
        // onto exactly one page in each direction, not double-paginate from title double-subtraction.
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize   = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;

        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        sheet.PrintTitleRows    = new WorksheetRepeatRange(1, 1);
        sheet.ScaleToFit = new WorksheetScaleToFit(null, FitToPagesWide: 1, FitToPagesTall: 1);
        sheet.SetPrintAreas([GridRange.Parse("A1:U21", sheet.Id)]);

        for (var row = 1u; row <= 21u; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));
        for (var col = 1u; col <= 21u; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new TextValue($"C{col}"));

        var plan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: 0),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeTrue(plan.StatusText);
        plan.SheetPlans.Should().HaveCount(1);
        plan.SheetPlans[0].ColumnPageCount.Should().Be(1,
            "Fit to 1x1 with Print Titles on both axes must still collapse onto a single column page");
        plan.SheetPlans[0].RowPageCount.Should().Be(1,
            "Fit to 1x1 with Print Titles on both axes must still collapse onto a single row page");
    }

    // -----------------------------------------------------------------------
    // Sibling / no-regression: no print titles -> capacity unchanged (+0)
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveCapacity_FitToWidthOne_NoPrintTitles_CapacityUnchanged()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize   = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;

        // No PrintTitleColumns/Rows set at all.
        sheet.ScaleToFit = new WorksheetScaleToFit(null, FitToPagesWide: 1, null);

        var range = GridRange.Parse("A1:T50", sheet.Id);
        var capacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        // 20 columns, no titles to exclude or add back: body = 20, capacity = 20 + 0.
        capacity.ColumnsPerPage.Should().Be(20,
            "with no print titles the +titleCount term is 0, so capacity is unchanged from the " +
            "existing body-only result");
    }

    [Fact]
    public void ResolveCapacity_FitToHeightOne_NoPrintTitles_CapacityUnchanged()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize   = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;

        sheet.ScaleToFit = new WorksheetScaleToFit(null, null, FitToPagesTall: 1);

        var range = GridRange.Parse("A1:J50", sheet.Id);
        var capacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        capacity.RowsPerPage.Should().Be(50,
            "with no print titles the +titleCount term is 0, so capacity is unchanged from the " +
            "existing body-only result");
    }
}
