using FluentAssertions;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Dialogs;

/// <summary>
/// Covers the Data Bar / Color Scale threshold-VALUE validation gap (R29-dialogs-validation-logic-1):
/// a Number/Percent/Percentile threshold with garbage (or blank) text used to pass
/// <see cref="ConditionalFormatRuleSchema.Validate"/> unnoticed, silently producing no bar/scale at
/// render time. These tests pin the bug case, a representative valid-numeric sibling, and the
/// already-working Min/Max automatic-threshold case that must remain unaffected.
/// </summary>
public sealed class ConditionalFormatRuleSchemaThresholdValueTests
{
    [Theory]
    [InlineData(CfThresholdType.Number)]
    [InlineData(CfThresholdType.Percent)]
    [InlineData(CfThresholdType.Percentile)]
    public void DataBar_MinValue_Invalid_WhenNotANumber(CfThresholdType type)
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.DataBar);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.DataBar,
            DataBarMinType = type,
            DataBarMinValue = "abc"
        };

        var result = schema.Validate(input);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == CfInputField.DataBarMinValue);
    }

    [Theory]
    [InlineData(CfThresholdType.Number)]
    [InlineData(CfThresholdType.Percent)]
    [InlineData(CfThresholdType.Percentile)]
    public void DataBar_MinValue_Invalid_WhenBlank(CfThresholdType type)
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.DataBar);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.DataBar,
            DataBarMinType = type,
            DataBarMinValue = "  "
        };

        var result = schema.Validate(input);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == CfInputField.DataBarMinValue);
    }

    [Theory]
    [InlineData(CfThresholdType.Number, "10")]
    [InlineData(CfThresholdType.Percent, "50")]
    [InlineData(CfThresholdType.Percentile, "25")]
    public void DataBar_MinAndMaxValue_Valid_WhenNumeric(CfThresholdType type, string value)
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.DataBar);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.DataBar,
            DataBarMinType = type,
            DataBarMinValue = value,
            DataBarMaxType = type,
            DataBarMaxValue = value
        };

        schema.Validate(input).IsValid.Should().BeTrue();
    }

    [Fact]
    public void DataBar_MaxValue_Invalid_WhenNotANumber()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.DataBar);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.DataBar,
            DataBarMaxType = CfThresholdType.Number,
            DataBarMaxValue = "not-a-number"
        };

        var result = schema.Validate(input);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == CfInputField.DataBarMaxValue);
    }

    [Theory]
    [InlineData(CfThresholdType.Min)]
    [InlineData(CfThresholdType.Max)]
    public void DataBar_MinMaxThresholdType_IgnoresValue_EvenWhenGarbageOrBlank(CfThresholdType type)
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.DataBar);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.DataBar,
            DataBarMinType = type,
            DataBarMinValue = "abc",
            DataBarMaxType = type,
            DataBarMaxValue = null
        };

        schema.Validate(input).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(CfThresholdType.AutoMin)]
    [InlineData(CfThresholdType.AutoMax)]
    public void DataBar_AutoMinMaxThresholdType_IgnoresValue_EvenWhenGarbageOrBlank(CfThresholdType type)
    {
        // AutoMin/AutoMax ("Automatic") are the data-bar-only Automatic endpoint and, like the explicit
        // Min/Max endpoint above, derive their bound from the range data rather than typed text — this
        // is also what lets a brand-new data bar (which now defaults DataBarMinType/DataBarMaxType to
        // AutoMin/AutoMax) validate successfully with its value boxes left blank.
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.DataBar);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.DataBar,
            DataBarMinType = type,
            DataBarMinValue = "abc",
            DataBarMaxType = type,
            DataBarMaxValue = null
        };

        schema.Validate(input).IsValid.Should().BeTrue();
    }

    [Fact]
    public void DataBar_FormulaThresholdType_Invalid_WhenBlank()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.DataBar);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.DataBar,
            DataBarMinType = CfThresholdType.Formula,
            DataBarMinValue = "  "
        };

        var result = schema.Validate(input);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == CfInputField.DataBarMinValue);
    }

    [Fact]
    public void DataBar_FormulaThresholdType_Valid_WhenFormulaProvided()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.DataBar);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.DataBar,
            DataBarMinType = CfThresholdType.Formula,
            DataBarMinValue = "=A1"
        };

        schema.Validate(input).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(CfThresholdType.Number)]
    [InlineData(CfThresholdType.Percent)]
    [InlineData(CfThresholdType.Percentile)]
    public void ColorScale_MinValue_Invalid_WhenNotANumber(CfThresholdType type)
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.ColorScale);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.ColorScale,
            MinColor = "0,0,0",
            MaxColor = "255,255,255",
            ColorScaleMinType = type,
            ColorScaleMinValue = "abc"
        };

        var result = schema.Validate(input);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == CfInputField.ColorScaleMinValue);
    }

    [Fact]
    public void ColorScale_MidValue_Invalid_WhenNotANumber_AndThreeColorScale()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.ColorScale);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinColor = "0,0,0",
            MidColor = "128,128,128",
            MaxColor = "255,255,255",
            ColorScaleMidType = CfThresholdType.Percentile,
            ColorScaleMidValue = "not-a-percentile"
        };

        var result = schema.Validate(input);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == CfInputField.ColorScaleMidValue);
    }

    [Fact]
    public void ColorScale_MidValue_Ignored_WhenTwoColorScale_EvenIfGarbage()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.ColorScale);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = false,
            MinColor = "0,0,0",
            MaxColor = "255,255,255",
            ColorScaleMidType = CfThresholdType.Percentile,
            ColorScaleMidValue = "garbage"
        };

        schema.Validate(input).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ColorScale_AllValues_Valid_WhenNumericAndColorsParse()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.ColorScale);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinColor = "99,190,123",
            MidColor = "255,235,132",
            MaxColor = "248,105,107",
            ColorScaleMinType = CfThresholdType.Number,
            ColorScaleMinValue = "0",
            ColorScaleMidType = CfThresholdType.Percentile,
            ColorScaleMidValue = "50",
            ColorScaleMaxType = CfThresholdType.Number,
            ColorScaleMaxValue = "100"
        };

        schema.Validate(input).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(CfThresholdType.Min)]
    [InlineData(CfThresholdType.Max)]
    public void ColorScale_MinMaxThresholdType_IgnoresValue_EvenWhenGarbageOrBlank(CfThresholdType type)
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.ColorScale);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = false,
            MinColor = "0,0,0",
            MaxColor = "255,255,255",
            ColorScaleMinType = type,
            ColorScaleMinValue = "not numeric",
            ColorScaleMaxType = type,
            ColorScaleMaxValue = null
        };

        schema.Validate(input).IsValid.Should().BeTrue();
    }
}
