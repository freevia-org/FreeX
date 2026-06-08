using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSaveServiceTests
{
    [Fact]
    public async Task SaveAsync_WritesWorkbookThroughTemporaryFileAndReportsPortableProgress()
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

        await new WorkbookSaveService().SaveAsync(
            tempPath,
            adapter,
            workbook,
            new TestProgress<WorkbookSaveProgressUpdate>(progressUpdates.Add));

        (await File.ReadAllTextAsync(tempPath)).Should().Be("saved payload");
        progressUpdates.Should().Contain(update =>
            update.Phase == WorkbookSavePhase.Preparing &&
            update.Percent == 1);
        progressUpdates.Should().Contain(update =>
            update.Phase == WorkbookSavePhase.Writing &&
            update.Percent == 99);
        progressUpdates.Should().Contain(update =>
            update.Phase == WorkbookSavePhase.Completed &&
            update.Percent == 100);
        Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_ReplacesExistingFileThroughTemporaryFile()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "saved.fxjson");
        await File.WriteAllTextAsync(tempPath, "original");
        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapter = new TestFileAdapter(save: (_, stream) =>
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write("replacement");
        });

        await new WorkbookSaveService().SaveAsync(tempPath, adapter, workbook);

        (await File.ReadAllTextAsync(tempPath)).Should().Be("replacement");
        Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_PreservesExistingFileAndDeletesTemporaryFileWhenAdapterFails()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "saved.fxjson");
        await File.WriteAllTextAsync(tempPath, "original");
        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapter = new TestFileAdapter(save: (_, _) => throw new InvalidOperationException("boom"));

        var act = async () => await new WorkbookSaveService().SaveAsync(tempPath, adapter, workbook);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await File.ReadAllTextAsync(tempPath)).Should().Be("original");
        Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
    }
}
