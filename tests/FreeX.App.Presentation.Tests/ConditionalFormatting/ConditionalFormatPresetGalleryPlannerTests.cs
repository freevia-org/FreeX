using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class ConditionalFormatPresetGalleryPlannerTests
{
    [Fact]
    public void DataBarGroups_ExposePortablePresetKeysAndRuleOptions()
    {
        ConditionalFormatPresetGalleryPlanner.DataBarGroups
            .Select(group => (group.CategoryKey, group.Options.Count))
            .Should()
            .Equal(
                ("ConditionalFormatDataBar_Category_GradientFill", 6),
                ("ConditionalFormatDataBar_Category_SolidFill", 6));

        ConditionalFormatPresetGalleryPlanner.DataBarOptions.Select(option => option.Style)
            .Should()
            .ContainInOrder("GradientBlue", "GradientGreen", "GradientRed", "GradientOrange", "GradientLightBlue", "GradientPurple");
    }

    [Fact]
    public void ColorScaleGroups_ExposePortablePresetKeysAndRuleOptions()
    {
        ConditionalFormatPresetGalleryPlanner.ColorScaleGroups
            .Select(group => (group.CategoryKey, group.Options.Count))
            .Should()
            .Equal(
                ("ConditionalFormatColorScale_Category_ThreeColor", 6),
                ("ConditionalFormatColorScale_Category_TwoColor", 4));

        ConditionalFormatPresetGalleryPlanner.ColorScaleOptions.Select(option => option.Style)
            .Should()
            .ContainInOrder("GreenYellowRed", "RedYellowGreen", "GreenWhiteRed", "RedWhiteGreen");
    }

    [Fact]
    public void PopupGroups_ExposeConditionalFormatPseudoCommandRowsWithSharedBacking()
    {
        var pseudoCommandRows = new[]
        {
            "Greater Than",
            "Less Than",
            "Between",
            "Equal To",
            "Text that Contains",
            "A Date Occurring",
            "Duplicate Values",
            "Top 10 Items",
            "Top 10%",
            "Bottom 10 Items",
            "Bottom 10%",
            "Above Average",
            "Below Average",
            "Data Bars",
            "Color Scales",
            "3 Arrows",
            "3 Arrows (Gray)",
            "4 Arrows",
            "4 Arrows (Gray)",
            "5 Arrows",
            "5 Arrows (Gray)",
            "3 Traffic Lights",
            "3 Traffic Lights (Rimmed)",
            "3 Signs",
            "3 Symbols",
            "3 Symbols (Uncircled)",
            "3 Flags",
            "4 Traffic Lights",
            "4 Red To Black",
            "4 Ratings",
            "5 Ratings",
            "5 Quarters",
            "5 Boxes",
            "More Rules",
        };

        var items = ConditionalFormatPresetGalleryPlanner.PopupItems;

        items.Select(item => item.CommandId)
            .Should()
            .ContainInOrder(pseudoCommandRows);

        items.Where(item => item.Kind == ConditionalFormatPopupCatalogItemKind.Preset)
            .Should()
            .OnlyContain(item => item.Preset != null);
        items.Where(item => item.Kind == ConditionalFormatPopupCatalogItemKind.IconSetGallery)
            .Should()
            .OnlyContain(item => item.IconSetStyle != null);
        items.Single(item => item.CommandId == "Data Bars").Kind.Should().Be(ConditionalFormatPopupCatalogItemKind.DataBarGallery);
        items.Single(item => item.CommandId == "Color Scales").Kind.Should().Be(ConditionalFormatPopupCatalogItemKind.ColorScaleGallery);
        items.Single(item => item.CommandId == "More Rules").Kind.Should().Be(ConditionalFormatPopupCatalogItemKind.RuleDialog);
    }

    [Fact]
    public void PopupIconSetRows_MapToSharedIconSetCatalogStyles()
    {
        var iconSetItems = ConditionalFormatPresetGalleryPlanner.PopupItems
            .Where(item => item.Kind == ConditionalFormatPopupCatalogItemKind.IconSetGallery)
            .ToArray();

        iconSetItems.Select(item => item.IconSetStyle)
            .Should()
            .Equal(ConditionalFormatIconSetCatalog.GalleryStyles);
    }

    [Fact]
    public void CreateDataBarRule_AppliesPresetColorAndFillMode()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 1));

        var gradient = ConditionalFormatPresetGalleryPlanner.CreateDataBarRule("GradientGreen", range);
        var solid = ConditionalFormatPresetGalleryPlanner.CreateDataBarRule("SolidGreen", range);

        gradient.Should().NotBeNull();
        gradient!.RuleType.Should().Be(CfRuleType.DataBar);
        gradient.AppliesTo.Should().Be(range);
        gradient.DataBarColor.Should().Be(new RgbColor(99, 190, 123));
        gradient.DataBarGradient.Should().BeTrue();

        solid.Should().NotBeNull();
        solid!.DataBarColor.Should().Be(new RgbColor(99, 190, 123));
        solid.DataBarGradient.Should().BeFalse();
    }

    [Fact]
    public void CreateColorScaleRule_MapsTwoAndThreeColorPresets()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 1));

        var threeColor = ConditionalFormatPresetGalleryPlanner.CreateColorScaleRule("GreenYellowRed", range);
        var twoColor = ConditionalFormatPresetGalleryPlanner.CreateColorScaleRule("WhiteRed", range);

        threeColor.Should().NotBeNull();
        threeColor!.RuleType.Should().Be(CfRuleType.ColorScale);
        threeColor.UseThreeColorScale.Should().BeTrue();
        threeColor.MinColor.Should().Be(new RgbColor(99, 190, 123));
        threeColor.MidColor.Should().Be(new RgbColor(255, 235, 132));
        threeColor.MaxColor.Should().Be(new RgbColor(248, 105, 107));

        twoColor.Should().NotBeNull();
        twoColor!.UseThreeColorScale.Should().BeFalse();
        twoColor.MinColor.Should().Be(new RgbColor(255, 255, 255));
        twoColor.MaxColor.Should().Be(new RgbColor(248, 105, 107));
    }
}
