using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookStartupSmokeServiceTests
{
    [Fact]
    public void Run_WithoutArguments_LoadsPreviewWorkbookSession()
    {
        var result = new WorkbookStartupSmokeService().Run([]);

        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.Message.Should().Contain("Packaging smoke opened");
        result.Message.Should().Contain("macOS Preview Workbook");
        result.Message.Should().Contain("Port Plan");
    }

    [Fact]
    public async Task Run_WithCsvPath_LoadsWorkbookThroughPortableOpenPath()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "Smoke.csv");
        await File.WriteAllTextAsync(path, "Name,Amount\r\nFreeX,42\r\n");

        var result = new WorkbookStartupSmokeService().Run([path]);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Smoke.csv");
        result.Message.Should().Contain("Smoke");
    }

    [Fact]
    public void Run_WithMissingPath_FailsInsteadOfPreviewFallback()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "Missing.csv");

        var result = new WorkbookStartupSmokeService().Run([path]);

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.Message.Should().Contain("file not found");
        result.Message.Should().Contain("Missing.csv");
    }

    [Fact]
    public async Task Run_WithUnsupportedPath_FailsInsteadOfPreviewFallback()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "Notes.unsupported");
        await File.WriteAllTextAsync(path, "not a workbook");

        var result = new WorkbookStartupSmokeService().Run([path]);

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.Message.Should().Contain("requested file was not opened");
        result.Message.Should().Contain("Notes.unsupported");
    }

    [Fact]
    public void PackagingSmokeCommand_WithFlag_WritesSuccessAndExitCode()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handled = PackagingSmokeCommand.TryRun(
            [PackagingSmokeCommand.Argument],
            output,
            error,
            out var exitCode);

        handled.Should().BeTrue();
        exitCode.Should().Be(0);
        output.ToString().Should().Contain("Packaging smoke opened");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public void PackagingSmokeCommand_WithBadPath_WritesFailureToError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handled = PackagingSmokeCommand.TryRun(
            [PackagingSmokeCommand.Argument, "Missing.csv"],
            output,
            error,
            out var exitCode);

        handled.Should().BeTrue();
        exitCode.Should().Be(1);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().Contain("file not found");
    }

    [Fact]
    public void PackagingSmokeCommand_WithoutFlag_ReturnsFalse()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handled = PackagingSmokeCommand.TryRun(["Book.csv"], output, error, out var exitCode);

        handled.Should().BeFalse();
        exitCode.Should().Be(0);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().BeEmpty();
    }
}
