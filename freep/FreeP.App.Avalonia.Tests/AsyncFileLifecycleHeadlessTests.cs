using Avalonia.Controls;
using FreeP.App.Compositor;
using Avalonia.Headless;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;

namespace FreeP.App.Avalonia.Tests;

public sealed class AsyncFileLifecycleHeadlessTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    [Theory]
    [InlineData(SaveChangesPrompt.Save, true, true)]
    [InlineData(SaveChangesPrompt.DontSave, false, true)]
    [InlineData(SaveChangesPrompt.Cancel, false, false)]
    public async Task DirtyNew_PreservesSaveDiscardCancelSemantics(
        SaveChangesPrompt prompt,
        bool expectedSave,
        bool expectedLoad)
    {
        var saveCalls = 0;
        var loadCalls = 0;
        var result = false;
        var isDirty = false;

        await RunOnUiThread(async () =>
        {
            var workflow = CreateWorkflow(
                prompt,
                saveAsync: () =>
                {
                    saveCalls++;
                    return Task.FromResult(true);
                });
            workflow.MarkDirty();

            result = await workflow.NewAsync(
                "creating a new presentation",
                () =>
                {
                    loadCalls++;
                    return Task.CompletedTask;
                });
            isDirty = workflow.IsDirty;
        });

        result.Should().Be(expectedLoad);
        saveCalls.Should().Be(expectedSave ? 1 : 0);
        loadCalls.Should().Be(expectedLoad ? 1 : 0);
        isDirty.Should().Be(!expectedLoad);
    }

    [Fact]
    public async Task DirtyNew_SaveFailure_CancelsAndKeepsDocumentDirty()
    {
        var loaded = false;
        var result = true;
        var isDirty = false;

        await RunOnUiThread(async () =>
        {
            var workflow = CreateWorkflow(
                SaveChangesPrompt.Save,
                saveAsync: () => Task.FromResult(false));
            workflow.MarkDirty();

            result = await workflow.NewAsync(
                "creating a new presentation",
                () =>
                {
                    loaded = true;
                    return Task.CompletedTask;
                });
            isDirty = workflow.IsDirty;
        });

        result.Should().BeFalse();
        loaded.Should().BeFalse();
        isDirty.Should().BeTrue();
    }

    [Theory]
    [InlineData("creating a new presentation")]
    [InlineData("opening another presentation")]
    public async Task DirtyNewAndOpen_CancelBeforeDestructiveOrPickerAction(string action)
    {
        var destructiveCalls = 0;
        var result = true;

        await RunOnUiThread(async () =>
        {
            var workflow = CreateWorkflow(SaveChangesPrompt.Cancel);
            workflow.MarkDirty();

            result = action.StartsWith("creating", StringComparison.Ordinal)
                ? await workflow.NewAsync(
                    action,
                    () =>
                    {
                        destructiveCalls++;
                        return Task.CompletedTask;
                    })
                : await workflow.OpenAsync(
                    action,
                    () =>
                    {
                        destructiveCalls++;
                        return Task.FromResult<string?>("should-not-open.pptx");
                    },
                    _ =>
                    {
                        destructiveCalls++;
                        return Task.FromResult(true);
                    });
        });

        result.Should().BeFalse();
        destructiveCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(SaveChangesPrompt.Save, true, true)]
    [InlineData(SaveChangesPrompt.Save, false, false)]
    [InlineData(SaveChangesPrompt.DontSave, true, true)]
    [InlineData(SaveChangesPrompt.Cancel, true, false)]
    public async Task DirtyClose_ResumesOnlyAfterAllowedDecision(
        SaveChangesPrompt prompt,
        bool saveSucceeds,
        bool expectedResume)
    {
        var requestCloseCalls = 0;
        var restoreFocusCalls = 0;
        var resumedCloseCancelled = true;
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await RunOnUiThread(async () =>
        {
            var workflow = CreateWorkflow(prompt, () => Task.FromResult(saveSucceeds));
            workflow.MarkDirty();
            SisterAvaloniaAsyncWindowCloseCoordinator? closeCoordinator = null;
            closeCoordinator = new SisterAvaloniaAsyncWindowCloseCoordinator(
                () => workflow.ConfirmCloseAllowedAsync("closing"),
                requestClose: () =>
                {
                    requestCloseCalls++;
                    resumedCloseCancelled = closeCoordinator!.ShouldCancelClosing();
                    settled.TrySetResult();
                },
                restoreOwnerFocus: () =>
                {
                    restoreFocusCalls++;
                    settled.TrySetResult();
                });

            closeCoordinator.ShouldCancelClosing().Should().BeTrue();
            await settled.Task;
        });

        requestCloseCalls.Should().Be(expectedResume ? 1 : 0);
        restoreFocusCalls.Should().Be(expectedResume ? 0 : 1);
        if (expectedResume)
            resumedCloseCancelled.Should().BeFalse();
    }

    [Fact]
    public async Task ReentrantClose_SharesOneDecisionAndRequestsOneResume()
    {
        var prompt = new TaskCompletionSource<SaveChangesPrompt>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCloseCalls = 0;
        var promptCalls = 0;
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await RunOnUiThread(async () =>
        {
            var workflow = CreateWorkflow(
                promptSaveChangesAsync: _ =>
                {
                    promptCalls++;
                    return prompt.Task;
                });
            workflow.MarkDirty();
            SisterAvaloniaAsyncWindowCloseCoordinator? closeCoordinator = null;
            closeCoordinator = new SisterAvaloniaAsyncWindowCloseCoordinator(
                () => workflow.ConfirmCloseAllowedAsync("closing"),
                requestClose: () =>
                {
                    requestCloseCalls++;
                    closeCoordinator!.ShouldCancelClosing().Should().BeFalse();
                    resumed.TrySetResult();
                },
                restoreOwnerFocus: () => resumed.TrySetException(
                    new InvalidOperationException("Focus should not be restored after discard.")));

            closeCoordinator.ShouldCancelClosing().Should().BeTrue();
            closeCoordinator.ShouldCancelClosing().Should().BeTrue();
            await Task.Yield();
            promptCalls.Should().Be(1);

            prompt.SetResult(SaveChangesPrompt.DontSave);
            await resumed.Task;
        });

        requestCloseCalls.Should().Be(1);
        promptCalls.Should().Be(1);
    }

    [Fact]
    public async Task CleanClose_ResumesAfterOriginalClosingCallbackReturns()
    {
        var firstClosingReturned = false;
        var requestCloseCalls = 0;
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await RunOnUiThread(async () =>
        {
            var workflow = CreateWorkflow();
            SisterAvaloniaAsyncWindowCloseCoordinator? closeCoordinator = null;
            closeCoordinator = new SisterAvaloniaAsyncWindowCloseCoordinator(
                () => workflow.ConfirmCloseAllowedAsync("closing"),
                requestClose: () =>
                {
                    firstClosingReturned.Should().BeTrue();
                    requestCloseCalls++;
                    closeCoordinator!.ShouldCancelClosing().Should().BeFalse();
                    resumed.TrySetResult();
                },
                restoreOwnerFocus: () => resumed.TrySetException(
                    new InvalidOperationException("A clean document should close.")));

            closeCoordinator.ShouldCancelClosing().Should().BeTrue();
            firstClosingReturned = true;
            await resumed.Task;
        });

        requestCloseCalls.Should().Be(1);
    }

    [Fact]
    public async Task MainWindow_DirtyNewAndOpenCancellation_PreserveCurrentPresentation()
    {
        var newResult = true;
        var openResult = true;
        var slideCount = 0;
        var isDirty = false;

        await RunOnUiThread(async () =>
        {
            var window = new MainWindow(
                Array.Empty<string>(),
                loadRecentFilesStore: null,
                options: null,
                promptSaveChangesAsync: _ => Task.FromResult(SaveChangesPrompt.Cancel));
            window.Editor.InsertSlide();
            slideCount = window.SlideCount;

            newResult = await window.FileNewAsyncForTests();
            openResult = await window.FileOpenAsyncForTests();
            isDirty = window.IsDirty;

            window.SlideCount.Should().Be(slideCount);
        });

        newResult.Should().BeFalse();
        openResult.Should().BeFalse();
        isDirty.Should().BeTrue();
    }

    [Fact]
    public async Task FileFailure_UsesInjectedSharedModalErrorSurface()
    {
        string? summary = null;
        Exception? shownException = null;

        await RunOnUiThread(async () =>
        {
            var workflow = CreateWorkflow(
                SaveChangesPrompt.Cancel,
                showFileCommandErrorAsync: (message, exception) =>
                {
                    summary = message;
                    shownException = exception;
                    return Task.CompletedTask;
                });
            var error = new IOException("disk unavailable");

            await workflow.ShowFileCommandErrorAsync("Could not save the presentation", error);
        });

        summary.Should().Be("Could not save the presentation");
        shownException.Should().BeOfType<IOException>()
            .Which.Message.Should().Be("disk unavailable");
    }

    [Fact]
    public async Task MainWindow_SaveFailure_RoutesThroughSharedModalErrorSurface()
    {
        string? summary = null;
        Exception? shownException = null;
        var saved = true;

        await RunOnUiThread(async () =>
        {
            var window = new MainWindow(
                Array.Empty<string>(),
                loadRecentFilesStore: null,
                options: null,
                showFileCommandErrorAsync: (message, exception) =>
                {
                    summary = message;
                    shownException = exception;
                    return Task.CompletedTask;
                });

            saved = await window.TrySavePresentationFileAsyncForTests("\0invalid.pptx");
        });

        saved.Should().BeFalse();
        summary.Should().Be("Could not save the presentation");
        shownException.Should().NotBeNull();
    }

    // r174-shared-protection-readonly: the read-only title marker changes while a window lives (it
    // appears on opening a read-only file and disappears on Save-As), which the title spec's
    // captured GroupSuffix cannot express -- hence the provider. This pins that it is re-queried on
    // every refresh, and that the constructor's own RefreshTitle can call it safely.
    [Fact]
    public async Task GroupSuffixProvider_IsRequeriedOnEveryTitleRefresh()
    {
        var isReadOnly = false;
        string? atConstruction = null;
        string? whileReadOnly = null;
        string? afterClearing = null;

        await RunOnUiThread(() =>
        {
            var owner = new Window();
            var workflow = new SisterAvaloniaFileCommandWorkflow(
                owner: owner,
                titleSpec: new SisterAvaloniaFileTitleSpec("FreeP", " \u2014 "),
                maxRecentEntries: static () => 10,
                onChanged: static () => { },
                saveAsync: static () => Task.FromResult(true),
                groupSuffixProvider: () =>
                    PresentationDocumentWindowPlanner.FormatReadOnlySuffix(isReadOnly));

            atConstruction = owner.Title;
            isReadOnly = true;
            workflow.MarkDirty();
            whileReadOnly = owner.Title;
            isReadOnly = false;
            workflow.MarkSavedWithoutPath();
            afterClearing = owner.Title;
            return Task.CompletedTask;
        });

        atConstruction.Should().NotContain(PresentationDocumentWindowPlanner.ReadOnlySuffix);
        whileReadOnly.Should().Contain(PresentationDocumentWindowPlanner.ReadOnlySuffix);
        afterClearing.Should().NotContain(PresentationDocumentWindowPlanner.ReadOnlySuffix);
    }

    private static SisterAvaloniaFileCommandWorkflow CreateWorkflow(
        SaveChangesPrompt prompt = SaveChangesPrompt.Cancel,
        Func<Task<bool>>? saveAsync = null,
        Func<string, Task<SaveChangesPrompt>>? promptSaveChangesAsync = null,
        Func<string, Exception, Task>? showFileCommandErrorAsync = null) =>
        new(
            owner: new Window(),
            titleSpec: new SisterAvaloniaFileTitleSpec("FreeP", " — "),
            maxRecentEntries: static () => 10,
            onChanged: static () => { },
            saveAsync: saveAsync ?? (static () => Task.FromResult(true)),
            promptSaveChangesAsync: promptSaveChangesAsync ?? (_ => Task.FromResult(prompt)),
            showFileCommandErrorAsync: showFileCommandErrorAsync ?? (static (_, _) => Task.CompletedTask));

    private static async Task RunOnUiThread(Func<Task> action)
    {
        await Session.Dispatch(
            async () =>
            {
                await action();
                return true;
            },
            CancellationToken.None);
    }
}
