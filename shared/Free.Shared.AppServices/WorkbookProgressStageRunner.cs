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
        Func<TPhase, TimeSpan, double?, TProgressUpdate> createUpdate)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(createUpdate);
        cancellationToken.ThrowIfCancellationRequested();

        ReportProgress(progress, createUpdate, phase, TimeSpan.Zero, startPercent);
        if (progress is null)
            return await Task.Run(work, cancellationToken).ConfigureAwait(false);

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
            return await Task.Run(work, cancellationToken).ConfigureAwait(false);
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
