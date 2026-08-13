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
            ColorScaleMinType = CfThresholdType.Number,
            ColorScaleMinValue = "1",
            MinColor = "1,2,3",
            ColorScaleMidType = CfThresholdType.Percentile,
            ColorScaleMidValue = "50",
            MidColor = "4,5,6",
            ColorScaleMaxType = CfThresholdType.Formula,
            ColorScaleMaxValue = "MAX(A:A)",
            MaxColor = "7,8,9"
        };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range());

        rule.RuleType.Should().Be(CfRuleType.ColorScale);
        rule.UseThreeColorScale.Should().BeTrue();
        rule.MinThresholdType.Should().Be(CfThresholdType.Number);
        rule.MinThresholdValue.Should().Be("1");
        rule.MinColor.Should().Be(new RgbColor(1, 2, 3));
        rule.MidThresholdType.Should().Be(CfThresholdType.Percentile);
        rule.MidThresholdValue.Should().Be("50");
        rule.MidColor.Should().Be(new RgbColor(4, 5, 6));
        rule.MaxThresholdType.Should().Be(CfThresholdType.Formula);
        rule.MaxThresholdValue.Should().Be("MAX(A:A)");
        rule.MaxColor.Should().Be(new RgbColor(7, 8, 9));
        rule.FormatIfTrue.Should().BeNull();
    }

    [Fact]
    public void Build_DataBar_AppliesAdvancedDialogOptions()
    {
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(10, 20, 30),
            DataBarMinType = CfThresholdType.Percentile,
            DataBarMinValue = "10",
            DataBarMaxType = CfThresholdType.Number,
            DataBarMaxValue = "99",
            DataBarShowValue = false,
            DataBarGradient = false,
            DataBarMinLength = "5",
            DataBarMaxLength = "95",
            DataBarBorder = true,
            DataBarAxisPosition = "middle",
            DataBarAxisColor = new RgbColor(1, 2, 3),
            DataBarNegativeFillColor = new RgbColor(4, 5, 6),
            DataBarNegativeBorderColor = new RgbColor(7, 8, 9)
        };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range());

        rule.RuleType.Should().Be(CfRuleType.DataBar);
        rule.DataBarColor.Should().Be(new RgbColor(10, 20, 30));
        rule.DataBarMinThresholdType.Should().Be(CfThresholdType.Percentile);
        rule.DataBarMinThresholdValue.Should().Be("10");
        rule.DataBarMaxThresholdType.Should().Be(CfThresholdType.Number);
        rule.DataBarMaxThresholdValue.Should().Be("99");
        rule.DataBarShowValue.Should().BeFalse();
        rule.DataBarGradient.Should().BeFalse();
        rule.DataBarMinLength.Should().Be(5);
        rule.DataBarMaxLength.Should().Be(95);
        rule.DataBarBorder.Should().BeTrue();
        rule.DataBarAxisPosition.Should().Be("middle");
        rule.DataBarAxisColor.Should().Be(new RgbColor(1, 2, 3));
        rule.DataBarNegativeFillColor.Should().Be(new RgbColor(4, 5, 6));
        rule.DataBarNegativeBorderColor.Should().Be(new RgbColor(7, 8, 9));
        rule.FormatIfTrue.Should().BeNull();
    }

    [Fact]
    public void Build_DataBar_DefaultsToAutomaticMinAndMaxThresholdTypes()
    {
        // A brand-new data bar built from an untouched CfRuleInput (as the Avalonia editor and the
        // quick-preset gallery both do — neither ever assigns DataBarMinType/DataBarMaxType) must match
        // Excel's own "Automatic" default rather than the explicit Lowest/Highest Value endpoint.
        var input = new CfRuleInput { RuleType = CfRuleType.DataBar };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range());

        rule.DataBarMinThresholdType.Should().Be(CfThresholdType.AutoMin);
        rule.DataBarMaxThresholdType.Should().Be(CfThresholdType.AutoMax);
    }

    [Fact]
    public void Build_IconSet_FillsDefaultOverrideEntriesWhenAnyOverrideIsSelected()
    {
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1",
            IconSetThresholds =
            [
                new CfThresholdModel(CfThresholdType.Percent, "0"),
                new CfThresholdModel(CfThresholdType.Percent, "33"),
                new CfThresholdModel(CfThresholdType.Percent, "67")
            ],
            IconOverrides =
            [
                null,
                new CfIconOverride("NoIcons", 0),
                null
            ]
        };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range());

        rule.IconOverrides.Should().Equal(
            new CfIconOverride("3TrafficLights1", 0),
            new CfIconOverride("NoIcons", 0),
            new CfIconOverride("3TrafficLights1", 2));
    }

    [Fact]
    public void Build_ExistingRuleTypeChange_ClearsNativeConditionalFormatMetadata()
    {
        var existing = new ConditionalFormat
        {
            RuleType = CfRuleType.DataBar,
            AppliesTo = Range(),
            NativeAttributes = new Dictionary<string, string> { ["type"] = "dataBar" },
            NativeChildXmls = ["<extLst />"],
            NativePayloadAttributes = new Dictionary<string, string> { ["future"] = "1" },
            NativePayloadChildXmls = ["<axisColor theme=\"1\" />"],
            NativeContainerAttributes = new Dictionary<string, string> { ["sqref"] = "A1:A5" },
            NativeContainerChildXmls = ["<extLst />"]
        };
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.ColorScale,
            MinColor = "1,2,3",
            MaxColor = "4,5,6"
        };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range(), existingRule: existing);

        rule.RuleType.Should().Be(CfRuleType.ColorScale);
        rule.NativeAttributes.Should().BeNull();
        rule.NativeChildXmls.Should().BeNull();
        rule.NativePayloadAttributes.Should().BeNull();
        rule.NativePayloadChildXmls.Should().BeNull();
        rule.NativeContainerAttributes.Should().BeNull();
        rule.NativeContainerChildXmls.Should().BeNull();
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
        var input = ConditionalFormatPresetFactory.BuildInput(ConditionalFormatPreset.BelowAverage);
        var rule = ConditionalFormatPresetFactory.BuildRule(ConditionalFormatPreset.BelowAverage, Range());

        input.IsTop.Should().BeFalse("the editor seed and one-click rule must share direction policy");
        rule.RuleType.Should().Be(CfRuleType.AboveAverage);
        rule.AboveAverage.Should().BeFalse();
    }

    private static SheetId SheetId() => new(Guid.NewGuid());

    private static GridRange Range() => RangeAt(SheetId(), 0, 0, 4, 0);

    private static GridRange RangeAt(SheetId sheet, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(sheet, r1, c1), new CellAddress(sheet, r2, c2));
}
