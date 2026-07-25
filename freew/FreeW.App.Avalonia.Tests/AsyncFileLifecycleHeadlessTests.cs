using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia;
using FreeW.App.Presentation.Options;

namespace FreeW.App.Avalonia.Tests;

public sealed class AsyncFileLifecycleHeadlessTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData(SaveChangesPrompt.DontSave, true, false)]
    [InlineData(SaveChangesPrompt.Cancel, false, true)]
    public async Task MainWindow_NewDocumentAsync_PreservesDirtyGateSemantics(
        SaveChangesPrompt prompt,
        bool expectedResult,
        bool expectedDirty)
    {
        var result = false;
        var dirty = false;
        var text = string.Empty;
        var title = string.Empty;
        var beforeText = string.Empty;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(prompt);
            window.Editor.InsertText("FreeW async new sentinel");
            beforeText = window.Editor.PlainText;

            result = await window.NewDocumentAsyncForTests();
            dirty = window.BuildBackstageCallbacks().GetIsDirty();
            text = window.Editor.PlainText;
            title = window.Title ?? string.Empty;

            if (!expectedResult)
                text.Should().Be(beforeText);
        });

        result.Should().Be(expectedResult);
        dirty.Should().Be(expectedDirty);
        if (expectedResult)
        {
            text.Should().BeEmpty();
            title.Should().Contain("FreeW").And.NotContain("*");
        }
        else
        {
            text.Should().Be(beforeText);
            title.Should().Contain("*");
        }
    }

    [Theory]
    [InlineData(SaveChangesPrompt.DontSave, true)]
    [InlineData(SaveChangesPrompt.Cancel, false)]
    public async Task DirtyClose_UsesAsyncConfirmAndRestoresOwnerFocus(
        SaveChangesPrompt prompt,
        bool expectedResume)
    {
        var requestCloseCalls = 0;
        var restoreFocusCalls = 0;
        var resumedCloseCancelled = true;
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await RunOnUiThread(async () =>
        {
            var workflow = CreateWorkflow(prompt);
            workflow.MarkDirty();
            SisterAvaloniaAsyncWindowCloseCoordinator? coordinator = null;
            coordinator = new SisterAvaloniaAsyncWindowCloseCoordinator(
                () => workflow.ConfirmCloseAllowedAsync("closing"),
                requestClose: () =>
                {
                    requestCloseCalls++;
                    resumedCloseCancelled = coordinator!.ShouldCancelClosing();
                    settled.TrySetResult();
                },
                restoreOwnerFocus: () =>
                {
                    restoreFocusCalls++;
                    settled.TrySetResult();
                });

            coordinator.ShouldCancelClosing().Should().BeTrue();
            await settled.Task;
        });

        requestCloseCalls.Should().Be(expectedResume ? 1 : 0);
        restoreFocusCalls.Should().Be(expectedResume ? 0 : 1);
        if (expectedResume)
            resumedCloseCancelled.Should().BeFalse();
    }

    [Fact]
    public async Task ReentrantDirtyClose_SharesOneAsyncDecision()
    {
        var prompt = new TaskCompletionSource<SaveChangesPrompt>(TaskCreationOptions.RunContinuationsAsynchronously);
        var promptCalls = 0;
        var requestCloseCalls = 0;
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await RunOnUiThread(async () =>
        {
            var workflow = CreateWorkflow(promptSaveChangesAsync: _ =>
            {
                promptCalls++;
                return prompt.Task;
            });
            workflow.MarkDirty();
            SisterAvaloniaAsyncWindowCloseCoordinator? coordinator = null;
            coordinator = new SisterAvaloniaAsyncWindowCloseCoordinator(
                () => workflow.ConfirmCloseAllowedAsync("closing"),
                requestClose: () =>
                {
                    requestCloseCalls++;
                    coordinator!.ShouldCancelClosing().Should().BeFalse();
                    resumed.TrySetResult();
                },
                restoreOwnerFocus: () => resumed.TrySetException(
                    new InvalidOperationException("Discard should resume close.")));

            coordinator.ShouldCancelClosing().Should().BeTrue();
            coordinator.ShouldCancelClosing().Should().BeTrue();
            await Task.Yield();
            promptCalls.Should().Be(1);
            prompt.SetResult(SaveChangesPrompt.DontSave);
            await resumed.Task;
        });

        promptCalls.Should().Be(1);
        requestCloseCalls.Should().Be(1);
    }

    private static MainWindow CreateWindow(SaveChangesPrompt prompt) =>
        new(
            [],
            new FreeWOptions(),
            ApplicationOptionsStore<FreeWOptions>.ForPath(
                Path.Combine(Path.GetTempPath(), "FreeW.Avalonia.Tests", Guid.NewGuid().ToString("N"), "settings.json")),
            promptSaveChangesAsync: _ => Task.FromResult(prompt));

    private static SisterAvaloniaFileCommandWorkflow CreateWorkflow(
        SaveChangesPrompt prompt = SaveChangesPrompt.Cancel,
        Func<string, Task<SaveChangesPrompt>>? promptSaveChangesAsync = null) =>
        new(
            owner: new Window(),
            titleSpec: new SisterAvaloniaFileTitleSpec("FreeW", " - "),
            maxRecentEntries: static () => 10,
            onChanged: static () => { },
            saveAsync: static () => Task.FromResult(true),
            promptSaveChangesAsync: promptSaveChangesAsync ?? (_ => Task.FromResult(prompt)));

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
