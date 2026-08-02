using System.Diagnostics;

namespace Free.Shared.AppServices;

public static class WorkbookProgressStageRunner
{
    private static readonly TimeSpan ProgressTimerInterval = TimeSpan.FromMilliseconds(250);

    public static TimeSpan EstimateStageDuration(long sizeBytes, double secondsPerMegabyte, double floorSeconds)
    {
        var megabytes = Math.Max(0, sizeBytes) / (1024.0 * 1024.0);
        return TimeSpan.FromSeconds(Math.Max(floorSeconds, megabytes * secondsPerMegabyte));
    }

    public static async Task<T> RunStageAsync<T, TPhase, TProgressUpdate>(
        IProgress<TProgressUpdate>? progress,
        TPhase phase,
        double startPercent,
        double endPercent,
        TimeSpan expectedDuration,
        CancellationToken cancellationToken,
        Func<T> work,
        Func<TPhase, TimeSpan, double?, TProgressUpdate> createUpdate,
        // R119-appservices-progress-stage-runner-cancel-detach: by default this stage blocks until
        // `work` actually returns even after cancellation is requested -- see RunWorkAsync below for
        // why that is the SAFE choice for a caller (WorkbookSaveService's Writing stage) that is
        // serializing the live, possibly cross-window-shared Workbook. Pass true only when `work`
        // does not touch anything another thread could observe/mutate concurrently once this method
        // returns early (WorkbookOpenService's Inspecting/Parsing/Calculating stages, which only ever
        // touch a private FileStream and a not-yet-published Workbook).
        bool observeCancellationEagerly = false)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(createUpdate);
        cancellationToken.ThrowIfCancellationRequested();

        ReportProgress(progress, createUpdate, phase, TimeSpan.Zero, startPercent);
        if (progress is null)
            return await RunWorkAsync(work, cancellationToken, observeCancellationEagerly).ConfigureAwait(false);

        using var progressCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progressTask = ReportStageProgressAsync(
            progress,
            phase,
            startPercent,
            endPercent,
            expectedDuration,
            createUpdate,
            progressCancellation.Token);

        try
        {
            return await RunWorkAsync(work, cancellationToken, observeCancellationEagerly).ConfigureAwait(false);
        }
        finally
        {
            progressCancellation.Cancel();
            try { await progressTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            if (!cancellationToken.IsCancellationRequested)
                ReportProgress(progress, createUpdate, phase, TimeSpan.Zero, endPercent);
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the thread pool. By default this is exactly
    /// <c>await Task.Run(work, cancellationToken)</c>: cancelling <paramref name="cancellationToken"/>
    /// only prevents scheduling if <paramref name="work"/> has not started yet -- once it is running,
    /// synchronous work (a FileStream write, an XML parse) has nothing inside it that observes the
    /// token, so this still waits for it to actually return (or throw) before completing. That is a
    /// deliberate, load-bearing choice for WorkbookSaveService's Writing stage: it is serializing the
    /// LIVE Workbook (possibly shared with other "New Window" sibling views of the same document --
    /// see MainWindow.Backstage.cs's AdjustSaveGate/"torn snapshot" comment), so returning to the
    /// caller while that read is still in flight on another thread would let the UI resume mutating
    /// the very object the background thread is enumerating.
    ///
    /// When <paramref name="observeCancellationEagerly"/> is true, a caller that does NOT carry that
    /// live-object hazard (WorkbookOpenService's Inspecting/Parsing/Calculating stages -- a private
    /// FileStream plus a fresh Workbook nothing else can reach until LoadAsync returns) can opt in to
    /// unblocking the instant cancellation is requested, without waiting for the in-flight synchronous
    /// work to finish. Without this, an unresponsive/disconnected network path turns "Cancel" into a
    /// button that can never take effect (R119). The abandoned work is still left to run to completion
    /// on its own thread-pool thread; its eventual result or exception is observed via a fire-and-forget
    /// continuation so it never surfaces as an unobserved task exception.
    /// </summary>
    private static async Task<T> RunWorkAsync<T>(
        Func<T> work,
        CancellationToken cancellationToken,
        bool observeCancellationEagerly)
    {
        var runTask = Task.Run(work, cancellationToken);
        if (!observeCancellationEagerly || !cancellationToken.CanBeCanceled)
            return await runTask.ConfigureAwait(false);

        var cancellationSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            cancellationSignal);

        var firstCompleted = await Task.WhenAny(runTask, cancellationSignal.Task).ConfigureAwait(false);
        if (!ReferenceEquals(firstCompleted, runTask))
        {
            // Cancellation fired before `work` finished on its own. Stop waiting for it here, but
            // keep observing its eventual completion so a later fault doesn't surface as an
            // unobserved task exception once nothing else references this task.
            _ = runTask.ContinueWith(
                static faulted => _ = faulted.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return await runTask.ConfigureAwait(false);
    }

    private static async Task ReportStageProgressAsync<TPhase, TProgressUpdate>(
        IProgress<TProgressUpdate> progress,
        TPhase phase,
        double startPercent,
        double endPercent,
        TimeSpan expectedDuration,
        Func<TPhase, TimeSpan, double?, TProgressUpdate> createUpdate,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(ProgressTimerInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var percent = WorkbookProgressPresentationPlanner.CalculateServiceStagePercent(
                startPercent,
                endPercent,
                stopwatch.Elapsed,
                expectedDuration);
            ReportProgress(progress, createUpdate, phase, stopwatch.Elapsed, percent);
        }
    }

    private static void ReportProgress<TPhase, TProgressUpdate>(
        IProgress<TProgressUpdate>? progress,
        Func<TPhase, TimeSpan, double?, TProgressUpdate> createUpdate,
        TPhase phase,
        TimeSpan elapsed,
        double? percent)
    {
        progress?.Report(createUpdate(phase, elapsed, percent));
    }
}
