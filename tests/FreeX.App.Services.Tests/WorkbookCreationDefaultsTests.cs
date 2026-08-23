using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookCreationDefaultsTests
{
    [Theory]
    [InlineData(null, "Calibri")]
    [InlineData("  ", "Calibri")]
    [InlineData(" Aptos ", "Aptos")]
    public void FontNameNormalization_IsShared(string? value, string expected)
    {
        WorkbookCreationDefaults.NormalizeFontName(value).Should().Be(expected);
        AppOptions.NormalizeDefaultFontName(value).Should().Be(expected);
        WorkbookFactory.NormalizeDefaultFontName(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(-1, 11, 1)]
    [InlineData(0, 11, 1)]
    [InlineData(12, 12, 12)]
    [InlineData(500, 409, 255)]
    public void NumericDefaults_PreserveBounds(int value, int fontSize, int sheetCount)
    {
        WorkbookCreationDefaults.NormalizeFontSize(value).Should().Be(fontSize);
        WorkbookCreationDefaults.NormalizeSheetCount(value).Should().Be(sheetCount);
        AppOptions.NormalizeDefaultFontSize(value).Should().Be(fontSize);
        WorkbookFactory.NormalizeDefaultFontSize(value).Should().Be(fontSize);
    }

    [Fact]
    public void OptionsAndFactory_AdoptSharedOwner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        foreach (var file in new[] { "AppOptions.cs", "WorkbookFactory.cs" })
            File.ReadAllText(Path.Combine(root, "src", "FreeX.App.Services", file))
                .Should().Contain("WorkbookCreationDefaults.");
    }
}
