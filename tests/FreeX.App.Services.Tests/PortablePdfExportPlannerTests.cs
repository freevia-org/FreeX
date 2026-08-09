using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class PortablePdfExportPlannerTests
{
    [Fact]
    public void CreatePlan_ExpandsReadyPdfPlanIntoDownThenOverPageRequests()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        var selectedRange = GridRange.Parse("A1:E6", sheet.Id);
        var exportPrintPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.SelectedRange,
                WorkbookExportPrintOutputKind.Pdf,
                SelectedRange: selectedRange),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 3, ColumnsPerPage: 3),
            WorkbookExportPrintSurface.MacOs);

        var plan = PortablePdfExportPlanner.CreatePlan(exportPrintPlan);

        plan.IsReady.Should().BeTrue();
        plan.Status.Should().Be(PortablePdfExportPlanStatus.Ready);
        plan.TotalPageCount.Should().Be(6);
        plan.StatusText.Should().Be("Ready to export portable PDF: 6 pages across 1 sheet.");
        plan.ExportPrintPlan.Should().BeSameAs(exportPrintPlan);
        plan.PageRequests.Select(request => (request.RowPageIndex, request.ColumnPageIndex))
            .Should()
            .Equal(
                (0, 0),
                (1, 0),
                (2, 0),
                (0, 1),
                (1, 1),
                (2, 1));

        var firstPage = plan.PageRequests[0];
        firstPage.ExportPageNumber.Should().Be(1);
        firstPage.SheetIndex.Should().Be(0);
        firstPage.SheetName.Should().Be("Sheet1");
        firstPage.SheetPageNumber.Should().Be(1);
        firstPage.PrintRange.Should().Be(selectedRange);
        firstPage.RangeSource.Should().Be(WorkbookExportPrintRangeSource.SelectedRange);
        firstPage.RowPageCount.Should().Be(3);
        firstPage.ColumnPageCount.Should().Be(2);
        firstPage.RowPageNumber.Should().Be(1);
        firstPage.ColumnPageNumber.Should().Be(1);
        firstPage.PageOrder.Should().Be(WorksheetPageOrder.DownThenOver);
    }

    [Fact]
    public void CreatePlan_IncludesExactTitleAndBodySpansForMultiPageSheet()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        var selectedRange = GridRange.Parse("A1:E6", sheet.Id);
        var exportPrintPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.SelectedRange,
                WorkbookExportPrintOutputKind.Pdf,
                SelectedRange: selectedRange),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 3, ColumnsPerPage: 3),
            WorkbookExportPrintSurface.MacOs);

        var plan = PortablePdfExportPlanner.CreatePlan(exportPrintPlan);

        plan.IsReady.Should().BeTrue();
        plan.PageRequests.Should().HaveCount(6);
        AssertPageSpans(plan.PageRequests[0], new[] { 1u }, new[] { 2u, 3u }, new[] { 1u }, new[] { 2u, 3u });
        AssertPageSpans(plan.PageRequests[1], new[] { 1u }, new[] { 4u, 5u }, new[] { 1u }, new[] { 2u, 3u });
        AssertPageSpans(plan.PageRequests[2], new[] { 1u }, new[] { 6u }, new[] { 1u }, new[] { 2u, 3u });
        AssertPageSpans(plan.PageRequests[3], new[] { 1u }, new[] { 2u, 3u }, new[] { 1u }, new[] { 4u, 5u });
        AssertPageSpans(plan.PageRequests[4], new[] { 1u }, new[] { 4u, 5u }, new[] { 1u }, new[] { 4u, 5u });
        AssertPageSpans(plan.PageRequests[5], new[] { 1u }, new[] { 6u }, new[] { 1u }, new[] { 4u, 5u });

        static void AssertPageSpans(
            PortablePdfExportPageRequest request,
            uint[] titleRows,
            uint[] bodyRows,
            uint[] titleColumns,
            uint[] bodyColumns)
        {
            request.PageSpans.TitleRows.Should().Equal(titleRows);
            request.PageSpans.BodyRows.Should().Equal(bodyRows);
            request.PageSpans.TitleColumns.Should().Equal(titleColumns);
            request.PageSpans.BodyColumns.Should().Equal(bodyColumns);
            request.TitleRows.Should().Equal(titleRows);
            request.BodyRows.Should().Equal(bodyRows);
            request.TitleColumns.Should().Equal(titleColumns);
            request.BodyColumns.Should().Equal(bodyColumns);
        }
    }

    [Fact]
    public void CreatePlan_HonorsOverThenDownSheetPageOrder()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PageOrder = WorksheetPageOrder.OverThenDown;
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        var selectedRange = GridRange.Parse("A1:E6", sheet.Id);
        var exportPrintPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.SelectedRange,
                WorkbookExportPrintOutputKind.Pdf,
                SelectedRange: selectedRange),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 3, ColumnsPerPage: 3),
            WorkbookExportPrintSurface.MacOs);

        var plan = PortablePdfExportPlanner.CreatePlan(exportPrintPlan);

        plan.IsReady.Should().BeTrue();
        plan.PageRequests.Select(request => (request.RowPageIndex, request.ColumnPageIndex))
            .Should()
            .Equal(
                (0, 0),
                (0, 1),
                (1, 0),
                (1, 1),
                (2, 0),
                (2, 1));
        plan.PageRequests.Select(request => request.SheetPageNumber)
            .Should()
            .Equal(1, 2, 3, 4, 5, 6);
    }

    [Fact]
    public void CreatePlan_RejectsReadyXpsPlanForPortablePdf()
    {
        var workbook = new Workbook("Budget");
        workbook.AddSheet("Sheet1").PrintArea = GridRange.Parse("A1:B2", workbook.GetSheetAt(0).Id);
        var exportPrintPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Xps),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 5),
            WorkbookExportPrintSurface.WindowsDesktop);

        var plan = PortablePdfExportPlanner.CreatePlan(exportPrintPlan);

        exportPrintPlan.IsReady.Should().BeTrue();
        plan.IsReady.Should().BeFalse();
        plan.Status.Should().Be(PortablePdfExportPlanStatus.OutputKindUnavailable);
        plan.StatusText.Should().Be("Portable PDF export only accepts PDF export print plans; XPS remains Windows-only.");
        plan.PageRequests.Should().BeEmpty();
    }

    [Fact]
    public void TryApplyOptions_ValidatesAndRenumbersSelectedPageRange()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        var selectedRange = GridRange.Parse("A1:E6", sheet.Id);
        var exportPrintPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.SelectedRange,
                WorkbookExportPrintOutputKind.Pdf,
                SelectedRange: selectedRange),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 3, ColumnsPerPage: 3),
            WorkbookExportPrintSurface.MacOs);
        var plan = PortablePdfExportPlanner.CreatePlan(exportPrintPlan);
        var options = ExportOptions.ExcelLikeDefault with
        {
            PageRange = new ExportPageRange(2, 4)
        };

        var valid = PortablePdfExportPlanner.TryApplyOptions(
            plan,
            options,
            out var effectivePlan,
            out var error);

        valid.Should().BeTrue();
        error.Should().BeNull();
        effectivePlan.PageRequests.Select(page => page.ExportPageNumber).Should().Equal(1, 2, 3);
        effectivePlan.PageRequests.Select(page => page.SheetPageNumber).Should().Equal(2, 3, 4);
        effectivePlan.StatusText.Should().Be("Ready to export portable PDF: 3 pages from selected page range.");
    }

    [Fact]
    public void CreatePlan_ReturnsReadinessFailureForUnreadyExportPrintPlan()
    {
        var workbook = new Workbook("Hidden");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.IsHidden = true;
        var exportPrintPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 5),
            WorkbookExportPrintSurface.MacOs);

        var plan = PortablePdfExportPlanner.CreatePlan(exportPrintPlan);

        plan.IsReady.Should().BeFalse();
        plan.Status.Should().Be(PortablePdfExportPlanStatus.ExportPrintPlanNotReady);
        plan.StatusText.Should().Be("Portable PDF export cannot start because the export print plan is not ready: No visible worksheets are available for local PDF/XPS export.");
        plan.PageRequests.Should().BeEmpty();
    }
}
