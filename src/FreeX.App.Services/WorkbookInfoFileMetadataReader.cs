using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record WorkbookInfoFileMetadata(
    long? FileSizeBytes,
    DateTime? LastModifiedUtc,
    DateTime? LastModifiedLocal)
{
    public static WorkbookInfoFileMetadata Missing { get; } = new(null, null, null);
}

/// <summary>
/// Centralizes the safe on-disk metadata probe used by workbook Info surfaces before they hand raw
/// values to <see cref="WorkbookInfoPlanner"/>.
/// </summary>
public static class WorkbookInfoFileMetadataReader
{
    public static WorkbookInfoPlan BuildPlan(
        Workbook workbook,
        string? currentFilePath,
        int activeSheetIndex,
        bool hasUnsavedChanges = false,
        IReadOnlyCollection<CellAddress>? cyclicCells = null)
    {
        var metadata = Read(currentFilePath);
        return WorkbookInfoPlanner.Build(
            workbook,
            currentFilePath,
            activeSheetIndex,
            metadata.FileSizeBytes,
            metadata.LastModifiedUtc,
            metadata.LastModifiedLocal,
            hasUnsavedChanges,
            cyclicCells);
    }

    public static WorkbookInfoFileMetadata Read(string? currentFilePath)
    {
        if (string.IsNullOrWhiteSpace(currentFilePath))
            return WorkbookInfoFileMetadata.Missing;

        try
        {
            var info = new FileInfo(currentFilePath);
            if (!info.Exists)
                return WorkbookInfoFileMetadata.Missing;

            return new WorkbookInfoFileMetadata(
                info.Length,
                info.LastWriteTimeUtc,
                info.LastWriteTime);
        }
        catch (ArgumentException)
        {
            return WorkbookInfoFileMetadata.Missing;
        }
        catch (UnauthorizedAccessException)
        {
            return WorkbookInfoFileMetadata.Missing;
        }
        catch (PathTooLongException)
        {
            return WorkbookInfoFileMetadata.Missing;
        }
        catch (IOException)
        {
            return WorkbookInfoFileMetadata.Missing;
        }
        catch (NotSupportedException)
        {
            return WorkbookInfoFileMetadata.Missing;
        }
    }
}
