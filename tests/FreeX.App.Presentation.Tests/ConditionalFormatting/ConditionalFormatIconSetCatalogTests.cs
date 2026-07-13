using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class ConditionalFormatIconSetCatalogTests
{
    [Fact]
    public void GalleryGroups_ExposePortableIconSetKeysAndRuleOptions()
    {
        ConditionalFormatIconSetCatalog.GalleryGroups
            .Select(group => (group.CategoryKey, group.Options.Count))
            .Should()
            .Equal(
                ("ConditionalFormatIconSet_Category_Directional", 6),
                ("ConditionalFormatIconSet_Category_Shapes", 6),
                ("ConditionalFormatIconSet_Category_Indicators", 2),
                ("ConditionalFormatIconSet_Category_Ratings", 4));

        ConditionalFormatIconSetCatalog.GalleryOptions.Select(option => option.Style)
            .Should()
            .ContainInOrder("3Arrows", "3ArrowsGray", "4Arrows", "4ArrowsGray", "5Arrows", "5ArrowsGray");

        ConditionalFormatIconSetCatalog.GalleryOptions[0]
            .Should()
            .BeEquivalentTo(new ConditionalFormatIconSetOption(
                "3Arrows",
                3,
                "ConditionalFormatIconSet_3Arrows_Label",
                "ConditionalFormatIconSet_Category_Directional",
                "I3"));
    }

    [Fact]
    public void Styles_ExposeOnlyGalleryStyles()
    {
        var styles = ConditionalFormatIconSetCatalog.GalleryStyles;

        styles.Should().HaveCount(18);
        styles.Should().NotContain("3Stars");
        styles.Should().NotContain("3Triangles");
    }

    [Theory]
    [InlineData("3Arrows", new[] { "0", "33", "67" })]
    [InlineData("4Arrows", new[] { "0", "25", "50", "75" })]
    [InlineData("5Arrows", new[] { "0", "20", "40", "60", "80" })]
    public void CreateThresholds_UsesExcelStyleBaselineAndBandCutPoints(string style, string[] expectedValues)
    {
        var thresholds = ConditionalFormatIconSetCatalog.CreateThresholds(style);

        thresholds.Select(threshold => threshold.Type).Should().OnlyContain(type => type == CfThresholdType.Percent);
        thresholds.Select(threshold => threshold.Value).Should().Equal(expectedValues);
    }

    [Fact]
    public void CreateRule_AppliesIconSetStyleAndDefaultThresholds()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 1));

        var rule = ConditionalFormatIconSetCatalog.CreateRule("4Arrows", range);

        rule.Should().NotBeNull();
        rule!.RuleType.Should().Be(CfRuleType.IconSet);
        rule.AppliesTo.Should().Be(range);
        rule.IconSetStyle.Should().Be("4Arrows");
        rule.IconSetShowValue.Should().BeTrue();
        rule.IconSetReverse.Should().BeFalse();
        rule.IconSetThresholds.Select(threshold => threshold.Value).Should().Equal("0", "25", "50", "75");
    }
}
