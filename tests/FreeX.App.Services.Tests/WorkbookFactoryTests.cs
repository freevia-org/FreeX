using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookFactoryTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(0, 1)]
    [InlineData(300, 255)]
    public void Create_HonorsNormalizedDefaultSheetCount(
        int defaultSheetCount,
        int expectedSheetCount)
    {
        var workbook = WorkbookFactory.Create(new WorkbookCreationOptions(DefaultSheetCount: defaultSheetCount));

        workbook.SheetCount.Should().Be(expectedSheetCount);
        workbook.Sheets
            .Select(sheet => sheet.Name)
            .Should()
            .Equal(Enumerable.Range(1, expectedSheetCount).Select(index => $"Sheet{index}"));
    }

    [Theory]
    [InlineData(" Aptos ", 14, "Aptos", 14)]
    [InlineData("", 0, "Calibri", 11)]
    [InlineData("Arial", 500, "Arial", 409)]
    public void Create_HonorsNormalizedDefaultFontOptions(
        string defaultFontName,
        int defaultFontSize,
        string expectedFontName,
        int expectedFontSize)
    {
        var workbook = WorkbookFactory.Create(new WorkbookCreationOptions(
            DefaultSheetCount: 2,
            DefaultFontName: defaultFontName,
            DefaultFontSize: defaultFontSize));

        var defaultStyle = workbook.GetStyle(StyleId.Default);

        workbook.SheetCount.Should().Be(2);
        defaultStyle.FontName.Should().Be(expectedFontName);
        defaultStyle.FontSize.Should().Be(expectedFontSize);
    }

    [Fact]
    public void Create_WithUserName_AddsFileSharingMetadata()
    {
        var workbook = WorkbookFactory.Create(new WorkbookCreationOptions(
            DefaultSheetCount: 1,
            UserName: "  Analyst  "));

        workbook.FileSharing.Should().BeEquivalentTo(new WorkbookFileSharingModel
        {
            UserName = "Analyst",
        });
    }

    [Fact]
    public void Create_WithNoExplicitFont_DefaultStyleHasMinorFontScheme()
    {
        var workbook = WorkbookFactory.Create(new WorkbookCreationOptions(
            DefaultSheetCount: 1,
            DefaultFontName: ""));

        var defaultStyle = workbook.GetStyle(StyleId.Default);
        defaultStyle.FontScheme.Should().Be(CellFontScheme.Minor,
            "when no custom default font is specified the workbook body font should track the theme minor font");
    }

    [Fact]
    public void Create_WithExplicitFont_DefaultStyleHasNoneFontScheme()
    {
        var workbook = WorkbookFactory.Create(new WorkbookCreationOptions(
            DefaultSheetCount: 1,
            DefaultFontName: "Arial"));

        var defaultStyle = workbook.GetStyle(StyleId.Default);
        defaultStyle.FontScheme.Should().Be(CellFontScheme.None,
            "when a specific font is explicitly chosen as the default, the font scheme should be None (pinned)");
    }
}
