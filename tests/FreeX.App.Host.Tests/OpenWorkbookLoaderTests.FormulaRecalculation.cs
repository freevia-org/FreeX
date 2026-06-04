using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class OpenWorkbookLoaderTests
{
    [Fact]
    public async Task LoadAsync_ReadsLoadsRecalculatesAndReportsProgress()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.fxjson");
        await File.WriteAllTextAsync(tempPath, "payload");
        try
        {
            var recalculateCalled = false;
            var adapter = new TestFileAdapter(stream =>
            {
                using var reader = new StreamReader(stream);
                reader.ReadToEnd().Should().Be("payload");
                var workbook = new Workbook("Loaded");
                var sheet = workbook.AddSheet("Sheet1");
                sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromFormula("1+1"));
                return workbook;
            });
            var progressUpdates = new List<OpenProgressUpdate>();
            var loader = new OpenWorkbookLoader(recalculateAllFormulas: workbook =>
            {
                workbook.Name.Should().Be("Loaded");
                recalculateCalled = true;
            });

            var result = await loader.LoadAsync(
                tempPath,
                adapter,
                ".fxjson",
                new FileFormatDescriptor(".fxjson", "Fake"),
                new TestProgress<OpenProgressUpdate>(progressUpdates.Add));

            result.Workbook.Name.Should().Be("Loaded");
            result.DisplayName.Should().Be(Path.GetFileNameWithoutExtension(tempPath));
            result.FeatureReport.Should().BeNull();
            result.OpenedAsTemplate.Should().BeFalse();
            recalculateCalled.Should().BeTrue();
            progressUpdates.Should().Contain(update => update.Detail.StartsWith("Loading file (reading)", StringComparison.Ordinal));
            progressUpdates.Should().Contain(update => update.Percent == 16);
            progressUpdates.Should().Contain(update => update.Percent == 98);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task LoadAsync_SkipsRecalculateStageWhenWorkbookHasNoFormulas()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.fxjson");
        await File.WriteAllTextAsync(tempPath, "payload");
        try
        {
            var adapter = new TestFileAdapter(_ =>
            {
                var workbook = new Workbook("Loaded");
                var sheet = workbook.AddSheet("Sheet1");
                sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("plain"));
                return workbook;
            });
            var recalculateCalled = false;
            var loader = new OpenWorkbookLoader(_ => recalculateCalled = true);

            await loader.LoadAsync(
                tempPath,
                adapter,
                ".fxjson",
                new FileFormatDescriptor(".fxjson", "Fake"),
                new TestProgress<OpenProgressUpdate>(_ => { }));

            recalculateCalled.Should().BeFalse();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void WorkbookFormulaScanner_UsesSheetFormulaCountsInsteadOfScanningCells()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "WorkbookFormulaScanner.cs"));

        source.Should().Contain("sheet.HasFormulas");
        source.Should().NotContain("EnumerateCells");
        source.Should().NotContain(".Any(");
    }
}
