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
}
