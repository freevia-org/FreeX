using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed class SaveWorkbookWriterTests
{
    [Fact]
    public async Task SaveAsync_WritesWorkbookAndReportsProgress()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.fxjson");
        try
        {
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
            var progressUpdates = new List<SaveProgressUpdate>();
            var saver = new SaveWorkbookWriter();

            await saver.SaveAsync(
                tempPath,
                adapter,
                workbook,
                new TestProgress<SaveProgressUpdate>(progressUpdates.Add));

            (await File.ReadAllTextAsync(tempPath)).Should().Be("saved payload");
            progressUpdates.Should().Contain(update => update.Detail.StartsWith("Saving file (serializing)", StringComparison.Ordinal));
            progressUpdates.Should().Contain(update => update.Detail.StartsWith("Saving file (writing)", StringComparison.Ordinal));
            progressUpdates.Should().Contain(update => update.Percent == 99);
            progressUpdates.Should().Contain(update => update.Percent == 100);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task SaveAsync_PreservesExistingFileWhenAdapterFails()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.fxjson");
        await File.WriteAllTextAsync(tempPath, "original");
        try
        {
            var workbook = new Workbook("Saved");
            workbook.AddSheet("Sheet1");
            var adapter = new TestFileAdapter(save: (_, _) => throw new InvalidOperationException("boom"));
            var saver = new SaveWorkbookWriter();

            var act = async () => await saver.SaveAsync(
                tempPath,
                adapter,
                workbook,
                new TestProgress<SaveProgressUpdate>(_ => { }));

            await act.Should().ThrowAsync<InvalidOperationException>();
            (await File.ReadAllTextAsync(tempPath)).Should().Be("original");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
