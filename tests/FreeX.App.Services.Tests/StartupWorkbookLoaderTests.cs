using FluentAssertions;

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
