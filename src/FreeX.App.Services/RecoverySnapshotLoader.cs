using FreeX.Core.IO;

namespace FreeX.App.Services;

public static class RecoverySnapshotLoader
{
    public static StartupWorkbookLoadResult Load(string snapshotPath, string? originalFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);

        using var stream = File.OpenRead(snapshotPath);
        var workbook = new NativeJsonAdapter().Load(stream);
        var displayName = string.IsNullOrWhiteSpace(originalFilePath)
            ? workbook.Name
            : Path.GetFileName(originalFilePath);
        workbook.Name = displayName;

        return new StartupWorkbookLoadResult(
            workbook,
            displayName,
            "Recovered from a previous session.",
            IsFallback: false,
            SourcePath: originalFilePath);
    }
}
