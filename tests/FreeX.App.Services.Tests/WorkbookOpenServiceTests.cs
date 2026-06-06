using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookOpenServiceTests
{
    [Fact]
    public async Task LoadAsync_LoadsRecalculatesAndReportsPortableProgress()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "formula-load.fxjson");
        await File.WriteAllTextAsync(tempPath, "payload");

        var adapter = new TestFileAdapter(stream =>
        {
            using var reader = new StreamReader(stream);
            reader.ReadToEnd().Should().Be("payload");
            var workbook = new Workbook("Loaded");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromFormula("1+1"));
            return workbook;
        });
        var progressUpdates = new List<WorkbookOpenProgressUpdate>();
        var recalculateCalled = false;
        var service = new WorkbookOpenService(workbook =>
        {
            workbook.Name.Should().Be("Loaded");
            recalculateCalled = true;
        });

        var result = await service.LoadAsync(
            tempPath,
            adapter,
            ".fxjson",
            new FileFormatDescriptor(".fxjson", "Fake"),
            new TestProgress<WorkbookOpenProgressUpdate>(progressUpdates.Add));

        result.Workbook.Name.Should().Be("Loaded");
        result.DisplayName.Should().Be("formula-load");
        result.FeatureReport.Should().BeNull();
        result.OpenedAsTemplate.Should().BeFalse();
        result.LoadWarnings.Should().BeEmpty();
        recalculateCalled.Should().BeTrue();
        progressUpdates.Should().Contain(update =>
            update.Phase == WorkbookOpenPhase.Reading &&
            update.Percent == 8);
        progressUpdates.Should().Contain(update =>
            update.Phase == WorkbookOpenPhase.Parsing &&
            update.Percent == 16);
        progressUpdates.Should().Contain(update =>
            update.Phase == WorkbookOpenPhase.Calculating &&
            update.Percent == 98);
    }

    [Fact]
    public async Task LoadAsync_RejectsOversizedFilesBeforeLoading()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "oversized.fxjson");
        await File.WriteAllTextAsync(tempPath, "payload-that-is-too-large");
        var adapterInvoked = false;
        var adapter = new TestFileAdapter(_ =>
        {
            adapterInvoked = true;
            return new Workbook("Loaded");
        });
        var service = new WorkbookOpenService(maxFileBytes: 4);

        var act = async () => await service.LoadAsync(
            tempPath,
            adapter,
            ".fxjson",
            new FileFormatDescriptor(".fxjson", "Fake"));

        await act.Should().ThrowAsync<WorkbookTooLargeException>();
        adapterInvoked.Should().BeFalse();
    }

    [Fact]
    public void WorkbookFormulaScanner_UsesSheetFormulaCountsInsteadOfScanningCells()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Services", "WorkbookFormulaScanner.cs"));

        source.Should().Contain("sheet.HasFormulas");
        source.Should().NotContain("EnumerateCells");
        source.Should().NotContain(".Any(");
    }
}
