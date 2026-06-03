using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class OpenWorkbookLoaderTests
{
    [Theory]
    [InlineData(".xlt", "XLT 97-2003 Template")]
    [InlineData(".xltx", "XLTX Template")]
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
    [InlineData(".XLSX", false, true)]
    [InlineData(".xlsm", false, false)]
    [InlineData(".XLSM", false, false)]
    [InlineData(".xltx", true, false)]
    [InlineData(".XLTX", true, false)]
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
                new FileFormatDescriptor(extension, "XLSB Binary", CanOpen: true, CanSave: false, opensAsTemplate),
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
}
