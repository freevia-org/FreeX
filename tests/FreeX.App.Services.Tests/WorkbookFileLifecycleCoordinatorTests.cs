using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookFileLifecycleCoordinatorTests
{
    [Fact]
    public async Task ConfirmBeforeDestructiveAction_CleanWorkbook_ContinuesWithoutPromptOrSave()
    {
        var prompted = false;
        var saved = false;

        var confirmation = await WorkbookFileLifecycleCoordinator.ConfirmBeforeDestructiveActionAsync(
            isDirty: false,
            promptSaveChangesAsync: () =>
            {
                prompted = true;
                return Task.FromResult(SaveChangesPrompt.Save);
            },
            saveCurrentAsync: () =>
            {
                saved = true;
                return Task.FromResult(true);
            });

        confirmation.Should().Be(SaveChangesConfirmation.Continue);
        prompted.Should().BeFalse();
        saved.Should().BeFalse();
    }

    [Theory]
    [InlineData(SaveChangesPrompt.Cancel, SaveChangesConfirmation.Cancel, false)]
    [InlineData(SaveChangesPrompt.DontSave, SaveChangesConfirmation.DiscardWithoutSaving, false)]
    [InlineData(SaveChangesPrompt.Save, SaveChangesConfirmation.Continue, true)]
    public async Task ConfirmBeforeDestructiveAction_DirtyWorkbook_ResolvesPrompt(
        SaveChangesPrompt prompt,
        SaveChangesConfirmation expected,
        bool expectedSave)
    {
        var saveCalls = 0;

        var confirmation = await WorkbookFileLifecycleCoordinator.ConfirmBeforeDestructiveActionAsync(
            isDirty: true,
            promptSaveChangesAsync: () => Task.FromResult(prompt),
            saveCurrentAsync: () =>
            {
                saveCalls++;
                return Task.FromResult(true);
            });

        confirmation.Should().Be(expected);
        saveCalls.Should().Be(expectedSave ? 1 : 0);
    }

    [Fact]
    public async Task ConfirmBeforeDestructiveAction_SaveFailure_Cancels()
    {
        var confirmation = await WorkbookFileLifecycleCoordinator.ConfirmBeforeDestructiveActionAsync(
            isDirty: true,
            promptSaveChangesAsync: () => Task.FromResult(SaveChangesPrompt.Save),
            saveCurrentAsync: () => Task.FromResult(false));

        confirmation.Should().Be(SaveChangesConfirmation.Cancel);
    }

    [Fact]
    public async Task CanProceedAfterDirtyGate_Cancel_ReturnsFalseWithoutActionCeremony()
    {
        var canProceed = await WorkbookFileLifecycleCoordinator.CanProceedAfterDirtyGateAsync(
            isDirty: true,
            promptSaveChangesAsync: () => Task.FromResult(SaveChangesPrompt.Cancel),
            saveCurrentAsync: () => throw new InvalidOperationException("Cancel should not save."));

        canProceed.Should().BeFalse();
    }

    [Fact]
    public async Task CanProceedAfterDirtyGateWithCleanSave_SaveThatLeavesWorkbookDirty_ReturnsFalse()
    {
        var canProceed = await WorkbookFileLifecycleCoordinator.CanProceedAfterDirtyGateWithCleanSaveAsync(
            isDirty: true,
            promptSaveChangesAsync: () => Task.FromResult(SaveChangesPrompt.Save),
            saveCurrentAsync: () => Task.FromResult(true),
            isDirtyNow: () => true);

        canProceed.Should().BeFalse();
    }

    [Fact]
    public async Task CanProceedAfterDirtyGateWithCleanSave_DiscardingChanges_ProceedsWithoutCleanRecheck()
    {
        var recheckedDirty = false;

        var canProceed = await WorkbookFileLifecycleCoordinator.CanProceedAfterDirtyGateWithCleanSaveAsync(
            isDirty: true,
            promptSaveChangesAsync: () => Task.FromResult(SaveChangesPrompt.DontSave),
            saveCurrentAsync: () => throw new InvalidOperationException("Discard should not save."),
            isDirtyNow: () =>
            {
                recheckedDirty = true;
                return true;
            });

        canProceed.Should().BeTrue();
        recheckedDirty.Should().BeFalse();
    }

    [Fact]
    public async Task RunAfterDirtyGate_DirtySaveAnswer_RunsActionAfterSave()
    {
        var saved = false;
        var actionRan = false;

        var proceeded = await WorkbookFileLifecycleCoordinator.RunAfterDirtyGateAsync(
            isDirty: true,
            promptSaveChangesAsync: () => Task.FromResult(SaveChangesPrompt.Save),
            saveCurrentAsync: () =>
            {
                saved = true;
                return Task.FromResult(true);
            },
            runActionAsync: () =>
            {
                actionRan = true;
                return Task.CompletedTask;
            });

        proceeded.Should().BeTrue();
        saved.Should().BeTrue();
        actionRan.Should().BeTrue();
    }

    [Fact]
    public async Task RunAfterDirtyGate_SaveFailure_DoesNotRunAction()
    {
        var actionRan = false;

        var proceeded = await WorkbookFileLifecycleCoordinator.RunAfterDirtyGateAsync(
            isDirty: true,
            promptSaveChangesAsync: () => Task.FromResult(SaveChangesPrompt.Save),
            saveCurrentAsync: () => Task.FromResult(false),
            runActionAsync: () =>
            {
                actionRan = true;
                return Task.CompletedTask;
            });

        proceeded.Should().BeFalse();
        actionRan.Should().BeFalse();
    }

    [Fact]
    public async Task SaveResolved_NoCurrentPath_UsesSaveAsWithoutResolvingTarget()
    {
        var resolved = false;
        var savedAs = false;

        var saved = await WorkbookFileLifecycleCoordinator.SaveResolvedAsync(
            isDirty: true,
            currentFilePath: null,
            resolveCurrentTarget: () =>
            {
                resolved = true;
                return null;
            },
            saveTargetAsync: _ => throw new InvalidOperationException("Save target should not be used."),
            saveAsAsync: () =>
            {
                savedAs = true;
                return Task.FromResult(true);
            });

        saved.Should().BeTrue();
        resolved.Should().BeFalse();
        savedAs.Should().BeTrue();
    }

    [Fact]
    public async Task SaveResolved_CurrentPathAndResolvedTarget_SavesTarget()
    {
        var target = new FileSaveTarget(@"C:\Work\Book.fxl", new TestFileAdapter(extension: ".fxl"));
        FileSaveTarget? savedTarget = null;

        var saved = await WorkbookFileLifecycleCoordinator.SaveResolvedAsync(
            isDirty: true,
            currentFilePath: target.Path,
            resolveCurrentTarget: () => target,
            saveTargetAsync: resolvedTarget =>
            {
                savedTarget = resolvedTarget;
                return Task.FromResult(true);
            },
            saveAsAsync: () => throw new InvalidOperationException("Save As should not be used."));

        saved.Should().BeTrue();
        savedTarget.Should().BeSameAs(target);
    }

    [Fact]
    public async Task SaveResolved_CleanCurrentPathSkipsTargetWrite()
    {
        var path = Path.Combine(Path.GetTempPath(), "Book.fxl");
        var target = new FileSaveTarget(path, new TestFileAdapter(extension: ".fxl"));
        var saveTargetCalls = 0;

        var saved = await WorkbookFileLifecycleCoordinator.SaveResolvedAsync(
            isDirty: false,
            currentFilePath: path,
            resolveCurrentTarget: () => target,
            saveTargetAsync: _ =>
            {
                saveTargetCalls++;
                return Task.FromResult(true);
            },
            saveAsAsync: () => throw new InvalidOperationException("Save As should not be used."));

        saved.Should().BeTrue();
        saveTargetCalls.Should().Be(0);
    }

    [Fact]
    public async Task SaveResolved_CleanDifferentTargetWrites()
    {
        var currentPath = Path.Combine(Path.GetTempPath(), "Current.fxl");
        var target = new FileSaveTarget(
            Path.Combine(Path.GetTempPath(), "Different.fxl"),
            new TestFileAdapter(extension: ".fxl"));
        FileSaveTarget? savedTarget = null;

        var saved = await WorkbookFileLifecycleCoordinator.SaveResolvedAsync(
            isDirty: false,
            currentFilePath: currentPath,
            resolveCurrentTarget: () => target,
            saveTargetAsync: resolvedTarget =>
            {
                savedTarget = resolvedTarget;
                return Task.FromResult(true);
            },
            saveAsAsync: () => throw new InvalidOperationException("Save As should not be used."));

        saved.Should().BeTrue();
        savedTarget.Should().BeSameAs(target);
    }

    [Fact]
    public async Task SaveResolved_CurrentPathButNoResolvedTarget_FallsBackToSaveAs()
    {
        var savedAs = false;

        var saved = await WorkbookFileLifecycleCoordinator.SaveResolvedAsync(
            isDirty: true,
            currentFilePath: @"C:\Work\Book.unknown",
            resolveCurrentTarget: () => null,
            saveTargetAsync: _ => throw new InvalidOperationException("Save target should not be used."),
            saveAsAsync: () =>
            {
                savedAs = true;
                return Task.FromResult(false);
            });

        saved.Should().BeFalse();
        savedAs.Should().BeTrue();
    }

    [Fact]
    public async Task SaveResolved_AdapterOverload_ResolvesExistingPathByExtension()
    {
        var adapter = new TestFileAdapter(extension: ".fxl");
        FileSaveTarget? savedTarget = null;

        var saved = await WorkbookFileLifecycleCoordinator.SaveResolvedAsync(
            isDirty: true,
            currentFilePath: @"C:\Work\Book.fxl",
            adapters: [adapter],
            saveTargetAsync: target =>
            {
                savedTarget = target;
                return Task.FromResult(true);
            },
            saveAsAsync: () => throw new InvalidOperationException("Save As should not be used."));

        saved.Should().BeTrue();
        savedTarget.Should().NotBeNull();
        savedTarget!.Path.Should().Be(@"C:\Work\Book.fxl");
        savedTarget.Adapter.Should().BeSameAs(adapter);
    }

    [Fact]
    public async Task SaveResolved_TargetWriteErrorPropagates()
    {
        var target = new FileSaveTarget(@"C:\Work\Book.fxl", new TestFileAdapter(extension: ".fxl"));
        var expected = new InvalidOperationException("Write failed.");

        Func<Task> act = async () => await WorkbookFileLifecycleCoordinator.SaveResolvedAsync(
            isDirty: true,
            currentFilePath: target.Path,
            resolveCurrentTarget: () => target,
            saveTargetAsync: _ => Task.FromException<bool>(expected),
            saveAsAsync: () => throw new InvalidOperationException("Save As should not be used."));

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task SaveResolved_SaveAsCancellationRemainsCanceled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var saveTask = WorkbookFileLifecycleCoordinator.SaveResolvedAsync(
            isDirty: true,
            currentFilePath: null,
            resolveCurrentTarget: () => throw new InvalidOperationException("Target should not be resolved."),
            saveTargetAsync: _ => throw new InvalidOperationException("Save target should not be used."),
            saveAsAsync: () => Task.FromCanceled<bool>(cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => saveTask);
        saveTask.IsCanceled.Should().BeTrue();
    }

    [Fact]
    public void PlanSaveTargetWrite_CleanCurrentPathSkipsWrite()
    {
        var path = Path.Combine(Path.GetTempPath(), "Book.fxl");
        var target = new FileSaveTarget(path, new TestFileAdapter(extension: ".fxl"));

        WorkbookFileLifecycleCoordinator.PlanSaveTargetWrite(
                isDirty: false,
                currentFilePath: path,
                target)
            .Should()
            .Be(WorkbookSaveTargetIntent.SkipCleanCurrentPath);
    }

    [Fact]
    public void PlanSaveTargetWrite_DirtyCurrentPathWrites()
    {
        var path = Path.Combine(Path.GetTempPath(), "Book.fxl");
        var target = new FileSaveTarget(path, new TestFileAdapter(extension: ".fxl"));

        WorkbookFileLifecycleCoordinator.PlanSaveTargetWrite(
                isDirty: true,
                currentFilePath: path,
                target)
            .Should()
            .Be(WorkbookSaveTargetIntent.Write);
    }

    [Fact]
    public void PlanSavePathNormalization_AddsDefaultExtensionAndRequestsOverwriteConfirmation()
    {
        var requested = Path.Combine(Path.GetTempPath(), "Budget");
        var normalized = requested + ".fxl";

        var plan = WorkbookFileLifecycleCoordinator.PlanSavePathNormalization(
            requested,
            ".fxl",
            path => string.Equals(path, normalized, StringComparison.Ordinal));

        plan.Path.Should().Be(normalized);
        plan.ShouldConfirmOverwrite.Should().BeTrue();
    }

    [Fact]
    public void PlanSavePathNormalization_DoesNotConfirmWhenPathAlreadyMatches()
    {
        var requested = Path.Combine(Path.GetTempPath(), "Budget.fxl");
        var existsChecked = false;

        var plan = WorkbookFileLifecycleCoordinator.PlanSavePathNormalization(
            requested,
            ".fxl",
            _ =>
            {
                existsChecked = true;
                return true;
            });

        plan.Path.Should().Be(requested);
        plan.ShouldConfirmOverwrite.Should().BeFalse();
        existsChecked.Should().BeFalse();
    }
}
