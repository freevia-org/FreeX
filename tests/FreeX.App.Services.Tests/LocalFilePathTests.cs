using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class LocalFilePathTests
{
    [Fact]
    public void TryNormalize_ReturnsFullPathForRelativeLocalPath()
    {
        LocalFilePath.TryNormalize("  Budget.xlsx  ", out var normalized).Should().BeTrue();

        normalized.Should().Be(Path.GetFullPath("Budget.xlsx"));
    }

    [Fact]
    public void TryNormalize_PreservesUnixAbsolutePath()
    {
        LocalFilePath.TryNormalize("  /Users/quinn/Work/Budget.xlsx  ", out var normalized).Should().BeTrue();

        normalized.Should().Be("/Users/quinn/Work/Budget.xlsx");
    }

    [Fact]
    public void TryNormalize_ConvertsMacOsFileUriToUnixAbsolutePath()
    {
        LocalFilePath.TryNormalize("file:///Users/quinn/Work/Budget%202026.xlsx", out var normalized)
            .Should()
            .BeTrue();

        normalized.Should().Be("/Users/quinn/Work/Budget 2026.xlsx");
    }

    [Theory]
    [InlineData("C:\\Temp\\Budget.xlsx")]
    [InlineData("C:/Temp/Budget.xlsx")]
    public void TryNormalize_RejectsWindowsDriveRootedPathOutsideWindows(string candidate)
    {
        LocalFilePath.TryNormalize(candidate, isWindows: false, out var normalized).Should().BeFalse();

        normalized.Should().BeEmpty();
    }

    [Fact]
    public void TryNormalize_RejectsWindowsDriveFileUriOutsideWindows()
    {
        LocalFilePath.TryNormalize("file:///C:/Temp/Budget%202026.xlsx", isWindows: false, out var normalized)
            .Should()
            .BeFalse();

        normalized.Should().BeEmpty();
    }

    [Fact]
    public void TryNormalize_RejectsNonFileUri()
    {
        LocalFilePath.TryNormalize("https://example.test/Budget.xlsx", out var normalized).Should().BeFalse();

        normalized.Should().BeEmpty();
    }

    [Fact]
    public void TryNormalize_RejectsNullCharacter()
    {
        LocalFilePath.TryNormalize("/Users/quinn/Bad\0Budget.xlsx", out var normalized).Should().BeFalse();

        normalized.Should().BeEmpty();
    }
}
