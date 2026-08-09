using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookImportWorkflowTests : IDisposable
{
    private readonly TestTemporaryDirectory _tempDirectory = new(nameof(WorkbookImportWorkflowTests) + "-");

    public void Dispose() => _tempDirectory.Dispose();

    [Fact]
    public async Task ImportPathAsync_LoadsFirstSheetAndDelegatesCommandApplication()
    {
        var imported = new Workbook("Imported");
        imported.AddSheet("Data");
        var adapter = new TestFileAdapter(load: _ => imported, extension: ".csv");
        var path = Path.Combine(_tempDirectory.Path, "data.csv");
        await File.WriteAllTextAsync(path, "a,b");
        var targetSheetId = SheetId.New();
        var destination = new CellAddress(targetSheetId, 2, 3);
        ImportSheetCommand? executed = null;

        var result = await WorkbookImportWorkflow.ImportPathAsync(
            path,
            ".csv",
            adapter,
            targetSheetId,
            destination,
            command =>
            {
                executed = command;
                return new CommandOutcome(true, AffectedCells: [destination]);
            });

        result.Succeeded.Should().BeTrue();
        result.WorksheetCount.Should().Be(1);
        executed.Should().NotBeNull();
        result.CommandOutcome!.AffectedCells.Should().Contain(destination);
    }

    [Fact]
    public void ApplyImportedWorkbook_EmptySourceReturnsStableReasonWithoutExecutingCommand()
    {
        var imported = new Workbook("Empty");

        var result = WorkbookImportWorkflow.ApplyImportedWorkbook(
            imported,
            SheetId.New(),
            new CellAddress(SheetId.New(), 1, 1),
            _ => throw new InvalidOperationException("Empty import must not execute."));

        result.Outcome.Should().Be(WorkbookImportExecutionOutcome.EmptyWorkbook);
        result.Reason.Should().Be("empty_workbook");
    }

    [Fact]
    public async Task ImportPathAsync_XsltFailureUsesReusableDiagnostic()
    {
        var adapter = new TestFileAdapter(
            load: _ => throw new InvalidDataException("The XSLT transform output exceeded the safety limit."),
            extension: ".xml");
        var path = Path.Combine(_tempDirectory.Path, "data.xml");
        await File.WriteAllTextAsync(path, "<xml />");

        var result = await WorkbookImportWorkflow.ImportPathAsync(
            path,
            ".xml",
            adapter,
            SheetId.New(),
            new CellAddress(SheetId.New(), 1, 1),
            _ => new CommandOutcome(true));

        result.Outcome.Should().Be(WorkbookImportExecutionOutcome.Failed);
        result.Reason.Should().Be("xslt_transform_failed");
        result.UserMessage.Should().StartWith("Failed to import XML data after applying the XSLT transform:");
    }
}
