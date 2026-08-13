using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSaveExecutionCoordinatorTests
{
    [Fact]
    public void Begin_ExternalWriteDeclined_DoesNotCreateExecution()
    {
        var workbook = CreateWorkbook();
        var originalWriteTime = new DateTime(2026, 8, 6, 1, 0, 0, DateTimeKind.Utc);
        var externalWriteTime = originalWriteTime.AddMinutes(1);
        var promptCount = 0;

        var start = WorkbookSaveExecutionCoordinator.Begin(new WorkbookSaveExecutionStartRequest(
            CurrentFilePath: "Book.fxjson",
            new FileSaveTarget("Book.fxjson", new TestFileAdapter()),
            originalWriteTime,
            GetCurrentWorkbook: () => workbook,
            GetDirtyGeneration: () => 3,
            ConfirmExternallyModifiedOverwrite: _ =>
            {
                promptCount++;
                return false;
            },
            FileExists: _ => true,
            GetLastWriteTimeUtc: _ => externalWriteTime));

        start.Outcome.Should().Be(WorkbookSaveExecutionStartOutcome.ExternalWriteDeclined);
        start.Execution.Should().BeNull();
        promptCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_AcceptedExternalWrite_UsesAcceptedVersionAsServiceBaseline()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "Book.fxjson");
        await File.WriteAllTextAsync(path, "external content");
        var openedWriteTime = File.GetLastWriteTimeUtc(path).AddMinutes(-5);
        var acceptedWriteTime = File.GetLastWriteTimeUtc(path);
        var workbook = CreateWorkbook();
        var adapter = new TestFileAdapter(save: (_, stream) =>
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write("saved content");
        });

        var start = WorkbookSaveExecutionCoordinator.Begin(new WorkbookSaveExecutionStartRequest(
            path,
            new FileSaveTarget(path, adapter),
            openedWriteTime,
            GetCurrentWorkbook: () => workbook,
            GetDirtyGeneration: () => 1,
            ConfirmExternallyModifiedOverwrite: _ => true));

        var result = await start.Execution!.ExecuteAsync(new WorkbookSaveExecutionRequest(
            CancellationToken.None,
            ProjectViewStateForSave: () => { },
            SaveAsync: invocation => new WorkbookSaveService().SaveAsync(
                invocation.Target.Path,
                invocation.Target.Adapter,
                invocation.Workbook,
                cancellationToken: invocation.CancellationToken,
                expectedLastWriteTimeUtc: invocation.ExpectedLastWriteTimeUtc)));

        result.Outcome.Should().Be(WorkbookSaveExecutionOutcome.Succeeded);
        result.CompletionPlan!.MarkSaved.Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("saved content");
        acceptedWriteTime.Should().NotBe(openedWriteTime);
    }

    [Fact]
    public async Task ExecuteAsync_SecondExternalWriteAfterAcceptedPrompt_ReturnsConflictAndPreservesDisk()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "Book.fxjson");
        await File.WriteAllTextAsync(path, "first external content");
        var openedWriteTime = File.GetLastWriteTimeUtc(path).AddMinutes(-5);
        var workbook = CreateWorkbook();
        var adapterInvoked = false;
        var adapter = new TestFileAdapter(save: (_, _) => adapterInvoked = true);

        var start = WorkbookSaveExecutionCoordinator.Begin(new WorkbookSaveExecutionStartRequest(
            path,
            new FileSaveTarget(path, adapter),
            openedWriteTime,
            GetCurrentWorkbook: () => workbook,
            GetDirtyGeneration: () => 1,
            ConfirmExternallyModifiedOverwrite: _ => true));

        await File.WriteAllTextAsync(path, "second external content");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));

        var result = await start.Execution!.ExecuteAsync(new WorkbookSaveExecutionRequest(
            CancellationToken.None,
            ProjectViewStateForSave: () => { },
            SaveAsync: invocation => new WorkbookSaveService().SaveAsync(
                invocation.Target.Path,
                invocation.Target.Adapter,
                invocation.Workbook,
                cancellationToken: invocation.CancellationToken,
                expectedLastWriteTimeUtc: invocation.ExpectedLastWriteTimeUtc)));

        result.Outcome.Should().Be(WorkbookSaveExecutionOutcome.ExternalWriteConflict);
        adapterInvoked.Should().BeFalse();
        (await File.ReadAllTextAsync(path)).Should().Be("second external content");
    }

    [Fact]
    public async Task ExecuteAsync_MidSaveEdit_PreservesDirtyCompletionAndPreparationLifetime()
    {
        var workbook = CreateWorkbook();
        var generation = 4;
        var events = new List<string>();
        var lifetime = new CallbackDisposable(() => events.Add("dispose"));
        var start = ReadyExecution(workbook, () => generation);

        var result = await start.ExecuteAsync(new WorkbookSaveExecutionRequest(
            CancellationToken.None,
            ProjectViewStateForSave: () => events.Add("project"),
            SaveAsync: _ =>
            {
                events.Add("save");
                generation++;
                return Task.FromResult<IReadOnlyList<string>>(["warning"]);
            },
            PrepareAsync: _ =>
            {
                events.Add("prepare");
                return Task.FromResult(new WorkbookSaveExecutionPreparation(lifetime: lifetime));
            }));

        result.Outcome.Should().Be(WorkbookSaveExecutionOutcome.Succeeded);
        result.Warnings.Should().Equal("warning");
        result.CompletionPlan.Should().BeEquivalentTo(new
        {
            MarkSaved = false,
            ApplyFileContext = true
        });
        events.Should().Equal("prepare", "project", "save", "dispose");
    }

    [Fact]
    public async Task ExecuteAsync_WorkbookReplacedDuringSave_SkipsAllCompletionMutation()
    {
        var original = CreateWorkbook();
        var current = original;
        var start = ReadyExecution(original, () => 2, () => current);

        var result = await start.ExecuteAsync(new WorkbookSaveExecutionRequest(
            CancellationToken.None,
            ProjectViewStateForSave: () => { },
            SaveAsync: _ =>
            {
                current = CreateWorkbook("Replacement");
                return Task.FromResult<IReadOnlyList<string>>([]);
            }));

        result.Outcome.Should().Be(WorkbookSaveExecutionOutcome.Succeeded);
        result.CompletionPlan!.MarkSaved.Should().BeFalse();
        result.CompletionPlan.ApplyFileContext.Should().BeFalse();
        result.CompletionPlan.FileContext.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_CancellationAndFailure_ReturnTypedOutcomes()
    {
        var workbook = CreateWorkbook();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var canceled = await ReadyExecution(workbook, () => 0).ExecuteAsync(new WorkbookSaveExecutionRequest(
            cancellation.Token,
            ProjectViewStateForSave: () => throw new InvalidOperationException("must not project"),
            SaveAsync: _ => throw new InvalidOperationException("must not save")));

        var failure = await ReadyExecution(workbook, () => 0).ExecuteAsync(new WorkbookSaveExecutionRequest(
            CancellationToken.None,
            ProjectViewStateForSave: () => { },
            SaveAsync: _ => throw new IOException("disk full")));

        canceled.Outcome.Should().Be(WorkbookSaveExecutionOutcome.Canceled);
        canceled.Exception.Should().BeOfType<OperationCanceledException>();
        failure.Outcome.Should().Be(WorkbookSaveExecutionOutcome.Failed);
        failure.Exception.Should().BeOfType<IOException>().Which.Message.Should().Be("disk full");
    }

    private static WorkbookSaveExecution ReadyExecution(
        Workbook workbook,
        Func<int> getGeneration,
        Func<Workbook>? getWorkbook = null)
    {
        getWorkbook ??= () => workbook;
        var start = WorkbookSaveExecutionCoordinator.Begin(new WorkbookSaveExecutionStartRequest(
            CurrentFilePath: null,
            new FileSaveTarget("Book.fxjson", new TestFileAdapter()),
            ExpectedLastWriteTimeUtc: null,
            getWorkbook,
            getGeneration,
            ConfirmExternallyModifiedOverwrite: _ => throw new InvalidOperationException("no prompt expected"),
            FileExists: _ => false));

        start.CanExecute.Should().BeTrue();
        return start.Execution!;
    }

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        return workbook;
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        public void Dispose() => callback();
    }
}
