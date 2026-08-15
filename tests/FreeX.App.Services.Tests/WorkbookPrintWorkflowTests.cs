using FluentAssertions;
using Free.Shared.AppServices.Printing;
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
