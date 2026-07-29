using System.IO;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class OpenWorkbookLoader
{
    private readonly WorkbookOpenService _openService;

    public OpenWorkbookLoader(
        Action<Workbook> recalculateAllFormulas,
        Func<Stream, XlsxFeatureReport>? inspectXlsx = null,
        long maxFileBytes = WorkbookOpenSizeGuard.DefaultMaxFileBytes)
    {
        _openService = new WorkbookOpenService(recalculateAllFormulas, inspectXlsx, maxFileBytes);
    }

    public async Task<OpenWorkbookResult> LoadAsync(
        string path,
        IFileAdapter adapter,
        string extension,
        FileFormatDescriptor format,
        IProgress<OpenProgressUpdate> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var result = await _openService.LoadAsync(
            path,
            adapter,
            extension,
            format,
            new Progress<WorkbookOpenProgressUpdate>(
                update => progress.Report(ToHostProgressUpdate(update))),
            cancellationToken).ConfigureAwait(false);

        return new OpenWorkbookResult(
            result.Workbook,
            result.FeatureReport,
            result.DisplayName,
            result.OpenedAsTemplate,
            result.LoadWarnings,
            result.SourceLastWriteTimeUtc);
    }

    private static OpenProgressUpdate ToHostProgressUpdate(WorkbookOpenProgressUpdate update) =>
        FromSharedText(WorkbookProgressTextFormatter.FormatOpen(update, UiText.Get));

    private static OpenProgressUpdate FromSharedText(WorkbookProgressText text) =>
        new(text.Title, text.Detail, text.Percent);
}

public sealed record OpenProgressUpdate(string Title, string Detail, double? Percent);

public sealed record OpenWorkbookResult(
    Workbook Workbook,
    XlsxFeatureReport? FeatureReport,
    string DisplayName,
    bool OpenedAsTemplate,
    IReadOnlyList<string>? LoadWarnings = null,
    // Snapshot of the source file's on-disk write time taken at open (see
    // WorkbookOpenResult.SourceLastWriteTimeUtc). The host stashes this alongside
    // _currentFilePath and threads it into SaveWorkbookWriter.SaveAsync so a save that would
    // silently overwrite a concurrent external edit is caught instead
    // (WorkbookExternallyModifiedException).
    DateTime? SourceLastWriteTimeUtc = null);
