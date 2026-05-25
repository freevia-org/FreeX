using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Freexcel.Core.IO;

namespace Freexcel.App.Host.Tests;

public sealed class HeavyWorkbookRetestTests
{
    private const string DefaultHeavyWorkbookPath = @"E:\Users\quinn\Documents\Samples\Acme\Sample Dashboard 20250116.xlsx";

    [Fact]
    [Trait("Category", "ExternalWorkbook")]
    public async Task PartnerDashboardWorkbook_OpensAndSavesWithinSmokeBudget()
    {
        var sourcePath = ResolveHeavyWorkbookPath();
        if (sourcePath is null)
            return;

        var adapter = new XlsxFileAdapter();
        var loader = new OpenWorkbookLoader(_ => { });
        var openProgress = new List<OpenProgressUpdate>();

        var openStopwatch = Stopwatch.StartNew();
        var openResult = await loader.LoadAsync(
            sourcePath,
            adapter,
            ".xlsx",
            new FileFormatDescriptor(".xlsx", "Excel Workbook", CanOpen: true, CanSave: true),
            new ImmediateProgress<OpenProgressUpdate>(openProgress.Add));
        openStopwatch.Stop();

        openResult.Workbook.SheetCount.Should().BeGreaterThan(0);
        openProgress.Should().Contain(update => update.Detail.StartsWith("Loading file (reading)", StringComparison.Ordinal));
        openProgress.Should().Contain(update => update.Percent == 98);
        openStopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60));

        var savePath = Path.Combine(Path.GetTempPath(), $"freexcel-heavy-retest-{Guid.NewGuid():N}.xlsx");
        try
        {
            var saveProgress = new List<SaveProgressUpdate>();
            var saveStopwatch = Stopwatch.StartNew();
            await new SaveWorkbookWriter().SaveAsync(
                savePath,
                adapter,
                openResult.Workbook,
                new ImmediateProgress<SaveProgressUpdate>(saveProgress.Add));
            saveStopwatch.Stop();

            File.Exists(savePath).Should().BeTrue();
            new FileInfo(savePath).Length.Should().BeGreaterThan(0);
            saveProgress.Should().Contain(update => update.Detail.StartsWith("Saving file (writing)", StringComparison.Ordinal));
            saveProgress.Should().Contain(update => update.Percent == 100);
            saveStopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(90));
        }
        finally
        {
            File.Delete(savePath);
        }
    }

    private static string? ResolveHeavyWorkbookPath()
    {
        var configured = Environment.GetEnvironmentVariable("FREEXCEL_HEAVY_WORKBOOK_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        return File.Exists(DefaultHeavyWorkbookPath) ? DefaultHeavyWorkbookPath : null;
    }

    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
