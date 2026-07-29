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
        CancellationToken cancellationToken = default,
        DateTime? expectedLastWriteTimeUtc = null)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return await _saveService.SaveAsync(
            path,
            adapter,
            workbook,
            new Progress<WorkbookSaveProgressUpdate>(
                update => progress.Report(ToHostProgressUpdate(update))),
            cancellationToken,
            expectedLastWriteTimeUtc);
    }

    private static SaveProgressUpdate ToHostProgressUpdate(WorkbookSaveProgressUpdate update) =>
        FromSharedText(WorkbookProgressTextFormatter.FormatSave(update, UiText.Get));

    private static SaveProgressUpdate FromSharedText(WorkbookProgressText text) =>
        new(text.Title, text.Detail, text.Percent);
}

public sealed record SaveProgressUpdate(string Title, string Detail, double? Percent);
