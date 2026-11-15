using FluentAssertions;
using Free.Shared.AppServices.Printing;
using Free.Shared.Pdf;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookPrintWorkflowTests
{
    [Fact]
    public async Task ExecutePortableAsync_PrinterRouteRendersThenSubmitsValidatedJob()
    {
        var events = new List<string>();
        var printService = new RecordingPrintService(events);
        var workbook = PrintableWorkbook();
        var plan = WorkbookPrintWorkflow.CreatePlan(
            workbook,
            hasSelection: false,
            new PrintJobRequest(WorkbookExportPrintScope.ActiveSheet, Copies: 2, Collate: false),
            PrintExportHostCapabilities.AvaloniaPortable(
                canSubmitToPlatformPrinter: true,
                hasPrinterDestination: true));

        var result = await WorkbookPrintWorkflow.ExecutePortableAsync(
            plan,
            printerId: "office",
            jobTitle: "Budget",
            (portable, _) =>
            {
                events.Add("render");
                portable.IsReady.Should().BeTrue();
                return Task.FromResult(new WorkbookPrintRenderResult(
                    [1, 2, 3],
                    ["picture warning"]));
            },
            printService,
            (_, _) => throw new InvalidOperationException("Printer route must not save fallback."));

        result.Succeeded.Should().BeTrue();
        result.StatusText.Should().Be("Sent to office.");
        result.RenderedDocument!.ImageDiagnostics.Should().Equal("picture warning");
        events.Should().Equal("render", "submit");
        printService.SubmittedPath.Should().NotBeNull();
        File.Exists(printService.SubmittedPath!).Should().BeFalse();
    }

    [Fact]
    public async Task ExecutePortableAsync_NoPrinterRoutesRenderedBytesToFallback()
    {
        var workbook = PrintableWorkbook();
        var plan = WorkbookPrintWorkflow.CreatePlan(
            workbook,
            hasSelection: false,
            new PrintJobRequest(WorkbookExportPrintScope.ActiveSheet),
            PrintExportHostCapabilities.AvaloniaPortable());

        var result = await WorkbookPrintWorkflow.ExecutePortableAsync(
            plan,
            printerId: null,
            jobTitle: "Budget",
            (_, _) => Task.FromResult(new WorkbookPrintRenderResult([4, 5], [])),
            new UnexpectedPrintService(),
            (bytes, _) =>
            {
                bytes.Should().Equal(4, 5);
                return Task.FromResult(WorkbookPrintFallbackResult.Success("saved", "print.pdf"));
            });

        result.Succeeded.Should().BeTrue();
        result.Fallback!.Path.Should().Be("print.pdf");
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    public void CreatePlan_KeepsOnlyTheSelectedPagesInThePdfFallback(bool hasPrinter, int expectedPdfPages)
    {
        var workbook = PrintableWorkbook();
        var sheet = workbook.Sheets[0];
        sheet.RowPageBreaks.Add(2);
        sheet.RowPageBreaks.Add(3);

        var plan = WorkbookPrintWorkflow.CreatePlan(
            workbook,
            hasSelection: false,
            new PrintJobRequest(
                WorkbookExportPrintScope.ActiveSheet,
                PageRangeKind: PrintJobPageRangeKind.PageRange,
                FromPage: 2,
                ToPage: 2),
            PrintExportHostCapabilities.AvaloniaPortable(
                canSubmitToPlatformPrinter: hasPrinter,
                hasPrinterDestination: hasPrinter));

        plan.Readiness.NativePrintPlan.JobPlan.Should().Match<PrintJobPlan>(job =>
            job.IsReady && job.TotalPageCount == 3 && job.FirstPage == 2 && job.LastPage == 2);
        plan.PortablePdfPlan!.TotalPageCount.Should().Be(expectedPdfPages);
        if (!hasPrinter)
        {
            var outputPage = plan.PortablePdfPlan.PageRequests.Should().ContainSingle().Which;
            outputPage.ExportPageNumber.Should().Be(1);
            outputPage.SheetPageNumber.Should().Be(2);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreatePlan_PrintsTheSelectedCommentPageInWorkbookOrder(bool hasPrinter)
    {
        var workbook = PrintableWorkbook();
        var sheet = workbook.Sheets[0];
        sheet.PrintComments = WorksheetPrintComments.AtEnd;
        sheet.SetPrintAreas([GridRange.Parse("A1:B2", sheet.Id), GridRange.Parse("D4:E5", sheet.Id)]);
        sheet.HiddenRows.Add(2);
        sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "Printed first-area note";
        sheet.Comments[new CellAddress(sheet.Id, 4, 4)] = "Printed second-area note";
        sheet.Comments[new CellAddress(sheet.Id, 2, 1)] = "Hidden row note";
        sheet.Comments[new CellAddress(sheet.Id, 4, 1)] = "Between print areas";
        var secondSheet = workbook.AddSheet("Second worksheet");
        secondSheet.SetCell(new CellAddress(secondSheet.Id, 1, 1), new TextValue("Next worksheet"));

        var plan = WorkbookPrintWorkflow.CreatePlan(
            workbook,
            hasSelection: false,
            new PrintJobRequest(
                WorkbookExportPrintScope.VisibleWorkbook,
                PageRangeKind: PrintJobPageRangeKind.PageRange,
                FromPage: 3,
                ToPage: 3),
            PrintExportHostCapabilities.AvaloniaPortable(
                canSubmitToPlatformPrinter: hasPrinter,
                hasPrinterDestination: hasPrinter));

        var job = plan.Readiness.NativePrintPlan.JobPlan;
        job.IsReady.Should().BeTrue();
        job.TotalPageCount.Should().Be(4);
        job.FirstPage.Should().Be(3);
        job.LastPage.Should().Be(3);
        var pdfPlan = plan.PortablePdfPlan!;
        pdfPlan.TotalPageCount.Should().Be(hasPrinter ? 4 : 1);
        var pageIndex = hasPrinter ? 2 : 0;
        var appendix = pdfPlan.PageRequests[pageIndex];
        appendix.IsCommentSummaryPage.Should().BeTrue();
        appendix.CommentSummaryEntries.Select(entry => entry.Text)
            .Should().Equal("Printed first-area note", "Printed second-area note");

        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);
        document.Pages[pageIndex].Ops.OfType<PdfText>().Select(run => run.Text)
            .Should().Equal("Comments", "A1: Printed first-area note", "D4: Printed second-area note");

        using var portablePdf = new MemoryStream();
        PortablePdfDocumentExporter.Save(workbook, pdfPlan, portablePdf);
        var pdf = System.Text.Encoding.Latin1.GetString(portablePdf.ToArray());
        pdf.Should().Contain("A1: Printed first-area note").And.Contain("D4: Printed second-area note");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreatePlan_PrintsTheSelectedBlankPageWithItsPageSetup(bool hasPrinter)
    {
        var workbook = PrintableWorkbook();
        var blank = workbook.AddSheet("Blank");
        blank.PaperSize = WorksheetPaperSize.Letter;
        blank.PageOrientation = WorksheetPageOrientation.Landscape;
        blank.PageFooter = new WorksheetHeaderFooter("", "Page &P of &N", "");
        var appendix = workbook.AddSheet("Appendix");
        appendix.SetCell(CellAddress.Parse("A1", appendix.Id), new TextValue("Appendix content"));

        var plan = WorkbookPrintWorkflow.CreatePlan(
            workbook,
            hasSelection: false,
            new PrintJobRequest(
                WorkbookExportPrintScope.VisibleWorkbook,
                PageRangeKind: PrintJobPageRangeKind.PageRange,
                FromPage: 2,
                ToPage: 2),
            PrintExportHostCapabilities.AvaloniaPortable(
                canSubmitToPlatformPrinter: hasPrinter,
                hasPrinterDestination: hasPrinter));

        var job = plan.Readiness.NativePrintPlan.JobPlan;
        job.IsReady.Should().BeTrue();
        job.TotalPageCount.Should().Be(3);
        job.FirstPage.Should().Be(2);
        job.LastPage.Should().Be(2);
        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, plan.PortablePdfPlan!);
        document.Pages.Should().HaveCount(hasPrinter ? 3 : 1);
        var page = document.Pages[hasPrinter ? 1 : 0];
        page.WidthPoints.Should().Be(792);
        page.HeightPoints.Should().Be(612);
        page.Ops.OfType<PdfText>().Select(run => run.Text).Should().Equal("Page 2 of 3");
    }

    [Fact]
    public void CreatePlan_MatchesWindowsCommentAppendixPagination()
    {
        var workbook = PrintableWorkbook();
        var sheet = workbook.Sheets[0];
        sheet.PrintComments = WorksheetPrintComments.AtEnd;
        sheet.PageMargins = WorksheetPageMargins.Normal;
        sheet.PrintArea = GridRange.Parse("A1:AI1", sheet.Id);
        for (uint col = 1; col <= 35; col++)
            sheet.Comments[new CellAddress(sheet.Id, 1, col)] = "Note";

        WorksheetPrintRenderPlanner.TryBuild(sheet, null, false, out var windowsPlan).Should().BeTrue();
        var windowsSummaryPages = WorksheetPrintPageContentPlanner.BuildCommentSummaryPages(sheet, windowsPlan!);
        windowsSummaryPages.Should().ContainSingle();

        var plan = WorkbookPrintWorkflow.CreatePlan(
            workbook, hasSelection: false, new PrintJobRequest(WorkbookExportPrintScope.ActiveSheet),
            PrintExportHostCapabilities.AvaloniaPortable());
        plan.PortablePdfPlan!.PageRequests.Where(request => request.IsCommentSummaryPage)
            .Should().ContainSingle().Which.CommentSummaryEntries
            .Should().Equal(windowsSummaryPages[0].Entries);
    }

    [Fact]
    public async Task ExecutePortableAsync_InvalidRequestStopsBeforeRendering()
    {
        var workbook = PrintableWorkbook();
        var plan = WorkbookPrintWorkflow.CreatePlan(
            workbook,
            hasSelection: false,
            new PrintJobRequest(WorkbookExportPrintScope.ActiveSheet, Copies: 0),
            PrintExportHostCapabilities.AvaloniaPortable());

        var result = await WorkbookPrintWorkflow.ExecutePortableAsync(
            plan,
            printerId: null,
            jobTitle: "Budget",
            (_, _) => throw new InvalidOperationException("Invalid print must not render."),
            new UnexpectedPrintService(),
            (_, _) => throw new InvalidOperationException("Invalid print must not save."));

        result.Outcome.Should().Be(WorkbookPrintExecutionOutcome.NotReady);
        result.StatusText.Should().Contain("at least one copy");
    }

    private static Workbook PrintableWorkbook()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintArea = GridRange.Parse("A1:C4", sheet.Id);
        return workbook;
    }

    private sealed class RecordingPrintService(List<string> events) : IPlatformPrintService
    {
        public bool IsSupported => true;

        public string? SubmittedPath { get; private set; }

        public Task<PrinterDiscoveryResult> DiscoverAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PrinterDiscoveryResult(
                PrinterDiscoveryStatus.Available,
                [new PrinterInfo("office", IsDefault: true)],
                "office"));

        public Task<PrintSubmissionResult> SubmitAsync(
            string pdfPath,
            PrintSelection selection,
            CancellationToken cancellationToken = default)
        {
            events.Add("submit");
            SubmittedPath = pdfPath;
            File.Exists(pdfPath).Should().BeTrue();
            File.ReadAllBytes(pdfPath).Should().Equal(1, 2, 3);
            selection.PrinterName.Should().Be("office");
            selection.Copies.Should().Be(2);
            selection.Collate.Should().BeFalse();
            selection.EffectivePageRange.FirstPage.Should().Be(1);
            selection.EffectivePageRange.LastPage.Should().Be(1);
            selection.JobTitle.Should().Be("Budget");
            return Task.FromResult(new PrintSubmissionResult(
                PrintSubmissionStatus.Submitted,
                "office"));
        }
    }

    private sealed class UnexpectedPrintService : IPlatformPrintService
    {
        public bool IsSupported => true;

        public Task<PrinterDiscoveryResult> DiscoverAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fallback route must not discover printers.");

        public Task<PrintSubmissionResult> SubmitAsync(
            string pdfPath,
            PrintSelection selection,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fallback route must not submit.");
    }
}
