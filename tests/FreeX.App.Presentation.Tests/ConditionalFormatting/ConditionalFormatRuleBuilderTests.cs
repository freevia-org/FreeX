using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class ConditionalFormatRuleBuilderTests
{
    [Fact]
    public void Build_CellValue_SetsOperatorAndHighlightStyle()
    {
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.Between,
            Value1 = "10",
            Value2 = "20"
        };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range());

        rule.RuleType.Should().Be(CfRuleType.CellValue);
        rule.Operator.Should().Be(CfOperator.Between);
        rule.Value1.Should().Be("10");
        rule.Value2.Should().Be("20");
        rule.FormatIfTrue.Should().NotBeNull();
        rule.FormatIfTrue!.FillColor.Should().Be(new CellColor(255, 199, 206));
    }

    [Fact]
    public void Build_IconSet_AppliesStyleAndDefaultThresholdsWithoutHighlightStyle()
    {
        var input = new CfRuleInput { RuleType = CfRuleType.IconSet, IconSetStyle = "4Arrows" };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range());

        rule.RuleType.Should().Be(CfRuleType.IconSet);
        rule.IconSetStyle.Should().Be("4Arrows");
        rule.IconSetThresholds.Should().HaveCount(4);
        rule.FormatIfTrue.Should().BeNull();
    }

    [Fact]
    public void Build_ColorScale_ParsesColorsAndThreeColorFlag()
    {
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinColor = "1,2,3",
            MidColor = "4,5,6",
            MaxColor = "7,8,9"
        };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range());

        rule.RuleType.Should().Be(CfRuleType.ColorScale);
        rule.UseThreeColorScale.Should().BeTrue();
        rule.MinColor.Should().Be(new RgbColor(1, 2, 3));
        rule.MidColor.Should().Be(new RgbColor(4, 5, 6));
        rule.MaxColor.Should().Be(new RgbColor(7, 8, 9));
        rule.FormatIfTrue.Should().BeNull();
    }

    [Fact]
    public void TryBuildApplyCommand_InvalidInput_ReportsValidationErrors()
    {
        var input = new CfRuleInput { RuleType = CfRuleType.CellValue, Value1 = "" };

        var result = ConditionalFormatRuleBuilder.TryBuildApplyCommand(input, SheetId(), Range());

        result.IsValid.Should().BeFalse();
        result.Command.Should().BeNull();
        result.Validation.Errors.Should().ContainSingle()
            .Which.Field.Should().Be(CfInputField.Value1);
    }

    [Fact]
    public void PresetFactory_BuildsQuickPresetApplyCommand()
    {
        var rule = ConditionalFormatPresetFactory.BuildRule(ConditionalFormatPreset.IconSet, Range());
        var command = ConditionalFormatPresetFactory.BuildApplyCommand(
            ConditionalFormatPreset.IconSet,
            SheetId(),
            Range());

        rule.RuleType.Should().Be(CfRuleType.IconSet);
        rule.IconSetStyle.Should().Be(ConditionalFormatIconSetCatalog.DefaultStyle);
        command.Should().BeOfType<ApplyConditionalFormatCommand>();
    }

    [Fact]
    public void PresetFactory_BelowAverageSetsDirection()
    {
        var rule = ConditionalFormatPresetFactory.BuildRule(ConditionalFormatPreset.BelowAverage, Range());

        rule.RuleType.Should().Be(CfRuleType.AboveAverage);
        rule.AboveAverage.Should().BeFalse();
    }

    private static SheetId SheetId() => new(Guid.NewGuid());

    private static GridRange Range() => RangeAt(SheetId(), 0, 0, 4, 0);

    private static GridRange RangeAt(SheetId sheet, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(sheet, r1, c1), new CellAddress(sheet, r2, c2));
}
