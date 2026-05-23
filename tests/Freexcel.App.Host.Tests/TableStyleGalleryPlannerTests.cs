using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class TableStyleGalleryPlannerTests
{
    [Fact]
    public void GetOptions_ExposesLightMediumAndDarkExcelStyleGallery()
    {
        var options = TableStyleGalleryPlanner.GetOptions();

        options.Select(option => option.StyleName)
            .Should()
            .ContainInOrder(
                "TableStyleLight1",
                "TableStyleLight21",
                "TableStyleMedium1",
                "TableStyleMedium28",
                "TableStyleDark1",
                "TableStyleDark11");
        options.Should().HaveCount(60);
        options.Select(option => option.StyleName).Should().OnlyHaveUniqueItems();
        options.Should().OnlyContain(option => option.Banding.HeaderFill != default);
    }

    [Fact]
    public void GetOptions_GroupsBuiltInStylesLikeExcelGallery()
    {
        var options = TableStyleGalleryPlanner.GetOptions();

        options.Take(21).Should().OnlyContain(option => option.Label.StartsWith("Light ", StringComparison.Ordinal));
        options.Skip(21).Take(28).Should().OnlyContain(option => option.Label.StartsWith("Medium ", StringComparison.Ordinal));
        options.Skip(49).Should().OnlyContain(option => option.Label.StartsWith("Dark ", StringComparison.Ordinal));
    }

    [Fact]
    public void MainWindow_PopulatesFormatAsTableMenuFromGalleryPlanner()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.HomeFormatting.cs"));

        xaml.Should().Contain("x:Name=\"FormatTableGalleryMenu\"");
        xaml.Should().NotContain("Header=\"Light 1\"  Tag=\"0\"");
        source.Should().Contain("PopulateFormatTableGalleryMenu()");
        source.Should().Contain("TableStyleGalleryPlanner.GetOptions()");
    }

    [Fact]
    public void GetOption_ClampsOutOfRangeIndexes()
    {
        TableStyleGalleryPlanner.GetOption(-10).StyleName.Should().Be("TableStyleLight1");
        TableStyleGalleryPlanner.GetOption(999).StyleName.Should().Be("TableStyleDark11");
    }
}
