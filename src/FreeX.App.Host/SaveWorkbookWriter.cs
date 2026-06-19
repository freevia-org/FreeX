using System.IO;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class SaveWorkbookWriter
{
    private readonly WorkbookSaveService _saveService = new();

    public async Task<IReadOnlyList<string>> SaveAsync(
        string path,
        IFileAdapter adapter,
        Workbook workbook,
        IProgress<SaveProgressUpdate> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return await _saveService.SaveAsync(
            path,
            adapter,
            workbook,
            new Progress<WorkbookSaveProgressUpdate>(
                update => progress.Report(ToHostProgressUpdate(update))),
            cancellationToken);
    }

    private static SaveProgressUpdate ToHostProgressUpdate(WorkbookSaveProgressUpdate update) =>
        new(
            ProgressTitle(),
            FormatSavingFileDetail(PhaseDetail(update.Phase), update.Elapsed),
            update.Percent);

    private static string PhaseDetail(WorkbookSavePhase phase) =>
        phase switch
        {
            WorkbookSavePhase.Preparing => "serializing",
            WorkbookSavePhase.Writing => "writing",
            WorkbookSavePhase.Completed => "done",
            _ => phase.ToString().ToLowerInvariant()
        };

    private static string ProgressTitle() => UiText.Get("Progress_SavingWorkbook");

    private static string FormatSavingFileDetail(string phase, TimeSpan elapsed)
    {
        var normalizedPhase = string.IsNullOrWhiteSpace(phase)
            ? string.Empty
            : phase.Trim();
        string[] messages = normalizedPhase.ToLowerInvariant() switch
        {
            "serializing" =>
            [
                UiText.Get("Progress_SavingFileSerializing"),
                UiText.Get("Progress_SavingFileBuildingWorkbookParts"),
                UiText.Get("Progress_SavingFilePackagingSheets")
            ],
            "writing" =>
            [
                UiText.Get("Progress_SavingFileWriting"),
                UiText.Get("Progress_SavingFileWritingBytes"),
                UiText.Get("Progress_SavingFileFlushingPackage")
            ],
            "preparing" => [UiText.Get("Progress_SavingFilePreparing")],
            "done" => [UiText.Get("Progress_SavingFileDone")],
            _ => [UiText.Get("Progress_SavingFileWorking")]
        };

        var index = (int)Math.Floor(Math.Max(0, elapsed.TotalSeconds) / 3.0) % messages.Length;
        return messages[index];
    }
}

public sealed record SaveProgressUpdate(string Title, string Detail, double? Percent);
