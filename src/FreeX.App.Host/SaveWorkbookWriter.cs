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
            FormatSavingFileDetail(
                WorkbookProgressPresentationPlanner.ToSaveProgressStep(update.Phase),
                update.Elapsed),
            update.Percent);

    public static string ProgressTitle() =>
        UiText.Get(WorkbookProgressPresentationPlanner.SaveTitleResourceKey);

    public static string FormatSavingFileDetail(string phase, TimeSpan elapsed) =>
        FormatSavingFileDetail(
            WorkbookProgressPresentationPlanner.ParseSaveProgressStep(phase),
            elapsed);

    public static string FormatSavingFileDetail(WorkbookSaveProgressStep step, TimeSpan elapsed) =>
        UiText.Get(WorkbookProgressPresentationPlanner.SelectSaveDetailResourceKey(step, elapsed));
}

public sealed record SaveProgressUpdate(string Title, string Detail, double? Percent);
