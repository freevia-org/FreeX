using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class ConditionalFormatPresetGalleryPlannerTests
{
    [Fact]
    public void DataBarGroups_ExposeExcelStyleGradientAndSolidFillPresets()
    {
        ConditionalFormatPresetGalleryPlanner.DataBarGroups
            .Select(group => (group.Name, group.Options.Count))
            .Should()
            .Equal(("Gradient Fill", 6), ("Solid Fill", 6));

        ConditionalFormatPresetGalleryPlanner.DataBarOptions.Select(option => option.Style)
            .Should()
            .ContainInOrder("GradientBlue", "GradientGreen", "GradientRed", "GradientOrange", "GradientLightBlue", "GradientPurple");
    }

    [Fact]
    public void ColorScaleGroups_ExposeTwoAndThreeColorPresets()
    {
        ConditionalFormatPresetGalleryPlanner.ColorScaleGroups
            .Select(group => (group.Name, group.Options.Count))
            .Should()
            .Equal(("3-Color Scale", 6), ("2-Color Scale", 4));

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
