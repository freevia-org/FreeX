using System.Diagnostics;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed class WorkbookSaveService
{
    private const int BufferSize = 1024 * 128;

    public async Task SaveAsync(
        string path,
        IFileAdapter adapter,
        Workbook workbook,
        IProgress<WorkbookSaveProgressUpdate>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(workbook);

        ReportProgress(progress, WorkbookSavePhase.Preparing, TimeSpan.Zero, 1);
        var directory = Path.GetDirectoryName(path);
        var tempPath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? Path.GetTempPath() : directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await RunStageAsync(
                progress,
                WorkbookSavePhase.Writing,
                1,
                99,
                TimeSpan.FromSeconds(30),
                () =>
                {
                    using var file = new FileStream(
                        tempPath,
                        FileMode.Create,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        BufferSize,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    adapter.Save(workbook, file);
                    return true;
                }).ConfigureAwait(false);

            ReplaceTargetFile(tempPath, path);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        ReportProgress(progress, WorkbookSavePhase.Completed, TimeSpan.Zero, 100);
    }

    private static void ReplaceTargetFile(string tempPath, string path)
    {
        if (File.Exists(path))
            File.Replace(tempPath, path, null, ignoreMetadataErrors: true);
        else
            File.Move(tempPath, path);
    }

    private static async Task<T> RunStageAsync<T>(
        IProgress<WorkbookSaveProgressUpdate>? progress,
        WorkbookSavePhase phase,
        double startPercent,
        double endPercent,
        TimeSpan expectedDuration,
        Func<T> work)
    {
        ReportProgress(progress, phase, TimeSpan.Zero, startPercent);
        if (progress is null)
            return await Task.Run(work).ConfigureAwait(false);

        using var cancellation = new CancellationTokenSource();
        var progressTask = ReportStageProgressAsync(
            progress,
            phase,
            startPercent,
            endPercent,
            expectedDuration,
            cancellation.Token);

        try
        {
            return await Task.Run(work).ConfigureAwait(false);
        }
        finally
        {
            cancellation.Cancel();
            try { await progressTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            ReportProgress(progress, phase, TimeSpan.Zero, endPercent);
        }
    }

    private static async Task ReportStageProgressAsync(
        IProgress<WorkbookSaveProgressUpdate> progress,
        WorkbookSavePhase phase,
        double startPercent,
        double endPercent,
        TimeSpan expectedDuration,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var percent = CalculateStageProgress(startPercent, endPercent, stopwatch.Elapsed, expectedDuration);
            ReportProgress(progress, phase, stopwatch.Elapsed, percent);
        }
    }

    private static void ReportProgress(
        IProgress<WorkbookSaveProgressUpdate>? progress,
        WorkbookSavePhase phase,
        TimeSpan elapsed,
        double? percent)
    {
        progress?.Report(new WorkbookSaveProgressUpdate(phase, elapsed, percent));
    }

    private static double CalculateStageProgress(
        double startPercent,
        double endPercent,
        TimeSpan elapsed,
        TimeSpan expectedDuration)
    {
        if (expectedDuration <= TimeSpan.Zero)
            return endPercent;

        var ratio = Math.Clamp(elapsed.TotalMilliseconds / expectedDuration.TotalMilliseconds, 0, 0.92);
        return startPercent + ((endPercent - startPercent) * ratio);
    }
}
