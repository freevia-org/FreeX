using System.IO;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class NewWorkbookFactoryTests
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
        var workbook = NewWorkbookFactory.Create(defaultSheetCount);

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
        var workbook = NewWorkbookFactory.Create(new FreeXOptions
        {
            DefaultFontName = defaultFontName,
            DefaultFontSize = defaultFontSize,
            DefaultSheetCount = 2
        });

        var defaultStyle = workbook.GetStyle(StyleId.Default);

        workbook.SheetCount.Should().Be(2);
        defaultStyle.FontName.Should().Be(expectedFontName);
        defaultStyle.FontSize.Should().Be(expectedFontSize);
    }

    [Fact]
    public void Create_FromOptions_HonorsNormalizedUserNameMetadata()
    {
        var workbook = NewWorkbookFactory.Create(new FreeXOptions
        {
            UserName = "  Analyst  ",
            DefaultSheetCount = 1
        });

        workbook.FileSharing.Should().BeEquivalentTo(new WorkbookFileSharingModel
        {
            UserName = "Analyst"
        });
    }

    [Fact]
    public void AppAndFileNew_RouteFullOptionsIntoFactory()
    {
        var appSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "App.xaml.cs"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));

        appSource.Should().Contain("NewWorkbookFactory.Create(options)");
        backstageSource.Should().Contain("NewWorkbookFactory.Create(_options)");
        appSource.Should().NotContain("NewWorkbookFactory.Create(options.DefaultSheetCount)");
        backstageSource.Should().NotContain("NewWorkbookFactory.Create(_options.DefaultSheetCount)");
    }
}
