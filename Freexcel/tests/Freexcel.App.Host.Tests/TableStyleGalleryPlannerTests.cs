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
                "TableStyleLight9",
                "TableStyleLight11",
                "TableStyleMedium2",
                "TableStyleMedium4",
                "TableStyleMedium7",
                "TableStyleDark1",
                "TableStyleDark4");
        options.Should().OnlyContain(option => option.Banding.HeaderFill != default);
    }

    [Fact]
    public void GetOption_ClampsOutOfRangeIndexes()
    {
        TableStyleGalleryPlanner.GetOption(-10).StyleName.Should().Be("TableStyleLight1");
        TableStyleGalleryPlanner.GetOption(999).StyleName.Should().Be("TableStyleDark4");
    }
}
