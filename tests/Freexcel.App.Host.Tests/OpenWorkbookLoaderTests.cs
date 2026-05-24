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
                new ImmediateProgress<OpenProgressUpdate>(progressUpdates.Add));

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
            var adapter = new FakeAdapter(_ =>
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
                new ImmediateProgress<OpenProgressUpdate>(_ => { }));

            recalculateCalled.Should().BeFalse();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Theory]
    [InlineData(".xlt", "Excel 97-2003 Template")]
    [InlineData(".xltx", "Excel Template")]
    public async Task LoadAsync_ReturnsTemplateMetadataFromSelectedFormat(string extension, string formatName)
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
            var loader = new OpenWorkbookLoader(
                _ => { },
                inspectXlsx: _ => new XlsxFeatureReport([]));

            var result = await loader.LoadAsync(
                tempPath,
                adapter,
                extension,
                new FileFormatDescriptor(extension, formatName, CanOpen: true, CanSave: false, OpensAsTemplate: true),
                new ImmediateProgress<OpenProgressUpdate>(_ => { }));

            result.OpenedAsTemplate.Should().BeTrue();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Theory]
    [InlineData(".xlsx", false, true)]
    [InlineData(".xlsm", false, false)]
    [InlineData(".XLSM", false, false)]
    [InlineData(".xltx", true, false)]
    [InlineData(".xltm", true, false)]
    [InlineData(".XLTM", true, false)]
    public async Task LoadAsync_InspectsOpenXmlExcelVariants(string extension, bool opensAsTemplate, bool canSave)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        await File.WriteAllTextAsync(tempPath, "payload");
        try
        {
            var expectedReport = new XlsxFeatureReport([
                new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Macros, "xl/vbaProject.bin")
            ]);
            var adapter = new FakeAdapter(stream =>
            {
                using var reader = new StreamReader(stream);
                reader.ReadToEnd().Should().Be("payload");
                var workbook = new Workbook("Loaded");
                workbook.AddSheet("Sheet1");
                return workbook;
            });
            var inspected = false;
            var loader = new OpenWorkbookLoader(
                _ => { },
                inspectXlsx: stream =>
                {
                    using var reader = new StreamReader(stream);
                    reader.ReadToEnd().Should().Be("payload");
                    inspected = true;
                    return expectedReport;
                });

            var result = await loader.LoadAsync(
                tempPath,
                adapter,
                extension,
                new FileFormatDescriptor(extension, "Excel Open XML", CanOpen: true, CanSave: canSave, opensAsTemplate),
                new ImmediateProgress<OpenProgressUpdate>(_ => { }));

            inspected.Should().BeTrue();
            result.FeatureReport.Should().BeSameAs(expectedReport);
            result.OpenedAsTemplate.Should().Be(opensAsTemplate);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Theory]
    [InlineData(".xls", false)]
    [InlineData(".XLS", false)]
    [InlineData(".xlsb", false)]
    [InlineData(".XLSB", false)]
    [InlineData(".xlt", true)]
    [InlineData(".XLT", true)]
    public async Task LoadAsync_DoesNotInspectLegacyBinaryExcelVariants(string extension, bool opensAsTemplate)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        await File.WriteAllTextAsync(tempPath, "payload");
        try
        {
            var inspected = false;
            var adapter = new FakeAdapter(stream =>
            {
                using var reader = new StreamReader(stream);
                reader.ReadToEnd().Should().Be("payload");
                var workbook = new Workbook("Loaded");
                workbook.AddSheet("Sheet1");
                return workbook;
            });
            var loader = new OpenWorkbookLoader(
                _ => { },
                inspectXlsx: _ =>
                {
                    inspected = true;
                    return new XlsxFeatureReport([]);
                });

            var result = await loader.LoadAsync(
                tempPath,
                adapter,
                extension,
                new FileFormatDescriptor(extension, "Excel Binary", CanOpen: true, CanSave: false, opensAsTemplate),
                new ImmediateProgress<OpenProgressUpdate>(_ => { }));

            inspected.Should().BeFalse();
            result.FeatureReport.Should().BeNull();
            result.OpenedAsTemplate.Should().Be(opensAsTemplate);
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
                new ImmediateProgress<OpenProgressUpdate>(_ => { }));

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

    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
