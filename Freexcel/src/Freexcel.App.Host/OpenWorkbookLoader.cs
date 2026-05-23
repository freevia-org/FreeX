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
        var bytes = await ReadFileBytesWithProgressAsync(path, progress);

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
                    using var inspectStream = new MemoryStream(bytes, writable: false);
                    return _inspectXlsx(inspectStream);
                });
        }

        var workbook = await RunStageAsync(
            progress,
            "parsing",
            16,
            90,
            TimeSpan.FromSeconds(45),
            () =>
            {
                using var loadStream = new MemoryStream(bytes, writable: false);
                return adapter.Load(loadStream);
            });
        ApplyTextWorkbookSheetName(workbook, extension, Path.GetFileNameWithoutExtension(path));

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

        return new OpenWorkbookResult(
            workbook,
            featureReport,
            Path.GetFileNameWithoutExtension(path),
            format.OpensAsTemplate);
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

    private static async Task<byte[]> ReadFileBytesWithProgressAsync(
        string path,
        IProgress<OpenProgressUpdate> progress)
    {
        progress.Report(new OpenProgressUpdate("Opening workbook", OpenWorkbookProgressPlanner.FormatLoadingFileDetail("reading", TimeSpan.Zero), 1));
        await using var file = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            useAsync: true);

        var total = file.Length;
        using var memory = new MemoryStream(total > int.MaxValue ? 0 : (int)total);
        var buffer = new byte[1024 * 128];
        long readTotal = 0;
        var startTimestamp = Stopwatch.GetTimestamp();

        while (true)
        {
            var read = await file.ReadAsync(buffer);
            if (read == 0)
                break;

            memory.Write(buffer, 0, read);
            readTotal += read;
            if (total > 0)
            {
                var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
                var percent = 1 + readTotal * 7d / total;
                progress.Report(new OpenProgressUpdate(
                    "Opening workbook",
                    OpenWorkbookProgressPlanner.FormatLoadingFileDetail("reading", elapsed),
                    percent));
            }
        }

        return memory.ToArray();
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
    bool OpenedAsTemplate);
