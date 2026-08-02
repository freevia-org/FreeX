using FluentAssertions;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R119-appservices-progress-stage-runner-cancel-detach: WorkbookProgressStageRunner.RunStageAsync
/// wrapped every save/open phase in <c>await Task.Run(work, cancellationToken)</c>. Cancelling the
/// token only prevented scheduling if `work` had not started yet -- once it was running on the
/// thread pool, nothing aborted it, so a cancellation requested mid-flight was only ever observed
/// once `work` returned on its own (or never, if the underlying I/O hung on an unresponsive
/// network path). These tests exercise the shared runner directly.
/// </summary>
public sealed class R119_WorkbookProgressStageRunnerCancellationTests
{
    [Fact]
    public async Task RunStageAsync_ObserveCancellationEagerly_ReturnsPromptlyWhileWorkStillRuns()
    {
        // FAIL-BEFORE-FIX: before the fix, RunStageAsync always awaited the full Task.Run to
        // completion regardless of cancellation, so this assertion failed (it took ~2s, not <1s).
        using var workStarted = new ManualResetEventSlim();
        using var releaseWork = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();

        var stageTask = WorkbookProgressStageRunner.RunStageAsync<int, string, string>(
            progress: null,
            phase: "stage",
            startPercent: 0,
            endPercent: 100,
            expectedDuration: TimeSpan.FromMilliseconds(10),
            cancellation.Token,
            work: () =>
            {
                workStarted.Set();
                // Bounded so the background thread always finishes on its own instead of leaking
                // an indefinitely-blocked thread-pool thread past the end of this test.
                releaseWork.Wait(TimeSpan.FromSeconds(10));
                return 42;
            },
            createUpdate: (phase, elapsed, percent) => phase,
            observeCancellationEagerly: true);

        workStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("the work delegate must have started");
        cancellation.Cancel();

        var act = async () => await stageTask;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await act.Should().ThrowAsync<OperationCanceledException>();
        // Generous bound: this only needs to prove RunStageAsync returned long before the 10-second
        // block releases (never, under the pre-fix behavior) -- not race a tight deadline against
        // thread-pool scheduling latency under a busy parallel test run.
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(5),
            "cancellation must be observed immediately instead of waiting for the still-running work to finish");

        releaseWork.Set();
    }

    [Fact]
    public async Task RunStageAsync_DefaultBehavior_StillWaitsForWorkToFinishAfterCancellation()
    {
        // No-regression sibling: the default (observeCancellationEagerly: false, i.e. omitted) path
        // must keep the ORIGINAL blocking semantics. This is load-bearing for
        // WorkbookSaveService's Writing stage, which serializes the live, possibly cross-window-
        // shared Workbook and must not let the caller resume mutating it while the background write
        // is still reading it (see WorkbookSaveService.cs's R119 comment on that call).
        using var workStarted = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var workCompleted = false;

        var stageTask = WorkbookProgressStageRunner.RunStageAsync<int, string, string>(
            progress: null,
            phase: "stage",
            startPercent: 0,
            endPercent: 100,
            expectedDuration: TimeSpan.FromMilliseconds(10),
            cancellation.Token,
            work: () =>
            {
                workStarted.Set();
                Thread.Sleep(300);
                workCompleted = true;
                // Mirrors WorkbookSaveService's Writing-stage delegate, whose only cancellation
                // checks bracket the synchronous work itself (nothing inside the work observes the
                // token) -- the check that turns a mid-flight cancellation into an
                // OperationCanceledException only runs once the synchronous work has returned.
                cancellation.Token.ThrowIfCancellationRequested();
                return 42;
            },
            createUpdate: (phase, elapsed, percent) => phase);

        workStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("the work delegate must have started");
        cancellation.Cancel();

        var act = async () => await stageTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
        workCompleted.Should().BeTrue(
            "the default (non-eager) path must not return to the caller until the in-flight work has actually finished");
    }

    [Fact]
    public async Task RunStageAsync_ObserveCancellationEagerly_NoCancellationRequested_StillReturnsWorkResult()
    {
        // No-regression sibling: when cancellation never fires, the eager-observation opt-in must
        // not change the normal, successful result path.
        using var cancellation = new CancellationTokenSource();

        var result = await WorkbookProgressStageRunner.RunStageAsync<int, string, string>(
            progress: null,
            phase: "stage",
            startPercent: 0,
            endPercent: 100,
            expectedDuration: TimeSpan.FromMilliseconds(10),
            cancellation.Token,
            work: () => 7,
            createUpdate: (phase, elapsed, percent) => phase,
            observeCancellationEagerly: true);

        result.Should().Be(7);
    }
}
