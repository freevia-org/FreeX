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
}
