using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Tests that multi-area print areas produce one set of pages per area (each area on its own page)
/// in both the export print planner (PDF path) and related planners.
/// </summary>
public sealed class MultiAreaPrintAreaPaginationTests
{
    [Fact]
    public void ExportPrintPlanner_MultiAreaPrintArea_ProducesAtLeastOneSheetPlanPerArea()
    {
        var workbook = new Workbook("Multi-Area PDF");
        var sheet = workbook.AddSheet("Sheet1");
        PopulateCells(sheet, 1, 5, 1, 3);  // area 1: A1:C5
        PopulateCells(sheet, 1, 5, 5, 7);  // area 2: E1:G5

        var area1 = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 3));
        var area2 = new GridRange(
            new CellAddress(sheet.Id, 1, 5),
            new CellAddress(sheet.Id, 5, 7));
        sheet.SetPrintAreas([area1, area2]);

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);
        var capacity = new WorkbookExportPrintPageCapacity(RowsPerPage: 50, ColumnsPerPage: 50);

        var plan = WorkbookExportPrintPlanner.CreatePlan(workbook, intent, capacity);

        plan.IsReady.Should().BeTrue();
        // Two areas → two sheet-range entries → at least 2 pages (each area ≥ 1 page).
        plan.SheetPlans.Should().HaveCountGreaterThanOrEqualTo(2,
            "each print area should produce at least one sheet plan entry");
        plan.TotalPageCount.Should().BeGreaterThanOrEqualTo(2,
            "each print area should yield at least one page");
    }

    [Fact]
    public void ExportPrintPlanner_SinglePrintArea_ProducesOneSheetPlan()
    {
        var workbook = new Workbook("Single-Area PDF");
        var sheet = workbook.AddSheet("Sheet1");
        PopulateCells(sheet, 1, 5, 1, 3);

        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 3));

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);
        var capacity = new WorkbookExportPrintPageCapacity(RowsPerPage: 50, ColumnsPerPage: 50);

        var plan = WorkbookExportPrintPlanner.CreatePlan(workbook, intent, capacity);

        plan.IsReady.Should().BeTrue();
        plan.SheetPlans.Should().HaveCount(1);
    }

    [Fact]
    public void ExportPrintPlanner_MultiArea_RangeSourceIsPrintArea()
    {
        var workbook = new Workbook("Multi-Area Source");
        var sheet = workbook.AddSheet("Sheet1");
        PopulateCells(sheet, 1, 3, 1, 2);
        PopulateCells(sheet, 1, 3, 4, 5);

        var area1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        var area2 = new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 3, 5));
        sheet.SetPrintAreas([area1, area2]);

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);
        var capacity = new WorkbookExportPrintPageCapacity(RowsPerPage: 50, ColumnsPerPage: 50);

        var plan = WorkbookExportPrintPlanner.CreatePlan(workbook, intent, capacity);

        plan.IsReady.Should().BeTrue();
        plan.SheetPlans.Should().OnlyContain(p => p.RangeSource == WorkbookExportPrintRangeSource.PrintArea);
    }

    [Fact]
    public void ExportPrintPlanner_ThreePrintAreas_ProducesAtLeastThreePages()
    {
        var workbook = new Workbook("Three-Area PDF");
        var sheet = workbook.AddSheet("Sheet1");
        PopulateCells(sheet, 1, 3, 1, 2);
        PopulateCells(sheet, 1, 3, 4, 5);
        PopulateCells(sheet, 1, 3, 7, 8);

        sheet.SetPrintAreas([
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 3, 5)),
            new GridRange(new CellAddress(sheet.Id, 1, 7), new CellAddress(sheet.Id, 3, 8)),
        ]);

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);
        var capacity = new WorkbookExportPrintPageCapacity(RowsPerPage: 50, ColumnsPerPage: 50);

        var plan = WorkbookExportPrintPlanner.CreatePlan(workbook, intent, capacity);

        plan.IsReady.Should().BeTrue();
        plan.TotalPageCount.Should().BeGreaterThanOrEqualTo(3,
            "three print areas should yield at least three pages");
    }

    private static void PopulateCells(Sheet sheet, uint rowFrom, uint rowTo, uint colFrom, uint colTo)
    {
        for (var r = rowFrom; r <= rowTo; r++)
        for (var c = colFrom; c <= colTo; c++)
            sheet.SetCell(new CellAddress(sheet.Id, r, c), new TextValue($"R{r}C{c}"));
    }
}
