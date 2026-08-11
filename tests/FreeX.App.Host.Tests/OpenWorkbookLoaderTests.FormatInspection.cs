using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using System.IO;
using System.IO.Compression;

namespace FreeX.App.Host.Tests;

public sealed partial class WorkbookOpenServiceTests
{
    [Theory]
    [InlineData(".xlt", "XLT 97-2003 Template")]
    [InlineData(".xltx", "XLTX Template")]
    public async Task LoadAsync_ReturnsTemplateMetadataFromSelectedFormat(string extension, string formatName)
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, $"template{extension}");
        await File.WriteAllTextAsync(tempPath, "payload");

        var adapter = new TestFileAdapter(_ =>
        {
            var workbook = new Workbook("Loaded");
            workbook.AddSheet("Sheet1");
            return workbook;
        });
        var loader = new WorkbookOpenService(
            _ => { },
            inspectXlsx: _ => new XlsxFeatureReport([]));

        var result = await loader.LoadAsync(
            tempPath,
            adapter,
            extension,
            new FileFormatDescriptor(extension, formatName, CanOpen: true, CanSave: false, OpensAsTemplate: true),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        result.OpenedAsTemplate.Should().BeTrue();
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
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, $"openxml{extension}");
        await File.WriteAllTextAsync(tempPath, "payload");

        var expectedReport = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Macros, "xl/vbaProject.bin")
        ]);
        var adapter = new TestFileAdapter(stream =>
        {
            using var reader = new StreamReader(stream);
            reader.ReadToEnd().Should().Be("payload");
            var workbook = new Workbook("Loaded");
            workbook.AddSheet("Sheet1");
            return workbook;
        });
        var inspected = false;
        var loader = new WorkbookOpenService(
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
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        inspected.Should().BeTrue();
        result.FeatureReport.Should().BeSameAs(expectedReport);
        result.OpenedAsTemplate.Should().Be(opensAsTemplate);
    }

    [Fact]
    public async Task LoadAsync_UsesXlsxAdapterFeatureReportFromLoadPass()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "feature-report.xlsx");
        var sourceWorkbook = new Workbook("Feature report");
        var sheet = sourceWorkbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("payload"));
        var adapter = new XlsxFileAdapter();
        await using (var stream = File.Create(tempPath))
            adapter.Save(sourceWorkbook, stream);

        using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Update))
        {
            await using var macroStream = archive.CreateEntry("xl/vbaProject.bin").Open();
            macroStream.WriteByte(1);
        }

        var loader = new WorkbookOpenService(_ => { });

        var result = await loader.LoadAsync(
            tempPath,
            adapter,
            ".xlsx",
            new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        result.FeatureReport.Should().NotBeNull();
        result.FeatureReport!.Features.Should().Contain(feature =>
            feature.Kind == XlsxUnsupportedFeatureKind.Macros &&
            feature.PackagePart == "xl/vbaProject.bin");
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
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, $"legacy{extension}");
        await File.WriteAllTextAsync(tempPath, "payload");

        var inspected = false;
        var adapter = new TestFileAdapter(stream =>
        {
            using var reader = new StreamReader(stream);
            reader.ReadToEnd().Should().Be("payload");
            var workbook = new Workbook("Loaded");
            workbook.AddSheet("Sheet1");
            return workbook;
        });
        var loader = new WorkbookOpenService(
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
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        inspected.Should().BeFalse();
        result.FeatureReport.Should().BeNull();
        result.OpenedAsTemplate.Should().Be(opensAsTemplate);
    }
}
