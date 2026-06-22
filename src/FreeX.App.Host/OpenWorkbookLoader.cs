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
            result.LoadWarnings);
    }

    private static OpenProgressUpdate ToHostProgressUpdate(WorkbookOpenProgressUpdate update) =>
        new(
            OpenWorkbookProgressPlanner.ProgressTitle(),
            OpenWorkbookProgressPlanner.FormatLoadingFileDetail(
                WorkbookProgressPresentationPlanner.ToOpenProgressStep(update.Phase),
                update.Elapsed),
            update.Percent);
}

public sealed record OpenProgressUpdate(string Title, string Detail, double? Percent);

public sealed record OpenWorkbookResult(
    Workbook Workbook,
    XlsxFeatureReport? FeatureReport,
    string DisplayName,
    bool OpenedAsTemplate,
    IReadOnlyList<string>? LoadWarnings = null);
