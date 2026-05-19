using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;
using System.IO;

namespace Freexcel.App.Host.Tests;

public sealed class OpenWorkbookLoaderTests
{
    [Fact]
    public async Task LoadAsync_ReadsLoadsRecalculatesAndReportsProgress()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.fxjson");
        await File.WriteAllTextAsync(tempPath, "payload");
        try
        {
            var recalculateCalled = false;
            var adapter = new FakeAdapter(stream =>
            {
                using var reader = new StreamReader(stream);
                reader.ReadToEnd().Should().Be("payload");
                var workbook = new Workbook("Loaded");
                workbook.AddSheet("Sheet1");
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
                new Progress<OpenProgressUpdate>(progressUpdates.Add));

            result.Workbook.Name.Should().Be("Loaded");
            result.DisplayName.Should().Be(Path.GetFileNameWithoutExtension(tempPath));
            result.FeatureReport.Should().BeNull();
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

    private sealed class FakeAdapter(Func<Stream, Workbook> load) : IFileAdapter
    {
        public string Extension => ".fxjson";
        public string FormatName => "Fake";
        public Workbook Load(Stream stream) => load(stream);
        public void Save(Workbook workbook, Stream stream) => throw new NotSupportedException();
    }
}
