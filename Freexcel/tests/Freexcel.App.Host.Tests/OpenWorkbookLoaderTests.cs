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
                new FileFormatDescriptor(".fxjson", "Fake"),
                new Progress<OpenProgressUpdate>(progressUpdates.Add));

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
    public async Task LoadAsync_ReturnsTemplateMetadataFromSelectedFormat()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xltx");
        await File.WriteAllTextAsync(tempPath, "payload");
        try
        {
            var adapter = new FakeAdapter(_ =>
            {
                var workbook = new Workbook("Loaded");
                workbook.AddSheet("Sheet1");
                return workbook;
            });
            var loader = new OpenWorkbookLoader(_ => { }, _ => new XlsxFeatureReport([]));

            var result = await loader.LoadAsync(
                tempPath,
                adapter,
                ".xltx",
                new FileFormatDescriptor(".xltx", "Excel Template", CanOpen: true, CanSave: false, OpensAsTemplate: true),
                new Progress<OpenProgressUpdate>());

            result.OpenedAsTemplate.Should().BeTrue();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".xlsm")]
    [InlineData(".xltx")]
    [InlineData(".xltm")]
    public async Task LoadAsync_InspectsOpenXmlExcelPackageFormats(string extension)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        await File.WriteAllTextAsync(tempPath, "payload");
        try
        {
            var adapter = new FakeAdapter(_ =>
            {
                var workbook = new Workbook("Loaded");
                workbook.AddSheet("Sheet1");
                return workbook;
            });
            var inspectCalled = false;
            var expectedReport = new XlsxFeatureReport([
                new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Macros, "xl/vbaProject.bin")
            ]);
            var loader = new OpenWorkbookLoader(
                _ => { },
                stream =>
                {
                    inspectCalled = true;
                    using var reader = new StreamReader(stream);
                    reader.ReadToEnd().Should().Be("payload");
                    return expectedReport;
                });

            var result = await loader.LoadAsync(
                tempPath,
                adapter,
                extension,
                new FileFormatDescriptor(extension, "Excel Package", CanOpen: true, CanSave: extension == ".xlsx"),
                new Progress<OpenProgressUpdate>());

            inspectCalled.Should().BeTrue();
            result.FeatureReport.Should().BeSameAs(expectedReport);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Theory]
    [InlineData(".csv")]
    [InlineData(".txt")]
    [InlineData(".tsv")]
    [InlineData(".tab")]
    public async Task LoadAsync_RenamesSingleSheetTextWorkbooksToExcelCompatibleFileName(string extension)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var fileNameWithoutExtension = "Very Long Sales [Draft] Import Name 2026";
        var tempPath = Path.Combine(tempDirectory, $"{fileNameWithoutExtension}{extension}");
        await File.WriteAllTextAsync(tempPath, "payload");
        try
        {
            var adapter = new FakeAdapter(_ =>
            {
                var workbook = new Workbook("Loaded");
                workbook.AddSheet("Sheet1");
                return workbook;
            });
            var loader = new OpenWorkbookLoader(_ => { });

            var result = await loader.LoadAsync(
                tempPath,
                adapter,
                extension,
                new FileFormatDescriptor(extension, "Text", CanOpen: true, CanSave: false),
                new Progress<OpenProgressUpdate>());

            result.Workbook.Sheets.Single().Name.Should().Be("Very Long Sales _Draft_ Import");
        }
        finally
        {
            File.Delete(tempPath);
            Directory.Delete(tempDirectory);
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
