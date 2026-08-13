using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class AsyncFileLifecycleCoordinatorTests
{
    [Fact]
    public async Task ConfirmBeforeDestructiveAction_CleanDocument_ProceedsWithoutPromptOrSave()
    {
        var prompted = false;
        var saved = false;

        var result = await AsyncFileLifecycleCoordinator.ConfirmBeforeDestructiveActionAsync(
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

        result.Should().Be(DirtyGateResult.Proceed);
        prompted.Should().BeFalse();
        saved.Should().BeFalse();
    }

    [Theory]
    [InlineData(SaveChangesPrompt.Cancel, DirtyGateResult.Cancel, false)]
    [InlineData(SaveChangesPrompt.DontSave, DirtyGateResult.ProceedDiscardingChanges, false)]
    [InlineData(SaveChangesPrompt.Save, DirtyGateResult.Proceed, true)]
    public async Task ConfirmBeforeDestructiveAction_DirtyDocument_ResolvesPrompt(
        SaveChangesPrompt prompt,
        DirtyGateResult expected,
        bool expectedSave)
    {
        var saveCalls = 0;

        var result = await AsyncFileLifecycleCoordinator.ConfirmBeforeDestructiveActionAsync(
            isDirty: true,
            promptSaveChangesAsync: () => Task.FromResult(prompt),
            saveCurrentAsync: () =>
            {
                saveCalls++;
                return Task.FromResult(true);
            });

        result.Should().Be(expected);
        saveCalls.Should().Be(expectedSave ? 1 : 0);
    }

    [Fact]
    public async Task ConfirmBeforeDestructiveAction_SaveFailure_Cancels()
    {
        var result = await AsyncFileLifecycleCoordinator.ConfirmBeforeDestructiveActionAsync(
            isDirty: true,
            promptSaveChangesAsync: () => Task.FromResult(SaveChangesPrompt.Save),
            saveCurrentAsync: () => Task.FromResult(false));

        result.Should().Be(DirtyGateResult.Cancel);
    }

    [Fact]
    public async Task SaveResolved_NoCurrentPath_UsesSaveAsWithoutResolvingTarget()
    {
        var resolved = false;
        var savedAs = false;

        var saved = await AsyncFileLifecycleCoordinator.SaveResolvedAsync<TestTarget>(
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
            },
            resolvedTargetPolicy: _ => throw new InvalidOperationException("Policy should not be used."));

        saved.Should().BeTrue();
        resolved.Should().BeFalse();
        savedAs.Should().BeTrue();
    }

    [Fact]
    public async Task SaveResolved_CurrentPathAndResolvedTarget_SavesTarget()
    {
        var target = new TestTarget(@"C:\Work\Book.fx");
        TestTarget? savedTarget = null;

        var saved = await AsyncFileLifecycleCoordinator.SaveResolvedAsync<TestTarget>(
            isDirty: true,
            currentFilePath: target.Path,
            resolveCurrentTarget: () => target,
            saveTargetAsync: resolvedTarget =>
            {
                savedTarget = resolvedTarget;
                return Task.FromResult(true);
            },
            saveAsAsync: () => throw new InvalidOperationException("Save As should not be used."),
            resolvedTargetPolicy: _ => ResolvedSaveTargetDecision.Write);

        saved.Should().BeTrue();
        savedTarget.Should().BeSameAs(target);
    }

    [Fact]
    public async Task SaveResolved_CurrentPathButNoResolvedTarget_FallsBackToSaveAs()
    {
        var savedAs = false;

        var saved = await AsyncFileLifecycleCoordinator.SaveResolvedAsync<TestTarget>(
            isDirty: true,
            currentFilePath: @"C:\Work\Book.unknown",
            resolveCurrentTarget: () => null,
            saveTargetAsync: _ => throw new InvalidOperationException("Save target should not be used."),
            saveAsAsync: () =>
            {
                savedAs = true;
                return Task.FromResult(false);
            },
            resolvedTargetPolicy: _ => throw new InvalidOperationException("Policy should not be used."));

        saved.Should().BeFalse();
        savedAs.Should().BeTrue();
    }

    [Fact]
    public async Task SaveResolved_CleanDocumentWithCurrentPath_StillDelegatesToResolvedTarget()
    {
        var target = new TestTarget(@"C:\Work\Book.fx");
        var savedTarget = false;

        var saved = await AsyncFileLifecycleCoordinator.SaveResolvedAsync<TestTarget>(
            isDirty: false,
            currentFilePath: target.Path,
            resolveCurrentTarget: () => target,
            saveTargetAsync: resolvedTarget =>
            {
                savedTarget = ReferenceEquals(resolvedTarget, target);
                return Task.FromResult(true);
            },
            saveAsAsync: () => throw new InvalidOperationException("Save As should not be used."));

        saved.Should().BeTrue();
        savedTarget.Should().BeTrue();
    }

    [Fact]
    public async Task SaveResolved_ResolvedTargetPolicySkipsWrite()
    {
        var target = new TestTarget(@"C:\Work\Book.fx");
        TestTarget? policyTarget = null;

        var saved = await AsyncFileLifecycleCoordinator.SaveResolvedAsync<TestTarget>(
            isDirty: false,
            currentFilePath: target.Path,
            resolveCurrentTarget: () => target,
            saveTargetAsync: _ => throw new InvalidOperationException("Save target should not be used."),
            saveAsAsync: () => throw new InvalidOperationException("Save As should not be used."),
            resolvedTargetPolicy: resolvedTarget =>
            {
                policyTarget = resolvedTarget;
                return ResolvedSaveTargetDecision.Skip;
            });

        saved.Should().BeTrue();
        policyTarget.Should().BeSameAs(target);
    }

    [Fact]
    public async Task SaveResolved_ResolvedTargetPolicyErrorPropagatesWithoutWriting()
    {
        var target = new TestTarget(@"C:\Work\Book.fx");
        var expected = new InvalidOperationException("Policy failed.");

        Func<Task> act = async () => await AsyncFileLifecycleCoordinator.SaveResolvedAsync<TestTarget>(
            isDirty: true,
            currentFilePath: target.Path,
            resolveCurrentTarget: () => target,
            saveTargetAsync: _ => throw new InvalidOperationException("Save target should not be used."),
            saveAsAsync: () => throw new InvalidOperationException("Save As should not be used."),
            resolvedTargetPolicy: _ => throw expected);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task SaveResolved_CanceledTargetWriteRemainsCanceled()
    {
        var target = new TestTarget(@"C:\Work\Book.fx");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var saveTask = AsyncFileLifecycleCoordinator.SaveResolvedAsync<TestTarget>(
            isDirty: true,
            currentFilePath: target.Path,
            resolveCurrentTarget: () => target,
            saveTargetAsync: _ => Task.FromCanceled<bool>(cancellation.Token),
            saveAsAsync: () => throw new InvalidOperationException("Save As should not be used."));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => saveTask);
        saveTask.IsCanceled.Should().BeTrue();
    }

    private sealed record TestTarget(string Path);
}
