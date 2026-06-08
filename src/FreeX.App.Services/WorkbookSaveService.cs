using System.Diagnostics;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed class WorkbookSaveService
{
    private const int BufferSize = 1024 * 128;
    private readonly IWorkbookSaveFileOperations _fileOperations;

    public WorkbookSaveService()
        : this(DefaultWorkbookSaveFileOperations.Instance)
    {
    }

    internal WorkbookSaveService(IWorkbookSaveFileOperations fileOperations)
    {
        _fileOperations = fileOperations ?? throw new ArgumentNullException(nameof(fileOperations));
    }

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
        var tempPath = CreateTemporaryPath(path, ".tmp");

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
            if (_fileOperations.FileExists(tempPath))
                _fileOperations.DeleteFile(tempPath);
        }

        ReportProgress(progress, WorkbookSavePhase.Completed, TimeSpan.Zero, 100);
    }

    private void ReplaceTargetFile(string tempPath, string path)
    {
        if (_fileOperations.FileExists(path))
        {
            try
            {
                _fileOperations.ReplaceFile(tempPath, path);
            }
            catch (Exception ex) when (IsUnsupportedReplaceFailure(ex))
            {
                ReplaceExistingFileWithFallback(tempPath, path);
            }
        }
        else
        {
            _fileOperations.MoveFile(tempPath, path, overwrite: false);
        }
    }

    private void ReplaceExistingFileWithFallback(string tempPath, string path)
    {
        var backupPath = CreateTemporaryPath(path, ".bak");
        var deleteBackup = false;
        _fileOperations.CopyFile(path, backupPath, overwrite: false);

        try
        {
            _fileOperations.MoveFile(tempPath, path, overwrite: true);
            deleteBackup = true;
        }
        catch
        {
            try
            {
                deleteBackup = RestoreFallbackBackup(path, backupPath);
            }
            catch
            {
                deleteBackup = false;
            }

            throw;
        }
        finally
        {
            if (deleteBackup && _fileOperations.FileExists(backupPath))
                _fileOperations.DeleteFile(backupPath);
        }
    }

    private bool RestoreFallbackBackup(string path, string backupPath)
    {
        if (!_fileOperations.FileExists(backupPath))
            return true;

        if (_fileOperations.FileExists(path))
            return true;

        _fileOperations.MoveFile(backupPath, path, overwrite: false);
        return true;
    }

    private static string CreateTemporaryPath(string path, string extension)
    {
        var directory = Path.GetDirectoryName(path);
        return Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? Path.GetTempPath() : directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}{extension}");
    }

    private static bool IsUnsupportedReplaceFailure(Exception exception)
    {
        if (exception is PlatformNotSupportedException or NotSupportedException)
            return true;

        if (exception is IOException ioException)
        {
            var errorCode = ioException.HResult & 0xFFFF;
            return errorCode is 38 or 45 or 50 or 95;
        }

        return false;
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

    private sealed class DefaultWorkbookSaveFileOperations : IWorkbookSaveFileOperations
    {
        public static readonly DefaultWorkbookSaveFileOperations Instance = new();

        private DefaultWorkbookSaveFileOperations()
        {
        }

        public bool FileExists(string path) => File.Exists(path);

        public void ReplaceFile(string sourcePath, string destinationPath) =>
            File.Replace(sourcePath, destinationPath, null, ignoreMetadataErrors: true);

        public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        {
            if (overwrite)
                File.Move(sourcePath, destinationPath, overwrite: true);
            else
                File.Move(sourcePath, destinationPath);
        }

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
            File.Copy(sourcePath, destinationPath, overwrite);

        public void DeleteFile(string path) => File.Delete(path);
    }
}

internal interface IWorkbookSaveFileOperations
{
    bool FileExists(string path);

    void ReplaceFile(string sourcePath, string destinationPath);

    void MoveFile(string sourcePath, string destinationPath, bool overwrite);

    void CopyFile(string sourcePath, string destinationPath, bool overwrite);

    void DeleteFile(string path);
}
