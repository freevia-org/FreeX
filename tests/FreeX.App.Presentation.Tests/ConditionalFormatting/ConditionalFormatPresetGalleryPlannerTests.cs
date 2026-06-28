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
