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
        CancellationToken cancellationToken = default,
        DateTime? expectedLastWriteTimeUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(workbook);
        cancellationToken.ThrowIfCancellationRequested();

        // Detect a concurrent second writer: if the caller captured the file's write time at open
        // (WorkbookOpenResult.SourceLastWriteTimeUtc) and the file on disk has a different write
        // time now, someone else changed it since we read it -- writing over it here would silently
        // discard their changes. This is a best-effort check-then-act (not a held file lock), but it
        // catches the common "another instance/colleague saved while I was still editing" case.
        if (expectedLastWriteTimeUtc is { } expectedWriteTimeUtc &&
            _fileOperations.FileExists(path) &&
            _fileOperations.GetLastWriteTimeUtc(path) != expectedWriteTimeUtc)
        {
            throw new WorkbookExternallyModifiedException(path);
        }

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

        public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

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

    DateTime GetLastWriteTimeUtc(string path);

    void ReplaceFile(string sourcePath, string destinationPath);

    void MoveFile(string sourcePath, string destinationPath, bool overwrite);

    void CopyFile(string sourcePath, string destinationPath, bool overwrite);

    void DeleteFile(string path);
}

/// <summary>
/// Thrown by <see cref="WorkbookSaveService.SaveAsync"/> when the caller passed the file's write
/// time from open (<c>expectedLastWriteTimeUtc</c>, sourced from
/// <see cref="WorkbookOpenResult.SourceLastWriteTimeUtc"/>) and the target file on disk has since
/// been modified by someone else -- a second FreeX/Excel instance, or a colleague on a shared
/// path. Hosts should catch this the same way they catch <see cref="OperationCanceledException"/>
/// around the save call and prompt the user (overwrite anyway / reload / save-as) instead of
/// silently clobbering the other writer's changes.
/// </summary>
public sealed class WorkbookExternallyModifiedException(string path)
    : Exception($"'{path}' was modified by another program since it was opened.")
{
    public string Path { get; } = path;
}
