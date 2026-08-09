using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookFileWorkflowTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        nameof(WorkbookFileWorkflowTests),
        Guid.NewGuid().ToString("N"));

    public WorkbookFileWorkflowTests() => Directory.CreateDirectory(_tempDirectory);

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task OpenAsync_LoadsAppliesThenRegistersRecentFile()
    {
        var events = new List<string>();
        var workbook = WorkbookWithSheet();
        var adapter = new TestFileAdapter(load: _ => workbook);
        var path = Path.Combine(_tempDirectory, "Opened.fxjson");
        await File.WriteAllTextAsync(path, "payload");
        var store = RecentFilesStore.Load(Path.Combine(_tempDirectory, "recent.json"));
        var workflow = new WorkbookFileWorkflow(
            [adapter],
            registerRecentFile: request =>
            {
                events.Add("recent");
                return RecentFileRegistrationService.RegisterIfNeeded(store, request);
            });
        workflow.TryResolveOpenTarget(path, out var target, out var message).Should().BeTrue(message);

        var result = await workflow.OpenAsync(new WorkbookOpenWorkflowRequest(
            target!,
            (context, _) =>
            {
                events.Add("apply");
                context.CompletionPlan.Workbook.Should().BeSameAs(workbook);
                return Task.CompletedTask;
            }));

        result.Succeeded.Should().BeTrue();
        result.Context!.CompletionPlan.CurrentFilePath.Should().Be(path);
        result.RecentFileRegistration!.Registered.Should().BeTrue();
        events.Should().Equal("apply", "recent");
        store.Snapshot().Should().ContainSingle().Which.Path.Should().Be(path);
    }

    [Fact]
    public async Task OpenAsync_CancellationDoesNotApplyOrRegister()
    {
        var adapter = new TestFileAdapter(load: _ => WorkbookWithSheet());
        var path = Path.Combine(_tempDirectory, "Canceled.fxjson");
        await File.WriteAllTextAsync(path, "payload");
        var registrations = 0;
        var workflow = new WorkbookFileWorkflow(
            [adapter],
            registerRecentFile: request =>
            {
                registrations++;
                return new RecentFileRegistrationResult(RecentFileRegistration.Register, true);
            });
        workflow.TryResolveOpenTarget(path, out var target, out _).Should().BeTrue();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await workflow.OpenAsync(new WorkbookOpenWorkflowRequest(
            target!,
            (_, _) => throw new InvalidOperationException("Canceled open must not apply."),
            CancellationToken: cancellation.Token));

        result.Outcome.Should().Be(WorkbookFileOperationOutcome.Canceled);
        result.Message.Should().Be(WorkbookFileWorkflowMessages.OpenCanceled);
        registrations.Should().Be(0);
    }

    [Fact]
    public async Task SaveTargetAsync_AppliesCompletionThenRegistersRecentFile()
    {
        var events = new List<string>();
        var workbook = WorkbookWithSheet();
        var adapter = new TestFileAdapter(save: (_, _) => { });
        var path = Path.Combine(_tempDirectory, "Saved.fxjson");
        var generation = 4;
        var store = RecentFilesStore.Load(Path.Combine(_tempDirectory, "recent-save.json"));
        var workflow = new WorkbookFileWorkflow(
            [adapter],
            registerRecentFile: request =>
            {
                events.Add("recent");
                return RecentFileRegistrationService.RegisterIfNeeded(store, request);
            });

        var result = await workflow.SaveTargetAsync(new WorkbookSaveWorkflowRequest(
            IsDirty: true,
            CurrentFilePath: null,
            new FileSaveTarget(path, adapter),
            ExpectedLastWriteTimeUtc: null,
            GetCurrentWorkbook: () => workbook,
            GetDirtyGeneration: () => generation,
            ConfirmExternallyModifiedOverwrite: _ => true,
            ProjectViewStateForSave: () => events.Add("project"),
            SaveAsync: async invocation =>
            {
                events.Add("write");
                await File.WriteAllTextAsync(invocation.Target.Path, "saved", invocation.CancellationToken);
                return [];
            },
            ApplyCompletion: plan =>
            {
                events.Add("apply");
                plan.MarkSaved.Should().BeTrue();
            }));

        result.Succeeded.Should().BeTrue();
        result.ExecutionResult!.CompletionPlan!.FileContext!.Path.Should().Be(path);
        result.RequireExecutionResult().Should().BeSameAs(result.ExecutionResult);
        result.RequireCompletionPlan().Should().BeSameAs(result.ExecutionResult.CompletionPlan);
        events.Should().Equal("project", "write", "apply", "recent");
        store.Snapshot().Should().ContainSingle().Which.Path.Should().Be(path);
    }

    [Fact]
    public async Task SaveTargetAsync_ExternalWriteConflictReturnsTypedOutcomeWithoutApplying()
    {
        var workbook = WorkbookWithSheet();
        var adapter = new TestFileAdapter(save: (_, _) => { });
        var path = Path.Combine(_tempDirectory, "Conflict.fxjson");
        var applied = false;
        var workflow = new WorkbookFileWorkflow([adapter]);

        var result = await workflow.SaveTargetAsync(new WorkbookSaveWorkflowRequest(
            IsDirty: true,
            CurrentFilePath: path,
            new FileSaveTarget(path, adapter),
            ExpectedLastWriteTimeUtc: null,
            GetCurrentWorkbook: () => workbook,
            GetDirtyGeneration: () => 1,
            ConfirmExternallyModifiedOverwrite: _ => true,
            ProjectViewStateForSave: () => { },
            SaveAsync: _ => throw new WorkbookExternallyModifiedException(path),
            ApplyCompletion: _ => applied = true));

        result.Outcome.Should().Be(WorkbookFileOperationOutcome.ExternalWriteConflict);
        applied.Should().BeFalse();
    }

    [Fact]
    public async Task SaveTargetAsync_TargetPolicyRejectsBeforeExecutionStarts()
    {
        var adapter = new TestFileAdapter();
        var target = new FileSaveTarget(Path.Combine(_tempDirectory, "Blocked.xlsx"), adapter);
        var executionStarted = false;
        var workflow = new WorkbookFileWorkflow(
            [adapter],
            validateSaveTarget: _ => "Blocked by policy.");

        var result = await workflow.SaveTargetAsync(new WorkbookSaveWorkflowRequest(
            IsDirty: true,
            CurrentFilePath: null,
            target,
            ExpectedLastWriteTimeUtc: null,
            GetCurrentWorkbook: WorkbookWithSheet,
            GetDirtyGeneration: () => 1,
            ConfirmExternallyModifiedOverwrite: _ => true,
            ProjectViewStateForSave: () => throw new InvalidOperationException("Policy rejection must not project."),
            SaveAsync: _ => throw new InvalidOperationException("Policy rejection must not save."),
            ApplyCompletion: _ => throw new InvalidOperationException("Policy rejection must not apply."),
            ExecutionStarting: () => executionStarted = true));

        result.Outcome.Should().Be(WorkbookFileOperationOutcome.Rejected);
        result.Message.Should().Be("Blocked by policy.");
        executionStarted.Should().BeFalse();
    }

    [Fact]
    public async Task SaveTargetAsync_ExecutionHooksBracketWriteAndCompletion()
    {
        var events = new List<string>();
        var adapter = new TestFileAdapter();
        var target = new FileSaveTarget(Path.Combine(_tempDirectory, "Hooks.fxjson"), adapter);
        var workflow = new WorkbookFileWorkflow([adapter]);

        var result = await workflow.SaveTargetAsync(new WorkbookSaveWorkflowRequest(
            IsDirty: true,
            CurrentFilePath: null,
            target,
            ExpectedLastWriteTimeUtc: null,
            GetCurrentWorkbook: WorkbookWithSheet,
            GetDirtyGeneration: () => 1,
            ConfirmExternallyModifiedOverwrite: _ => true,
            ProjectViewStateForSave: () => events.Add("project"),
            SaveAsync: _ =>
            {
                events.Add("write");
                return Task.FromResult<IReadOnlyList<string>>([]);
            },
            ApplyCompletion: _ => events.Add("apply"),
            ExecutionStarting: () => events.Add("start"),
            ExecutionCompleted: () => events.Add("complete")));

        result.Succeeded.Should().BeTrue();
        events.Should().Equal("start", "project", "write", "apply", "complete");
    }

    [Fact]
    public void SaveTargetPolicy_BlocksOnlyUnsupportedXlsx()
    {
        var workbook = WorkbookWithSheet();
        var report = new XlsxFeatureReport(
        [
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.DataModel, "xl/model/item.data")
        ]);
        var adapter = new TestFileAdapter();

        WorkbookSaveTargetPolicy.BlockUnsupportedXlsxFeatures(
                new FileSaveTarget("Book.xlsx", adapter),
                report)
            .Should().Be(WorkbookFileWorkflowMessages.UnsupportedXlsxSave);
        WorkbookSaveTargetPolicy.BlockUnsupportedXlsxFeatures(
                new FileSaveTarget("Book.csv", adapter),
                report)
            .Should().BeNull();
    }

    private static Workbook WorkbookWithSheet()
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        return workbook;
    }
}
