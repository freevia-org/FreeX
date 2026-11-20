using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookExportPrintPlannerTests
{
    [Theory]
    [InlineData(WorkbookExportPrintScope.ActiveSheet)]
    [InlineData(WorkbookExportPrintScope.VisibleWorkbook)]
    public void CreatePlanFromPageSetup_UsesA1ForBlankWorksheets(WorkbookExportPrintScope scope)
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Blank");
        var printArea = GridRange.Parse("B2:C3", sheet.Id);
        sheet.PrintArea = printArea;
        var intent = new WorkbookExportPrintIntent(scope, WorkbookExportPrintOutputKind.Pdf);

        var configuredArea = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        var explicitPlan = configuredArea.SheetPlans.Should().ContainSingle().Which;
        explicitPlan.PrintRange.Should().Be(printArea);
        explicitPlan.RangeSource.Should().Be(WorkbookExportPrintRangeSource.PrintArea);

        intent = intent with { IgnorePrintAreas = true };
        var plan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        plan.IsReady.Should().BeTrue();
        plan.TotalPageCount.Should().Be(1);
        var sheetPlan = plan.SheetPlans.Should().ContainSingle().Which;
        sheetPlan.PrintRange.Should().Be(GridRange.Parse("A1:A1", sheet.Id));
        sheetPlan.RangeSource.Should().Be(WorkbookExportPrintRangeSource.UsedRange);

        var fixedCapacityPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook, intent, new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 5));
        fixedCapacityPlan.ValidationStatus.Should().Be(WorkbookExportPrintValidationStatus.NoPrintableRanges);
    }

    [Fact]
    public void CreatePlanFromPageSetup_IncludesOnlyPrintableVisibleWorksheetsInWorkbookOrder()
    {
        var workbook = new Workbook("Budget");
        var cover = workbook.AddSheet("Cover");
        cover.SetCell(CellAddress.Parse("A1", cover.Id), new TextValue("Overview"));
        workbook.AddSheet("Hidden").IsHidden = true;
        workbook.AddSheet("Blank");
        workbook.AddSheet("Very hidden").IsVeryHidden = true;
        var hiddenArea = workbook.AddSheet("Hidden print area");
        hiddenArea.PrintArea = GridRange.Parse("B2:C2", hiddenArea.Id);
        hiddenArea.HiddenRows.Add(2);
        workbook.AddSheet("Blank appendix");

        var plan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook, new WorkbookExportPrintIntent(WorkbookExportPrintScope.VisibleWorkbook, WorkbookExportPrintOutputKind.Pdf));

        plan.SheetPlans.Select(sheet => sheet.SheetName).Should().Equal("Cover", "Blank", "Blank appendix");
        plan.TotalPageCount.Should().Be(3);
    }

    [Fact]
    public void CreatePlan_PreparesXpsOnMacOs()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintArea = GridRange.Parse("A1:B2", sheet.Id);

        var plan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Xps),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 5),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeTrue();
        plan.ExportReadiness.IsReady.Should().BeTrue();
        plan.ValidationStatus.Should().Be(WorkbookExportPrintValidationStatus.Ready);
        plan.SupportedOutputKinds.Should().Equal(WorkbookExportPrintOutputKind.Pdf, WorkbookExportPrintOutputKind.Xps);
        plan.SheetPlans.Should().ContainSingle().Which.PrintRange.Should().Be(sheet.PrintArea);
        plan.TotalPageCount.Should().Be(1);
    }

    [Fact]
    public void CreatePlan_BuildsSelectedRangePageSummaryFromPrintLayoutPrimitives()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        var selectedRange = GridRange.Parse("A1:E6", sheet.Id);

        var plan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.SelectedRange,
                WorkbookExportPrintOutputKind.Pdf,
                SelectedRange: selectedRange),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 3, ColumnsPerPage: 3),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeTrue();
        plan.ExportReadiness.StatusText.Should().Contain("selected range");
        plan.ValidationStatus.Should().Be(WorkbookExportPrintValidationStatus.Ready);
        plan.TotalPageCount.Should().Be(6);
        plan.StatusText.Should().Be("Ready to plan PDF export on macOS for selected range: 1 sheet and 6 pages.");

        var sheetPlan = plan.SheetPlans.Should().ContainSingle().Subject;
        sheetPlan.SheetName.Should().Be("Sheet1");
        sheetPlan.PrintRange.Should().Be(selectedRange);
        sheetPlan.RangeSource.Should().Be(WorkbookExportPrintRangeSource.SelectedRange);
        sheetPlan.RowPageCount.Should().Be(3);
        sheetPlan.ColumnPageCount.Should().Be(2);
        sheetPlan.PageCount.Should().Be(6);
        sheetPlan.RowCount.Should().Be(6);
        sheetPlan.ColumnCount.Should().Be(5);
        sheetPlan.RowPagePlans.Should().HaveCount(3);
        sheetPlan.RowPagePlans[0].TitleRows.Should().Equal(1u);
        sheetPlan.RowPagePlans[0].BodyRows.Should().Equal(2u, 3u);
        sheetPlan.RowPagePlans[1].TitleRows.Should().Equal(1u);
        sheetPlan.RowPagePlans[1].BodyRows.Should().Equal(4u, 5u);
        sheetPlan.RowPagePlans[2].TitleRows.Should().Equal(1u);
        sheetPlan.RowPagePlans[2].BodyRows.Should().Equal(6u);
        sheetPlan.ColumnPagePlans.Should().HaveCount(2);
        sheetPlan.ColumnPagePlans[0].TitleColumns.Should().Equal(1u);
        sheetPlan.ColumnPagePlans[0].BodyColumns.Should().Equal(2u, 3u);
        sheetPlan.ColumnPagePlans[1].TitleColumns.Should().Equal(1u);
        sheetPlan.ColumnPagePlans[1].BodyColumns.Should().Equal(4u, 5u);
        sheetPlan.PageOrder.Should().Be(WorksheetPageOrder.DownThenOver);
    }

    [Fact]
    public void CreatePlan_UsesPrintAreaUnlessIntentIgnoresPrintAreas()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintArea = GridRange.Parse("B2:C3", sheet.Id);
        sheet.SetCell(CellAddress.Parse("A1", sheet.Id), new NumberValue(1));
        sheet.SetCell(CellAddress.Parse("E7", sheet.Id), new NumberValue(2));

        var printAreaPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 10, ColumnsPerPage: 10));
        var ignoredPrintAreaPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                IgnorePrintAreas: true),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 10, ColumnsPerPage: 10));

        var printAreaSheet = printAreaPlan.SheetPlans.Should().ContainSingle().Subject;
        printAreaSheet.RangeSource.Should().Be(WorkbookExportPrintRangeSource.PrintArea);
        printAreaSheet.PrintRange.ToString().Should().Be("B2:C3");

        var usedRangeSheet = ignoredPrintAreaPlan.SheetPlans.Should().ContainSingle().Subject;
        usedRangeSheet.RangeSource.Should().Be(WorkbookExportPrintRangeSource.UsedRange);
        usedRangeSheet.PrintRange.ToString().Should().Be("A1:E7");
    }

    [Fact]
    public void CreatePlan_ReportsValidationStatusBeforeCallingPrintLayoutForInvalidCapacity()
    {
        var workbook = new Workbook("Budget");
        workbook.AddSheet("Sheet1").PrintArea = GridRange.Parse("A1:B2", workbook.GetSheetAt(0).Id);

        var plan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 0, ColumnsPerPage: 5));

        plan.IsReady.Should().BeFalse();
        plan.ValidationStatus.Should().Be(WorkbookExportPrintValidationStatus.InvalidPageCapacity);
        plan.StatusText.Should().Be("Export print planning requires at least one row and one column per page.");
        plan.SheetPlans.Should().BeEmpty();
    }

    [Fact]
    public void CreatePlan_RequiresSelectedRangeForSelectedRangeScope()
    {
        var workbook = new Workbook("Budget");
        workbook.AddSheet("Sheet1").PrintArea = GridRange.Parse("A1:B2", workbook.GetSheetAt(0).Id);

        var plan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.SelectedRange,
                WorkbookExportPrintOutputKind.Pdf),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 10, ColumnsPerPage: 10));

        plan.IsReady.Should().BeFalse();
        plan.ExportReadiness.IsReady.Should().BeTrue();
        plan.ValidationStatus.Should().Be(WorkbookExportPrintValidationStatus.SelectedRangeRequired);
        plan.StatusText.Should().Be("Select a range before planning selected-range export.");
        plan.SheetPlans.Should().BeEmpty();
    }
}
