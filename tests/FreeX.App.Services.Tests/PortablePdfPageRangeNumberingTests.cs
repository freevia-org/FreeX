using FluentAssertions;
using Free.Shared.Pdf;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class PortablePdfPageRangeNumberingTests
{
    [Fact]
    public void WorkbookExportRange_PreservesOriginalNumberingAcrossPrintAreasAndCommentSummary()
    {
        var workbook = new Workbook("Budget");
        var cover = workbook.AddSheet("Cover");
        cover.PrintArea = GridRange.Parse("A1:B2", cover.Id);
        var sheet = workbook.AddSheet("Details");
        sheet.SetPrintAreas([GridRange.Parse("A1:B2", sheet.Id), GridRange.Parse("D1:E2", sheet.Id)]);
        sheet.PrintComments = WorksheetPrintComments.AtEnd;
        sheet.ThreadedComments[new CellAddress(sheet.Id, 2, 2)] = new ThreadedComment("Please review.", "Alice");
        ConfigureFooters(sheet, firstPageNumber: 7);

        var printPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(WorkbookExportPrintScope.VisibleWorkbook, WorkbookExportPrintOutputKind.Pdf),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 20),
            WorkbookExportPrintSurface.MacOs);
        var fullPlan = PortablePdfExportPlanner.CreatePlan(printPlan, workbook);
        fullPlan.TotalPageCount.Should().Be(4);

        var options = ExportOptions.ExcelLikeDefault with { PageRange = new ExportPageRange(2, 3) };
        PortablePdfExportPlanner.TryApplyOptions(fullPlan, options, out var rangePlan, out _)
            .Should().BeTrue();

        rangePlan.PageRequests.Select(page => page.ExportPageNumber).Should().Equal(1, 2);
        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, rangePlan);
        document.Pages[0].Ops.OfType<PdfText>().Should().Contain(text => text.Text == "Even page 8 of 4");
        document.Pages[1].Ops.OfType<PdfText>().Should().Contain(text => text.Text == "Odd page 9 of 4");

        var filteredAgain = PortablePdfExportPlanner.ApplyPageRange(rangePlan, new ExportPageRange(2, 2));
        var finalPage = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, filteredAgain)
            .Pages.Should().ContainSingle().Which;
        finalPage.Ops.OfType<PdfText>().Should().Contain(text => text.Text == "Odd page 9 of 4");
    }

    [Theory]
    [InlineData(WorkbookExportPrintScope.VisibleWorkbook, false, 0)]
    [InlineData(WorkbookExportPrintScope.VisibleWorkbook, true, 2)]
    [InlineData(WorkbookExportPrintScope.GroupedSheets, false, 0)]
    [InlineData(WorkbookExportPrintScope.GroupedSheets, true, 2)]
    public void MultiSheetPrintRange_NumberingIncludesEarlierWorksheetAndItsCommentAppendix(
        WorkbookExportPrintScope scope, bool hasPrinter, int firstDetailPageIndex)
    {
        var workbook = new Workbook("Budget");
        var cover = workbook.AddSheet("Cover");
        cover.PrintArea = GridRange.Parse("A1:B2", cover.Id);
        cover.PrintComments = WorksheetPrintComments.AtEnd;
        cover.Comments[new CellAddress(cover.Id, 1, 1)] = "Review";
        if (scope == WorkbookExportPrintScope.GroupedSheets)
        {
            var excluded = workbook.AddSheet("Excluded");
            excluded.PrintArea = GridRange.Parse("A1:B2", excluded.Id);
        }

        var details = workbook.AddSheet("Details");
        details.PrintArea = GridRange.Parse("A1:B2", details.Id);
        details.RowPageBreaks.Add(2);
        ConfigureFooters(details, firstPageNumber: null);

        var workflow = WorkbookPrintWorkflow.CreatePlan(
            workbook,
            hasSelection: false,
            new PrintJobRequest(
                scope,
                PageRangeKind: PrintJobPageRangeKind.PageRange,
                FromPage: 3,
                ToPage: 4)
            {
                GroupedSheetIds = scope == WorkbookExportPrintScope.GroupedSheets
                    ? new HashSet<SheetId> { cover.Id, details.Id }
                    : null
            },
            PrintExportHostCapabilities.AvaloniaPortable(
                canSubmitToPlatformPrinter: hasPrinter,
                hasPrinterDestination: hasPrinter));

        workflow.Readiness.NativePrintPlan.JobPlan.TotalPageCount.Should().Be(4);
        var pages = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, workflow.PortablePdfPlan!).Pages;
        pages.Should().HaveCount(hasPrinter ? 4 : 2);
        pages[firstDetailPageIndex].Ops.OfType<PdfText>()
            .Should().Contain(text => text.Text == "Odd page 3 of 4");
        pages[firstDetailPageIndex + 1].Ops.OfType<PdfText>()
            .Should().Contain(text => text.Text == "Even page 4 of 4");
    }

    [Theory]
    [InlineData(1, "First page 4 of 3")]
    [InlineData(2, "Odd page 5 of 3")]
    [InlineData(3, "Even page 6 of 3")]
    public void PrinterlessPrintRange_PreservesNumberAndFooterVariant(int requestedPage, string expectedFooter)
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Details");
        sheet.PrintArea = GridRange.Parse("A1:C4", sheet.Id);
        sheet.RowPageBreaks.Add(2);
        sheet.RowPageBreaks.Add(3);
        ConfigureFooters(sheet, firstPageNumber: 4);

        var plan = WorkbookPrintWorkflow.CreatePlan(
            workbook,
            hasSelection: false,
            new PrintJobRequest(
                WorkbookExportPrintScope.ActiveSheet,
                PageRangeKind: PrintJobPageRangeKind.PageRange,
                FromPage: requestedPage,
                ToPage: requestedPage),
            PrintExportHostCapabilities.AvaloniaPortable());

        plan.IsReady.Should().BeTrue();
        var page = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, plan.PortablePdfPlan!)
            .Pages.Should().ContainSingle().Which;
        page.Ops.OfType<PdfText>().Should().Contain(text => text.Text == expectedFooter);
    }

    private static void ConfigureFooters(Sheet sheet, int? firstPageNumber)
    {
        sheet.FirstPageNumber = firstPageNumber;
        sheet.DifferentFirstPageHeaderFooter = true;
        sheet.DifferentOddEvenHeaderFooter = true;
        sheet.PageFooter = new WorksheetHeaderFooter("", "Odd page &P of &N", "");
        sheet.FirstPageFooter = new WorksheetHeaderFooter("", "First page &P of &N", "");
        sheet.EvenPageFooter = new WorksheetHeaderFooter("", "Even page &P of &N", "");
    }
}
