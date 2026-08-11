using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookPrintWorkflowTests
{
    [Fact]
    public async Task ExecutePortableAsync_PrinterRouteRendersThenSubmitsValidatedJob()
    {
        var events = new List<string>();
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
            (submission, _) =>
            {
                events.Add("submit");
                submission.PrinterId.Should().Be("office");
                submission.Copies.Should().Be(2);
                submission.Collate.Should().BeFalse();
                submission.JobTitle.Should().Be("Budget");
                return Task.FromResult(PrintSubmissionResult.Success("queued"));
            },
            (_, _) => throw new InvalidOperationException("Printer route must not save fallback."));

        result.Succeeded.Should().BeTrue();
        result.StatusText.Should().Be("queued");
        result.RenderedDocument!.ImageDiagnostics.Should().Equal("picture warning");
        events.Should().Equal("render", "submit");
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
            (_, _) => throw new InvalidOperationException("Fallback route must not submit."),
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
            (_, _) => throw new InvalidOperationException("Invalid print must not submit."),
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
}
