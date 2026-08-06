using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookExportWorkflowTests
{
    [Fact]
    public async Task ExecuteAsync_NormalizesPdfOptionsBeforeRendererRuns()
    {
        ExportRequest? executed = null;
        var options = ExportOptions.ExcelLikeDefault with
        {
            PdfLanguage = "en_us"
        };
        var request = ExportPlanner.PlanExport("report", ExportFormat.Pdf, options);

        var result = await WorkbookExportWorkflow.ExecuteAsync(
            request,
            (effective, _) =>
            {
                executed = effective;
                return Task.CompletedTask;
            });

        result.Succeeded.Should().BeTrue();
        executed!.Path.Should().EndWith(".pdf");
        executed.Options.PdfLanguage.Should().Be("en-US");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidPublishOptionsNeverCallsRenderer()
    {
        var options = ExportOptions.ExcelLikeDefault with { PdfConformance = PdfConformance.PdfA1b };
        var request = ExportPlanner.PlanExport("report.pdf", ExportFormat.Pdf, options);

        var result = await WorkbookExportWorkflow.ExecuteAsync(
            request,
            (_, _) => throw new InvalidOperationException("Invalid export must not execute."));

        result.Outcome.Should().Be(WorkbookExportExecutionOutcome.ValidationFailed);
        result.Message.Should().Contain("PDF/A");
    }

    [Fact]
    public async Task ExecuteAsync_CapturesRendererFailure()
    {
        var request = ExportPlanner.PlanExport(
            "report.xps",
            ExportFormat.Xps,
            ExportOptions.ExcelLikeDefault);

        var result = await WorkbookExportWorkflow.ExecuteAsync(
            request,
            (_, _) => throw new IOException("disk full"));

        result.Outcome.Should().Be(WorkbookExportExecutionOutcome.Failed);
        result.Message.Should().Be("Export failed: disk full");
        result.Exception.Should().BeOfType<IOException>();
    }
}
