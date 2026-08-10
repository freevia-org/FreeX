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
        result.SourceFileAccessIdentity.Should().BeNull();
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
        result.SourceFileAccessIdentity.Should().NotBeNull();
        result.SourceFileAccessIdentity!.LocalPath.Should().Be(path);
        result.SourceFileAccessIdentity.HasBookmark.Should().BeFalse();
        result.DisplayName.Should().Be(Path.GetFileName(path));
        result.Workbook.Sheets.Single().Name.Should().Be("Very Long Sales _Draft_ Import");
        result.OpenedAsTemplate.Should().BeFalse();
        result.FeatureReport.Should().BeNull();
        result.LoadWarnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_WithLocalFileUri_OpensWorkbookThroughSharedAdapters()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "Open With.csv");
        await File.WriteAllTextAsync(path, "Name,Amount\r\nFreeX,42\r\n");

        var result = new StartupWorkbookLoader().Load([new Uri(path).AbsoluteUri]);

        result.IsFallback.Should().BeFalse();
        result.SourcePath.Should().Be(Path.GetFullPath(path));
        result.SourceFileAccessIdentity.Should().NotBeNull();
        result.SourceFileAccessIdentity!.LocalPath.Should().Be(Path.GetFullPath(path));
        result.DisplayName.Should().Be(Path.GetFileName(path));
        result.Workbook.Sheets.Single().Name.Should().Be("Open With");
    }

    [Fact]
    public async Task Load_WithUnsupportedPathBeforeSupportedWorkbook_OpensSupportedWorkbook()
    {
        using var temp = new TestTemporaryDirectory();
        var notesPath = Path.Combine(temp.Path, "launch-notes.unsupported");
        var workbookPath = Path.Combine(temp.Path, "OpenWith.csv");
        await File.WriteAllTextAsync(notesPath, "not a workbook");
        await File.WriteAllTextAsync(workbookPath, "Name,Amount\r\nFreeX,42\r\n");

        var result = new StartupWorkbookLoader().Load([notesPath, workbookPath]);

        result.IsFallback.Should().BeFalse();
        result.SourcePath.Should().Be(workbookPath);
        result.SourceFileAccessIdentity.Should().NotBeNull();
        result.SourceFileAccessIdentity!.LocalPath.Should().Be(workbookPath);
        result.DisplayName.Should().Be(Path.GetFileName(workbookPath));
        result.Workbook.Sheets.Single().Name.Should().Be("OpenWith");
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
        result.SourceFileAccessIdentity.Should().NotBeNull();
        result.SourceFileAccessIdentity!.LocalPath.Should().Be(path);
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
        result.SourceFileAccessIdentity.Should().BeNull();
        result.Status.Should().Contain("Unsupported file type: .unsupported");
        result.Workbook.Sheets.Single().Name.Should().Be("Port Plan");
    }

    [Fact]
    public async Task Load_WithOnlyUnsupportedExistingPaths_ReturnsFirstUnsupportedPreviewFallback()
    {
        using var temp = new TestTemporaryDirectory();
        var firstPath = Path.Combine(temp.Path, "notes.unsupported");
        var secondPath = Path.Combine(temp.Path, "other.unknown");
        await File.WriteAllTextAsync(firstPath, "not a workbook");
        await File.WriteAllTextAsync(secondPath, "also not a workbook");

        var result = new StartupWorkbookLoader().Load([firstPath, secondPath]);

        result.IsFallback.Should().BeTrue();
        result.SourceFileAccessIdentity.Should().BeNull();
        result.Status.Should().Contain("Unsupported file type: .unsupported");
        result.Workbook.Sheets.Single().Name.Should().Be("Port Plan");
    }

    // R133-avalonia-multi-file-startup-args: launching FreeX.Avalonia with more than one file
    // argument (or dragging multiple files onto the taskbar icon, which the OS delivers as a single
    // process launch with multiple path arguments) used to silently drop every argument after the
    // first -- Load() only ever returns ONE result. ResolveAdditionalOpenableFilePaths is the seam
    // App.cs uses to discover the rest so it can open each in its own window (mirrors the WPF host's
    // R118 PlanStartupFileOpens).
    [Fact]
    public async Task ResolveAdditionalOpenableFilePaths_WithMultipleExistingWorkbooks_ReturnsEveryPathAfterTheFirst()
    {
        using var temp = new TestTemporaryDirectory();
        var firstPath = Path.Combine(temp.Path, "First.csv");
        var secondPath = Path.Combine(temp.Path, "Second.csv");
        var thirdPath = Path.Combine(temp.Path, "Third.csv");
        await File.WriteAllTextAsync(firstPath, "Name,Amount\r\nFreeX,1\r\n");
        await File.WriteAllTextAsync(secondPath, "Name,Amount\r\nFreeX,2\r\n");
        await File.WriteAllTextAsync(thirdPath, "Name,Amount\r\nFreeX,3\r\n");

        var loader = new StartupWorkbookLoader();
        var primary = loader.Load([firstPath, secondPath, thirdPath]);
        var additional = loader.ResolveAdditionalOpenableFilePaths([firstPath, secondPath, thirdPath]);

        // Sibling no-regression: Load() itself must still only open the FIRST file into the primary
        // window/result -- the multi-window fan-out lives entirely in the additional-paths list, not
        // by widening what a single Load() call returns.
        primary.SourcePath.Should().Be(firstPath);
        additional.Should().Equal(secondPath, thirdPath);
    }

    [Fact]
    public async Task ResolveAdditionalOpenableFilePaths_WithSingleFileArgument_ReturnsEmpty()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "Solo.csv");
        await File.WriteAllTextAsync(path, "Name,Amount\r\nFreeX,42\r\n");

        var additional = new StartupWorkbookLoader().ResolveAdditionalOpenableFilePaths([path]);

        additional.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAdditionalOpenableFilePaths_SkipsMissingAndUnsupportedArguments()
    {
        using var temp = new TestTemporaryDirectory();
        var firstPath = Path.Combine(temp.Path, "First.csv");
        var missingPath = Path.Combine(temp.Path, "does-not-exist.csv");
        var unsupportedPath = Path.Combine(temp.Path, "notes.unsupported");
        var secondPath = Path.Combine(temp.Path, "Second.csv");
        await File.WriteAllTextAsync(firstPath, "Name,Amount\r\nFreeX,1\r\n");
        await File.WriteAllTextAsync(unsupportedPath, "not a workbook");
        await File.WriteAllTextAsync(secondPath, "Name,Amount\r\nFreeX,2\r\n");

        var additional = new StartupWorkbookLoader()
            .ResolveAdditionalOpenableFilePaths([firstPath, missingPath, unsupportedPath, secondPath]);

        additional.Should().Equal(secondPath);
    }
}
