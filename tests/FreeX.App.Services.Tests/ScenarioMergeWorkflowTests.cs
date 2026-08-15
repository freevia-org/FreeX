using FluentAssertions;
using Free.Shared.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class ScenarioMergeWorkflowTests : IDisposable
{
    private readonly TestTemporaryDirectory _temp = new(nameof(ScenarioMergeWorkflowTests) + "-");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task RunAsync_CanceledPickerDoesNotLoadApplyOrReportFailure()
    {
        var adapter = new TestFileAdapter(load: _ => throw new InvalidOperationException("Must not load."));
        var events = new List<string>();
        var result = await new ScenarioMergeWorkflow([adapter]).RunAsync(
            TargetWorkbook(),
            Host(
                (_, _) => ValueTask.FromResult<string?>(null),
                _ => throw new InvalidOperationException("Must not apply."),
                events));

        result.Outcome.Should().Be(ScenarioMergeWorkflowOutcome.Canceled);
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_UnsupportedPathReportsPortableOpenFailure()
    {
        var events = new List<string>();
        var result = await new ScenarioMergeWorkflow([new TestFileAdapter()]).RunAsync(
            TargetWorkbook(),
            Host(
                (_, _) => ValueTask.FromResult<string?>(Path.Combine(_temp.Path, "Source.unsupported")),
                _ => throw new InvalidOperationException("Must not apply."),
                events));

        result.Outcome.Should().Be(ScenarioMergeWorkflowOutcome.OpenFailed);
        events.Should().Equal("open-failed");
    }

    [Fact]
    public async Task RunAsync_LoadsRemapsAppliesAndRefreshesInOrder()
    {
        var source = new Workbook("Source");
        var sourceBudget = source.AddSheet("Budget");
        source.Scenarios.Add(new WorkbookScenario(
            "Forecast",
            [new ScenarioCellValue(new CellAddress(sourceBudget.Id, 2, 3), new NumberValue(42))]));
        var target = TargetWorkbook();
        var path = Path.Combine(_temp.Path, "Source.fxjson");
        await File.WriteAllTextAsync(path, "payload");
        var events = new List<string>();
        IReadOnlyList<WorkbookScenario>? applied = null;
        var workflow = new ScenarioMergeWorkflow([new TestFileAdapter(load: _ => source)]);

        var result = await workflow.RunAsync(
            target,
            Host(
                (plan, _) =>
                {
                    plan.Filter.Should().Contain("Fake");
                    events.Add("pick");
                    return ValueTask.FromResult<string?>(path);
                },
                scenarios =>
                {
                    events.Add("apply");
                    applied = scenarios;
                    return true;
                },
                events));

        result.Succeeded.Should().BeTrue();
        events.Should().Equal("pick", "apply", "refresh");
        applied.Should().ContainSingle();
        applied![0].ChangingCells.Should().ContainSingle();
        applied[0].ChangingCells[0].Address.Sheet.Should().Be(target.Sheets[0].Id);
    }

    [Fact]
    public async Task RunAsync_RejectedMutationDoesNotReportOpenFailureOrRefresh()
    {
        var source = TargetWorkbook();
        var path = Path.Combine(_temp.Path, "Source.fxjson");
        await File.WriteAllTextAsync(path, "payload");
        var events = new List<string>();

        var result = await new ScenarioMergeWorkflow([new TestFileAdapter(load: _ => source)]).RunAsync(
            TargetWorkbook(),
            Host(
                (_, _) => ValueTask.FromResult<string?>(path),
                _ =>
                {
                    events.Add("apply-rejected");
                    return false;
                },
                events));

        result.Outcome.Should().Be(ScenarioMergeWorkflowOutcome.ApplyFailed);
        events.Should().Equal("apply-rejected");
    }

    private static ScenarioMergeWorkflowHost Host(
        Func<FileOpenDialogPlan, CancellationToken, ValueTask<string?>> pick,
        Func<IReadOnlyList<WorkbookScenario>, bool> apply,
        ICollection<string> events) =>
        new(
            pick,
            apply,
            () => events.Add("open-failed"),
            () => events.Add("refresh"));

    private static Workbook TargetWorkbook()
    {
        var workbook = new Workbook("Target");
        workbook.AddSheet("Budget");
        return workbook;
    }
}
