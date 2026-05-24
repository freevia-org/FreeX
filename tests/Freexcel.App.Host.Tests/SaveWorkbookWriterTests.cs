using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;
using System.IO;

namespace Freexcel.App.Host.Tests;

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
            var adapter = new FakeAdapter((savedWorkbook, stream) =>
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
                new ImmediateProgress<SaveProgressUpdate>(progressUpdates.Add));

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
            var adapter = new FakeAdapter((_, _) => throw new InvalidOperationException("boom"));
            var saver = new SaveWorkbookWriter();

            var act = async () => await saver.SaveAsync(
                tempPath,
                adapter,
                workbook,
                new ImmediateProgress<SaveProgressUpdate>(_ => { }));

            await act.Should().ThrowAsync<InvalidOperationException>();
            (await File.ReadAllTextAsync(tempPath)).Should().Be("original");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private sealed class FakeAdapter(Action<Workbook, Stream> save) : IFileAdapter
    {
        public string Extension => ".fxjson";
        public string FormatName => "Fake";
        public Workbook Load(Stream stream) => throw new NotSupportedException();
        public void Save(Workbook workbook, Stream stream) => save(workbook, stream);
    }

    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
