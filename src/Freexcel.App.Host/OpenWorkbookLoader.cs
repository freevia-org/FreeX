using System.Diagnostics;
using System.IO;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed class OpenWorkbookLoader
{
    private readonly Action<Workbook> _recalculateAllFormulas;
    private readonly Func<Stream, XlsxFeatureReport> _inspectXlsx;

    public OpenWorkbookLoader(
        Action<Workbook> recalculateAllFormulas,
        Func<Stream, XlsxFeatureReport>? inspectXlsx = null)
    {
        _recalculateAllFormulas = recalculateAllFormulas;
        _inspectXlsx = inspectXlsx ?? XlsxFeatureInspector.Inspect;
    }

    public async Task<OpenWorkbookResult> LoadAsync(
        string path,
        IFileAdapter adapter,
        string extension,
        FileFormatDescriptor format,
        IProgress<OpenProgressUpdate> progress)
    {
        await using var fileStream = OpenFileStream(path, progress);

        XlsxFeatureReport? featureReport = null;
        if (IsOpenXmlExcelPackageExtension(extension))
        {
            featureReport = await RunStageAsync(
                progress,
                "inspecting",
                8,
                16,
                TimeSpan.FromSeconds(4),
                () =>
                {
                    fileStream.Position = 0;
                    return _inspectXlsx(fileStream);
                });
        }

        IReadOnlyList<string> loadWarnings = [];
        var workbook = await RunStageAsync(
            progress,
            "parsing",
            16,
            90,
            TimeSpan.FromSeconds(45),
            () =>
            {
                fileStream.Position = 0;
                if (adapter is XlsxFileAdapter xlsxAdapter)
                {
                    var result = xlsxAdapter.LoadWithWarnings(fileStream);
                    loadWarnings = result.Warnings;
                    return result.Workbook;
                }
                return adapter.Load(fileStream);
            });
        ApplyTextWorkbookSheetName(workbook, extension, Path.GetFileNameWithoutExtension(path));

        if (WorkbookFormulaScanner.HasFormulas(workbook))
        {
            await RunStageAsync(
                progress,
                "calculating",
                90,
                98,
                TimeSpan.FromSeconds(12),
                () =>
                {
                    _recalculateAllFormulas(workbook);
                    return true;
                });
        }
        else
        {
            progress.Report(new OpenProgressUpdate("Opening workbook", OpenWorkbookProgressPlanner.FormatLoadingFileDetail("calculating", TimeSpan.Zero), 98));
        }

        return new OpenWorkbookResult(
            workbook,
            featureReport,
            Path.GetFileNameWithoutExtension(path),
            format.OpensAsTemplate,
            loadWarnings);
    }

    private static async Task<T> RunStageAsync<T>(
        IProgress<OpenProgressUpdate> progress,
        string detail,
        double startPercent,
        double endPercent,
        TimeSpan expectedDuration,
        Func<T> work)
    {
        progress.Report(new OpenProgressUpdate("Opening workbook", OpenWorkbookProgressPlanner.FormatLoadingFileDetail(detail, TimeSpan.Zero), startPercent));
        using var cancellation = new CancellationTokenSource();
        var progressTask = ReportStageProgressAsync(
            progress,
            detail,
            startPercent,
            endPercent,
            expectedDuration,
            cancellation.Token);

        try
        {
            return await Task.Run(work);
        }
        finally
        {
            cancellation.Cancel();
            try { await progressTask; }
            catch (OperationCanceledException) { }
            progress.Report(new OpenProgressUpdate("Opening workbook", OpenWorkbookProgressPlanner.FormatLoadingFileDetail(detail, TimeSpan.Zero), endPercent));
        }
    }

    private static async Task ReportStageProgressAsync(
        IProgress<OpenProgressUpdate> progress,
        string detail,
        double startPercent,
        double endPercent,
        TimeSpan expectedDuration,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var percent = OpenWorkbookProgressPlanner.CalculateStageProgress(startPercent, endPercent, stopwatch.Elapsed, expectedDuration);
            progress.Report(new OpenProgressUpdate("Opening workbook", OpenWorkbookProgressPlanner.FormatLoadingFileDetail(detail, stopwatch.Elapsed), percent));
        }
    }

    private static FileStream OpenFileStream(string path, IProgress<OpenProgressUpdate> progress)
    {
        progress.Report(new OpenProgressUpdate("Opening workbook", OpenWorkbookProgressPlanner.FormatLoadingFileDetail("reading", TimeSpan.Zero), 8));
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            useAsync: true);
    }

    private static void ApplyTextWorkbookSheetName(Workbook workbook, string extension, string displayName)
    {
        if (workbook.Sheets.Count != 1 || !IsTextWorkbookExtension(extension))
            return;

        workbook.Sheets[0].Name = CreateExcelCompatibleSheetName(displayName);
    }

    private static bool IsTextWorkbookExtension(string extension) =>
        extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".tsv", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".tab", StringComparison.OrdinalIgnoreCase);

    private static bool IsOpenXmlExcelPackageExtension(string extension) =>
        extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".xltx", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".xltm", StringComparison.OrdinalIgnoreCase);

    private static string CreateExcelCompatibleSheetName(string displayName)
    {
        var chars = displayName
            .Trim()
            .Select(ch => IsInvalidSheetNameCharacter(ch) ? '_' : ch)
            .ToArray();
        var sheetName = new string(chars).Trim();
        if (sheetName.Length == 0)
            sheetName = "Sheet1";
        return sheetName.Length <= 31 ? sheetName : sheetName[..31].Trim();
    }

    private static bool IsInvalidSheetNameCharacter(char ch) =>
        ch is ':' or '\\' or '/' or '?' or '*' or '[' or ']';
}

public sealed record OpenProgressUpdate(string Title, string Detail, double? Percent);

public sealed record OpenWorkbookResult(
    Workbook Workbook,
    XlsxFeatureReport? FeatureReport,
    string DisplayName,
    bool OpenedAsTemplate,
    IReadOnlyList<string>? LoadWarnings = null);
