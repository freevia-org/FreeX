using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for H19 (K-print review group): <see cref="WorkbookExportPrintPlanner"/> must
/// exclude hidden/filter-hidden/group-hidden rows and columns from the row/column page plans it feeds
/// into PDF export, matching what <see cref="WorksheetPrintRenderPlanner"/> already does for on-screen
/// print and the WPF print path.
/// </summary>
public sealed class WorkbookExportPrintPlannerHiddenRowsTests
{
    [Fact]
    public void CreatePlan_ExcludesHiddenRowsFromRowPagePlans()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintArea = GridRange.Parse("A1:A10", sheet.Id);
        sheet.HiddenRows.Add(5);
        sheet.HiddenRows.Add(6);

        var plan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 5),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeTrue();
        var sheetPlan = plan.SheetPlans.Should().ContainSingle().Subject;
        var printedRows = sheetPlan.RowPagePlans.SelectMany(rowPlan => rowPlan.BodyRows).ToList();
        printedRows.Should().NotContain(5u);
        printedRows.Should().NotContain(6u);
        printedRows.Should().Contain([1u, 2u, 3u, 4u, 7u, 8u, 9u, 10u]);
    }

    [Fact]
    public void CreatePlan_ExcludesFilterHiddenColumnsFromColumnPagePlans()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintArea = GridRange.Parse("A1:F1", sheet.Id);
        sheet.HiddenCols.Add(3);

        var plan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 20),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeTrue();
        var sheetPlan = plan.SheetPlans.Should().ContainSingle().Subject;
        var printedColumns = sheetPlan.ColumnPagePlans.SelectMany(colPlan => colPlan.BodyColumns).ToList();
        printedColumns.Should().NotContain(3u);
        printedColumns.Should().Contain([1u, 2u, 4u, 5u, 6u]);
    }

    [Fact]
    public void CreatePlanFromPageSetup_ExcludesHiddenRowsFromRowPagePlans()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintArea = GridRange.Parse("A1:A10", sheet.Id);
        sheet.HiddenRows.Add(5);
        sheet.HiddenRows.Add(6);

        var plan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf));

        plan.IsReady.Should().BeTrue();
        var sheetPlan = plan.SheetPlans.Should().ContainSingle().Subject;
        var printedRows = sheetPlan.RowPagePlans.SelectMany(rowPlan => rowPlan.BodyRows).ToList();
        printedRows.Should().NotContain(5u);
        printedRows.Should().NotContain(6u);
        printedRows.Should().Contain([1u, 2u, 3u, 4u, 7u, 8u, 9u, 10u]);
    }
}
