using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class StartupWorkbookLoaderTests
{
    [Fact]
    public void Load_WithoutExistingPath_ReturnsPreviewWorkbook()
    {
        var result = new StartupWorkbookLoader().Load(["missing.xlsx"]);

        result.IsFallback.Should().BeFalse();
        result.DisplayName.Should().Be("macOS Preview Workbook");
        result.Workbook.Sheets.Single().Name.Should().Be("Port Plan");
    }

    [Fact]
    public async Task Load_WithCsvPath_OpensWorkbookThroughSharedAdapters()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "Very Long Sales [Draft] Import Name 2026.csv");
        await File.WriteAllTextAsync(path, "Name,Amount\r\nFreeX,42\r\n");

        var result = new StartupWorkbookLoader().Load([path]);

        result.IsFallback.Should().BeFalse();
        result.SourcePath.Should().Be(path);
        result.DisplayName.Should().Be(Path.GetFileName(path));
        result.Workbook.Sheets.Single().Name.Should().Be("Very Long Sales _Draft_ Import");
        result.OpenedAsTemplate.Should().BeFalse();
        result.FeatureReport.Should().BeNull();
        result.LoadWarnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_WithTemplatePath_PreservesTemplateAndFeatureMetadata()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "Budget.xltx");
        await File.WriteAllTextAsync(path, "template");
        var workbook = new Workbook("Template");
        workbook.AddSheet("Sheet1");
        var featureReport = new XlsxFeatureReport(
        [
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Charts, "xl/charts/chart1.xml")
        ]);
        var adapter = new TestFileAdapter(
            load: _ => workbook,
            extension: ".xltx",
            formatName: "XLTX Template",
            formats:
            [
                new FileFormatDescriptor(
                    ".xltx",
                    "XLTX Template",
                    CanOpen: true,
                    CanSave: false,
                    OpensAsTemplate: true)
            ]);
        var service = new WorkbookOpenService(inspectXlsx: _ => featureReport);

        var result = new StartupWorkbookLoader([adapter], openService: service).Load([path]);

        result.IsFallback.Should().BeFalse();
        result.SourcePath.Should().Be(path);
        result.OpenedAsTemplate.Should().BeTrue();
        result.FeatureReport.Should().BeSameAs(featureReport);
        result.LoadWarnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_WithUnsupportedPath_ReturnsPreviewFallback()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "notes.unsupported");
        await File.WriteAllTextAsync(path, "not a workbook");

        var result = new StartupWorkbookLoader().Load([path]);

        result.IsFallback.Should().BeTrue();
        result.Status.Should().Contain("Unsupported file type: .unsupported");
        result.Workbook.Sheets.Single().Name.Should().Be("Port Plan");
    }
}
