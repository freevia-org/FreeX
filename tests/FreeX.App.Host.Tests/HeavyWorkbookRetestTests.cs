using System.Diagnostics;
using System.IO;
using FluentAssertions;
using FreeX.Core.IO;
using Xunit.Sdk;

namespace FreeX.App.Host.Tests;

public sealed class HeavyWorkbookRetestTests
{
    private const string DefaultHeavyWorkbookPath = @"E:\Users\quinn\Documents\Samples\Acme\Sample Dashboard 20250116.xlsx";

    [HeavyWorkbookRetestFact]
    [Trait("Category", "ExternalWorkbook")]
    public async Task PartnerDashboardWorkbook_OpensAndSavesWithinSmokeBudget()
    {
        var sourcePath = ResolveHeavyWorkbookPath(out var configuredByEnvironment)
            ?? throw SkipException.ForSkip(HeavyWorkbookRetestFactAttribute.SkipReasonWhenMissing);

        var adapter = new XlsxFileAdapter();
        var loader = new WorkbookOpenService(_ => { });
        var openProgress = new List<WorkbookOpenProgressUpdate>();

        var openStopwatch = Stopwatch.StartNew();
        WorkbookOpenResult openResult;
        try
        {
            openResult = await loader.LoadAsync(
                sourcePath,
                adapter,
                ".xlsx",
                new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
                new TestProgress<WorkbookOpenProgressUpdate>(openProgress.Add));
        }
        catch (Exception ex) when (!configuredByEnvironment)
        {
            throw SkipException.ForSkip(
                $"Heavy workbook retest documented local workbook could not be loaded in this environment: {ex.GetType().Name}: {ex.Message}");
        }

        openStopwatch.Stop();

        openResult.Workbook.SheetCount.Should().BeGreaterThan(0);
        openProgress.Should().Contain(update => WorkbookProgressTextFormatter
            .FormatOpen(update, UiText.Get).Detail.StartsWith("Loading file (reading)", StringComparison.Ordinal));
        openProgress.Should().Contain(update => update.Percent == 98);
        openStopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60));

        using var temp = new TestTemporaryDirectory();
        var savePath = Path.Combine(temp.Path, "heavy-retest.xlsx");
        var saveProgress = new List<WorkbookSaveProgressUpdate>();
        var saveStopwatch = Stopwatch.StartNew();
        await new WorkbookSaveService().SaveAsync(
            savePath,
            adapter,
            openResult.Workbook,
            new TestProgress<WorkbookSaveProgressUpdate>(saveProgress.Add));
        saveStopwatch.Stop();

        File.Exists(savePath).Should().BeTrue();
        new FileInfo(savePath).Length.Should().BeGreaterThan(0);
        saveProgress.Should().Contain(update => WorkbookProgressTextFormatter
            .FormatSave(update, UiText.Get).Detail.StartsWith("Saving file (writing)", StringComparison.Ordinal));
        saveProgress.Should().Contain(update => update.Percent == 100);
        saveStopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(90));
    }

    private static string? ResolveHeavyWorkbookPath(out bool configuredByEnvironment)
    {
        var configured = Environment.GetEnvironmentVariable("FREEX_HEAVY_WORKBOOK_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            configuredByEnvironment = true;
            return configured;
        }

        if (!string.IsNullOrWhiteSpace(configured))
            throw new FileNotFoundException("FREEX_HEAVY_WORKBOOK_PATH does not point to an existing workbook.", configured);

        configuredByEnvironment = false;
        return File.Exists(DefaultHeavyWorkbookPath) ? DefaultHeavyWorkbookPath : null;
    }

    private sealed class HeavyWorkbookRetestFactAttribute : FactAttribute
    {
        public const string SkipReasonWhenMissing =
            "Heavy workbook retest requires FREEX_HEAVY_WORKBOOK_PATH or the documented local workbook path.";

        private const string SkipReasonWhenUsingDefaultLocalWorkbook =
            "Heavy workbook retest uses the documented local workbook only when FREEX_HEAVY_WORKBOOK_PATH is explicitly set for this test lane.";

        public HeavyWorkbookRetestFactAttribute()
        {
            var sourcePath = ResolveHeavyWorkbookPath(out var configuredByEnvironment);
            Skip = sourcePath is null
                ? SkipReasonWhenMissing
                : configuredByEnvironment
                    ? null
                    : SkipReasonWhenUsingDefaultLocalWorkbook;
        }
    }
}
