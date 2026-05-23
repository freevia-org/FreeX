using System.Diagnostics;
using System.IO;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed class SaveWorkbookWriter
{
    private const int BufferSize = 1024 * 128;

    public async Task SaveAsync(
        string path,
        IFileAdapter adapter,
        Workbook workbook,
        IProgress<SaveProgressUpdate> progress)
    {
        var bytes = await RunStageAsync(
            progress,
            "serializing",
            1,
            85,
            TimeSpan.FromSeconds(30),
            () =>
            {
                using var memory = new MemoryStream();
                adapter.Save(workbook, memory);
                return memory.ToArray();
            });

        await WriteFileBytesWithProgressAsync(path, bytes, progress);
        progress.Report(new SaveProgressUpdate("Saving workbook", FormatSavingFileDetail("done", TimeSpan.Zero), 100));
    }

    private static async Task<T> RunStageAsync<T>(
        IProgress<SaveProgressUpdate> progress,
        string detail,
        double startPercent,
        double endPercent,
        TimeSpan expectedDuration,
        Func<T> work)
    {
        progress.Report(new SaveProgressUpdate("Saving workbook", FormatSavingFileDetail(detail, TimeSpan.Zero), startPercent));
        using var cancellation = new CancellationTokenSource();
        var progressTask = ReportStageProgressAsync(
            progress,
            detail,
            startPercent,
            endPercent,
            expectedDuration,
            cancellation.Token);

        try
        {
            return await Task.Run(work);
        }
        finally
        {
            cancellation.Cancel();
            try { await progressTask; }
            catch (OperationCanceledException) { }
            progress.Report(new SaveProgressUpdate("Saving workbook", FormatSavingFileDetail(detail, TimeSpan.Zero), endPercent));
        }
    }

    private static async Task ReportStageProgressAsync(
        IProgress<SaveProgressUpdate> progress,
        string detail,
        double startPercent,
        double endPercent,
        TimeSpan expectedDuration,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var percent = OpenWorkbookProgressPlanner.CalculateStageProgress(startPercent, endPercent, stopwatch.Elapsed, expectedDuration);
            progress.Report(new SaveProgressUpdate("Saving workbook", FormatSavingFileDetail(detail, stopwatch.Elapsed), percent));
        }
    }

    private static async Task WriteFileBytesWithProgressAsync(
        string path,
        byte[] bytes,
        IProgress<SaveProgressUpdate> progress)
    {
        progress.Report(new SaveProgressUpdate("Saving workbook", FormatSavingFileDetail("writing", TimeSpan.Zero), 85));
        await using var file = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            useAsync: true);

        var startTimestamp = Stopwatch.GetTimestamp();
        var written = 0;
        while (written < bytes.Length)
        {
            var count = Math.Min(BufferSize, bytes.Length - written);
            await file.WriteAsync(bytes.AsMemory(written, count));
            written += count;

            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            var percent = bytes.Length == 0
                ? 99
                : 85 + written * 14d / bytes.Length;
            progress.Report(new SaveProgressUpdate("Saving workbook", FormatSavingFileDetail("writing", elapsed), percent));
        }
    }

    private static string FormatSavingFileDetail(string phase, TimeSpan elapsed)
    {
        var normalizedPhase = string.IsNullOrWhiteSpace(phase)
            ? "working"
            : phase.Trim();
        string[] messages = normalizedPhase.ToLowerInvariant() switch
        {
            "serializing" =>
            [
                "Saving file (serializing)",
                "Saving file (building workbook parts)",
                "Saving file (packaging sheets)"
            ],
            "writing" =>
            [
                "Saving file (writing)",
                "Saving file (writing bytes)",
                "Saving file (flushing package)"
            ],
            "done" => ["Saving file (done)"],
            _ => [$"Saving file ({normalizedPhase})"]
        };

        var index = (int)Math.Floor(Math.Max(0, elapsed.TotalSeconds) / 3.0) % messages.Length;
        return messages[index];
    }
}

public sealed record SaveProgressUpdate(string Title, string Detail, double? Percent);
