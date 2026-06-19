using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class PrintJobPlannerTests
{
    private static Workbook BuildWorkbookWithPrintArea(string area = "A1:E6")
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintArea = GridRange.Parse(area, sheet.Id);
        return workbook;
    }

    private static WorkbookExportPrintPageCapacity Capacity(uint rows = 3, uint cols = 3) =>
        new(rows, cols);

    [Fact]
    public void CreatePlan_AllPages_ReadyAndSpansEveryProducedPage()
    {
        var workbook = BuildWorkbookWithPrintArea();

        var plan = PrintJobPlanner.CreatePlan(
            workbook,
            new PrintJobRequest(WorkbookExportPrintScope.ActiveSheet),
            Capacity(),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeTrue();
        // 6 rows / 3 per page = 2 row pages; 5 cols / 3 per page = 2 col pages => 4 pages.
        plan.TotalPageCount.Should().Be(4);
        plan.FirstPage.Should().Be(1);
        plan.LastPage.Should().Be(4);
        plan.SelectedPageCount.Should().Be(4);
        plan.Copies.Should().Be(1);
        plan.TotalSheetsToPrint.Should().Be(4);
        plan.StatusText.Should().Contain("all 4 pages");
    }

    [Fact]
    public void CreatePlan_CopiesMultiplyTotalSheets()
    {
        var workbook = BuildWorkbookWithPrintArea();

        var plan = PrintJobPlanner.CreatePlan(
            workbook,
            new PrintJobRequest(WorkbookExportPrintScope.ActiveSheet, Copies: 3, Collate: false),
            Capacity(),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeTrue();
        plan.Copies.Should().Be(3);
        plan.Collate.Should().BeFalse();
        plan.TotalSheetsToPrint.Should().Be(12);
        plan.StatusText.Should().Contain("3 copies (uncollated)");
    }

    [Fact]
    public void CreatePlan_ZeroCopies_IsInvalid()
    {
        var workbook = BuildWorkbookWithPrintArea();

        var plan = PrintJobPlanner.CreatePlan(
            workbook,
            new PrintJobRequest(WorkbookExportPrintScope.ActiveSheet, Copies: 0),
            Capacity(),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeFalse();
        plan.ValidationStatus.Should().Be(PrintJobValidationStatus.InvalidCopyCount);
    }

    [Fact]
    public void CreatePlan_PageRange_TrimsToInclusiveWindow()
    {
        var workbook = BuildWorkbookWithPrintArea();

        var plan = PrintJobPlanner.CreatePlan(
            workbook,
            new PrintJobRequest(
                WorkbookExportPrintScope.ActiveSheet,
                PageRangeKind: PrintJobPageRangeKind.PageRange,
                FromPage: 2,
                ToPage: 3),
            Capacity(),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeTrue();
        plan.FirstPage.Should().Be(2);
        plan.LastPage.Should().Be(3);
        plan.SelectedPageCount.Should().Be(2);
        plan.StatusText.Should().Contain("pages 2-3 of 4");
    }

    [Fact]
    public void CreatePlan_PageRange_OpenEndedExtendsToLastPage()
    {
        var workbook = BuildWorkbookWithPrintArea();

        var plan = PrintJobPlanner.CreatePlan(
            workbook,
            new PrintJobRequest(
                WorkbookExportPrintScope.ActiveSheet,
                PageRangeKind: PrintJobPageRangeKind.PageRange,
                FromPage: 3,
                ToPage: null),
            Capacity(),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeTrue();
        plan.FirstPage.Should().Be(3);
        plan.LastPage.Should().Be(4);
    }

    [Fact]
    public void CreatePlan_PageRange_OutOfBounds_IsInvalid()
    {
        var workbook = BuildWorkbookWithPrintArea();

        var plan = PrintJobPlanner.CreatePlan(
            workbook,
            new PrintJobRequest(
                WorkbookExportPrintScope.ActiveSheet,
                PageRangeKind: PrintJobPageRangeKind.PageRange,
                FromPage: 2,
                ToPage: 99),
            Capacity(),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeFalse();
        plan.ValidationStatus.Should().Be(PrintJobValidationStatus.InvalidPageRange);
        plan.StatusText.Should().Contain("between 1 and 4");
    }

    [Fact]
    public void CreatePlan_PageRange_InvertedWindow_IsInvalid()
    {
        var workbook = BuildWorkbookWithPrintArea();

        var plan = PrintJobPlanner.CreatePlan(
            workbook,
            new PrintJobRequest(
                WorkbookExportPrintScope.ActiveSheet,
                PageRangeKind: PrintJobPageRangeKind.PageRange,
                FromPage: 3,
                ToPage: 2),
            Capacity(),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeFalse();
        plan.ValidationStatus.Should().Be(PrintJobValidationStatus.InvalidPageRange);
    }

    [Fact]
    public void CreatePlan_NothingPrintable_ReportsDocumentNotPrintable()
    {
        var workbook = new Workbook("Empty");
        workbook.AddSheet("Sheet1");

        var plan = PrintJobPlanner.CreatePlan(
            workbook,
            new PrintJobRequest(WorkbookExportPrintScope.ActiveSheet),
            Capacity(),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeFalse();
        plan.ValidationStatus.Should().Be(PrintJobValidationStatus.DocumentNotPrintable);
    }

    [Fact]
    public void CreatePlan_SelectedRangeScope_UsesSuppliedRange()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        var selectedRange = GridRange.Parse("A1:C3", sheet.Id);

        var plan = PrintJobPlanner.CreatePlan(
            workbook,
            new PrintJobRequest(
                WorkbookExportPrintScope.SelectedRange,
                SelectedRange: selectedRange),
            Capacity(rows: 10, cols: 10),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeTrue();
        plan.TotalPageCount.Should().Be(1);
    }
}
