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

    public async Task<IReadOnlyList<string>> SaveAsync(
        string path,
        IFileAdapter adapter,
        Workbook workbook,
        IProgress<WorkbookSaveProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(workbook);
        cancellationToken.ThrowIfCancellationRequested();

        ReportProgress(progress, WorkbookSavePhase.Preparing, TimeSpan.Zero, 1);
        var tempPath = CreateTemporaryPath(path, ".tmp");
        var estimatedBytes = EstimateWorkbookByteSize(workbook);
        IReadOnlyList<string> saveWarnings = [];

        try
        {
            saveWarnings = await WorkbookProgressStageRunner.RunStageAsync(
                progress,
                WorkbookSavePhase.Writing,
                1,
                99,
                WorkbookProgressStageRunner.EstimateStageDuration(estimatedBytes, secondsPerMegabyte: 1.0, floorSeconds: 0.4),
                cancellationToken,
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var file = new FileStream(
                        tempPath,
                        FileMode.Create,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        BufferSize,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    if (adapter is XlsxFileAdapter xlsxAdapter)
                    {
                        var result = xlsxAdapter.SaveWithWarnings(workbook, file);
                        cancellationToken.ThrowIfCancellationRequested();
                        return result.Warnings;
                    }

                    adapter.Save(workbook, file);
                    cancellationToken.ThrowIfCancellationRequested();
                    return [];
                },
                CreateProgressUpdate).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            ReplaceTargetFile(tempPath, path);
        }
        finally
        {
            if (_fileOperations.FileExists(tempPath))
                _fileOperations.DeleteFile(tempPath);
        }

        ReportProgress(progress, WorkbookSavePhase.Completed, TimeSpan.Zero, 100);
        return saveWarnings;
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

    private static void ReportProgress(
        IProgress<WorkbookSaveProgressUpdate>? progress,
        WorkbookSavePhase phase,
        TimeSpan elapsed,
        double? percent)
    {
        progress?.Report(new WorkbookSaveProgressUpdate(phase, elapsed, percent));
    }

    private static WorkbookSaveProgressUpdate CreateProgressUpdate(
        WorkbookSavePhase phase,
        TimeSpan elapsed,
        double? percent) =>
        new(phase, elapsed, percent);

    // Rough estimate of the serialized package size from the populated cell count (CellCount is O(1)
    // per sheet).  Used only to size the progress bar's expected duration; ~6 bytes/cell tracks the
    // compressed-xlsx density of large dense workbooks closely enough for a linear-feeling bar.
    private static long EstimateWorkbookByteSize(Workbook workbook)
    {
        long cells = 0;
        foreach (var sheet in workbook.Sheets)
            cells += sheet.CellCount;
        return Math.Max(64L * 1024, cells * 6);
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
