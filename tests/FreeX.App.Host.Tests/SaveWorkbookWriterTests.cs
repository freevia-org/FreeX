using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.Core.IO;
using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookSaveServiceTests
{
    [Fact]
    public void FormatSavingFileDetail_ChangesEveryThreeSeconds()
    {
        FormatSavingFileDetail("writing", TimeSpan.FromSeconds(0))
            .Should().Be("Saving file (writing)");
        FormatSavingFileDetail("writing", TimeSpan.FromSeconds(3))
            .Should().Be("Saving file (writing bytes)");
        FormatSavingFileDetail("writing", TimeSpan.FromSeconds(6))
            .Should().Be("Saving file (flushing package)");
    }

    [Fact]
    public void FormatSavingFileDetail_PreservesTrimmedCaseInsensitivePhaseMatching()
    {
        FormatSavingFileDetail(" Serializing ", TimeSpan.FromSeconds(6))
            .Should().Be("Saving file (packaging sheets)");
    }

    [Fact]
    public async Task SaveAsync_WritesWorkbookAndReportsProgress()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "saved.fxjson");
        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapter = new TestFileAdapter(save: (savedWorkbook, stream) =>
        {
            savedWorkbook.Should().BeSameAs(workbook);
            stream.Should().BeOfType<FileStream>();
            stream.CanRead.Should().BeTrue();
            stream.CanSeek.Should().BeTrue();
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write("saved payload");
        });
        var progressUpdates = new List<WorkbookSaveProgressUpdate>();
        var saver = new WorkbookSaveService();

        await saver.SaveAsync(
            tempPath,
            adapter,
            workbook,
            new TestProgress<WorkbookSaveProgressUpdate>(progressUpdates.Add));

        (await File.ReadAllTextAsync(tempPath)).Should().Be("saved payload");
        progressUpdates.Should().Contain(update => WorkbookProgressTextFormatter
            .FormatSave(update, UiText.Get).Detail.StartsWith("Saving file (serializing)", StringComparison.Ordinal));
        progressUpdates.Should().Contain(update => WorkbookProgressTextFormatter
            .FormatSave(update, UiText.Get).Detail.StartsWith("Saving file (writing)", StringComparison.Ordinal));
        progressUpdates.Should().Contain(update => update.Percent == 99);
        progressUpdates.Should().Contain(update => update.Percent == 100);
    }

    [Fact]
    public async Task SaveAsync_PreservesExistingFileWhenAdapterFails()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "saved.fxjson");
        await File.WriteAllTextAsync(tempPath, "original");
        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapter = new TestFileAdapter(save: (_, _) => throw new InvalidOperationException("boom"));
        var saver = new WorkbookSaveService();

        var act = async () => await saver.SaveAsync(
            tempPath,
            adapter,
            workbook,
            new TestProgress<WorkbookSaveProgressUpdate>(_ => { }));

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await File.ReadAllTextAsync(tempPath)).Should().Be("original");
    }

    [Fact]
    public async Task SaveAsync_CanceledBeforeSave_DoesNotInvokeAdapterOrCreateTarget()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "canceled-save.fxjson");
        var workbook = new Workbook("Canceled");
        workbook.AddSheet("Sheet1");
        var adapterInvoked = false;
        var adapter = new TestFileAdapter(save: (_, _) => adapterInvoked = true);
        var saver = new WorkbookSaveService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = async () => await saver.SaveAsync(
            tempPath,
            adapter,
            workbook,
            new TestProgress<WorkbookSaveProgressUpdate>(_ => { }),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        adapterInvoked.Should().BeFalse();
        File.Exists(tempPath).Should().BeFalse();
    }

    private static string FormatSavingFileDetail(string phase, TimeSpan elapsed) =>
        WorkbookProgressTextFormatter.FormatSave(phase, elapsed, percent: null, UiText.Get).Detail;
}
