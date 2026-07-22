using FluentAssertions;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Dialogs;

public sealed class ConditionalFormatRuleSchemaTests
{
    [Theory]
    [InlineData(CfRuleType.Formula, CfInputField.Formula)]
    [InlineData(CfRuleType.CellValue, CfInputField.Operator)]
    [InlineData(CfRuleType.CellValue, CfInputField.Value1)]
    [InlineData(CfRuleType.CellValue, CfInputField.Value2)]
    [InlineData(CfRuleType.Top10, CfInputField.Rank)]
    [InlineData(CfRuleType.Top10, CfInputField.TopBottom)]
    [InlineData(CfRuleType.Top10, CfInputField.Percent)]
    [InlineData(CfRuleType.IconSet, CfInputField.IconSetStyle)]
    [InlineData(CfRuleType.DataBar, CfInputField.DataBarMinMaxType)]
    [InlineData(CfRuleType.DataBar, CfInputField.DataBarColors)]
    [InlineData(CfRuleType.DataBar, CfInputField.DataBarMinLength)]
    [InlineData(CfRuleType.DataBar, CfInputField.DataBarMaxLength)]
    [InlineData(CfRuleType.ColorScale, CfInputField.UseThreeColorScale)]
    [InlineData(CfRuleType.ColorScale, CfInputField.ColorScaleThresholdTypes)]
    [InlineData(CfRuleType.ColorScale, CfInputField.ColorScaleColors)]
    [InlineData(CfRuleType.ColorScale, CfInputField.ColorScaleMinColor)]
    [InlineData(CfRuleType.ColorScale, CfInputField.ColorScaleMidColor)]
    [InlineData(CfRuleType.ColorScale, CfInputField.ColorScaleMaxColor)]
    [InlineData(CfRuleType.DateOccurring, CfInputField.DatePeriod)]
    [InlineData(CfRuleType.DuplicateValues, CfInputField.DuplicateOrUnique)]
    [InlineData(CfRuleType.UniqueValues, CfInputField.DuplicateOrUnique)]
    [InlineData(CfRuleType.ContainsText, CfInputField.Text)]
    [InlineData(CfRuleType.NotContainsText, CfInputField.Text)]
    [InlineData(CfRuleType.BeginsWith, CfInputField.Text)]
    [InlineData(CfRuleType.EndsWith, CfInputField.Text)]
    public void ForRuleType_IncludesExpectedField(CfRuleType ruleType, CfInputField field)
    {
        ConditionalFormatRuleSchema.ForRuleType(ruleType).HasField(field).Should().BeTrue();
    }

    [Theory]
    [InlineData(CfRuleType.Blanks)]
    [InlineData(CfRuleType.NoBlanks)]
    [InlineData(CfRuleType.Errors)]
    [InlineData(CfRuleType.NoErrors)]
    [InlineData(CfRuleType.AboveAverage)]
    public void ForRuleType_ValuelessRules_HaveNoFields(CfRuleType ruleType)
    {
        ConditionalFormatRuleSchema.ForRuleType(ruleType).Fields.Should().BeEmpty();
    }

    [Fact]
    public void CfRuleInput_Default_DataBarThresholdTypesAreAutomatic()
    {
        var input = new CfRuleInput();

        input.DataBarMinType.Should().Be(CfThresholdType.AutoMin);
        input.DataBarMaxType.Should().Be(CfThresholdType.AutoMax);
    }

    [Fact]
    public void Formula_Valid_WhenFormulaProvided()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.Formula);
        var input = new CfRuleInput { RuleType = CfRuleType.Formula, Formula = "=A1>0" };

        schema.Validate(input).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("=")]
    [InlineData("   ")]
    public void Formula_Invalid_WhenEmptyOrBare(string? formula)
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.Formula);
        var input = new CfRuleInput { RuleType = CfRuleType.Formula, Formula = formula };

        var result = schema.Validate(input);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Field.Should().Be(CfInputField.Formula);
    }

    [Fact]
    public void CellValue_Valid_WithSingleValue()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.CellValue);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "10"
        };

        schema.Validate(input).IsValid.Should().BeTrue();
    }

    [Fact]
    public void CellValue_Invalid_WhenValue1Missing()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.CellValue);
        var input = new CfRuleInput { RuleType = CfRuleType.CellValue, Value1 = "  " };

        var result = schema.Validate(input);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Field.Should().Be(CfInputField.Value1);
    }

    [Theory]
    [InlineData(CfOperator.Between)]
    [InlineData(CfOperator.NotBetween)]
    public void CellValue_Between_RequiresValue2(CfOperator op)
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.CellValue);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.CellValue,
            Operator = op,
            Value1 = "1",
            Value2 = null
        };

        var result = schema.Validate(input);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Field.Should().Be(CfInputField.Value2);
    }

    [Fact]
    public void CellValue_Between_Valid_WhenBothValuesPresent()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.CellValue);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.Between,
            Value1 = "1",
            Value2 = "9"
        };

        schema.Validate(input).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("1")]
    [InlineData("10")]
    [InlineData("1000")]
    public void Top10_Rank_Valid_InRange(string rank)
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.Top10);
        var input = new CfRuleInput { RuleType = CfRuleType.Top10, IsPercent = false, Rank = rank };

        schema.Validate(input).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1001")]
    [InlineData("abc")]
    [InlineData(null)]
    public void Top10_Rank_Invalid_OutOfRange(string? rank)
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.Top10);
        var input = new CfRuleInput { RuleType = CfRuleType.Top10, IsPercent = false, Rank = rank };

        var result = schema.Validate(input);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Field.Should().Be(CfInputField.Rank);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("50")]
    [InlineData("100")]
    public void Top10_Percent_Valid_InRange(string percent)
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.Top10);
        var input = new CfRuleInput { RuleType = CfRuleType.Top10, IsPercent = true, Rank = percent };

        schema.Validate(input).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    public void Top10_Percent_Invalid_OutOfRange(string percent)
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.Top10);
        var input = new CfRuleInput { RuleType = CfRuleType.Top10, IsPercent = true, Rank = percent };

        schema.Validate(input).IsValid.Should().BeFalse();
    }

    [Fact]
    public void IconSet_Valid_WhenStyleProvided()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.IconSet);
        var input = new CfRuleInput { RuleType = CfRuleType.IconSet, IconSetStyle = "3TrafficLights1" };

        schema.Validate(input).IsValid.Should().BeTrue();
    }

    [Fact]
    public void IconSet_Invalid_WhenStyleMissing()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.IconSet);
        var input = new CfRuleInput { RuleType = CfRuleType.IconSet, IconSetStyle = null };

        var result = schema.Validate(input);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Field.Should().Be(CfInputField.IconSetStyle);
    }

    [Fact]
    public void DataBar_Valid_WithDefaults()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.DataBar);
        var input = new CfRuleInput { RuleType = CfRuleType.DataBar };

        schema.Validate(input).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("0", true)]
    [InlineData("100", true)]
    [InlineData("-1", false)]
    [InlineData("101", false)]
    [InlineData("abc", false)]
    public void DataBar_LengthPercent_ValidatesOptionalRange(string value, bool expectedValid)
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.DataBar);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.DataBar,
            DataBarMinLength = value,
            DataBarMaxLength = value
        };

        schema.Validate(input).IsValid.Should().Be(expectedValid);
    }

    [Fact]
    public void ColorScale_TwoColor_Valid_WhenMinMaxColorsParse()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.ColorScale);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = false,
            MinColor = "99,190,123",
            MaxColor = "248,105,107"
        };

        schema.Validate(input).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ColorScale_ThreeColor_Valid_WhenAllThreeColorsParse()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.ColorScale);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinColor = "99,190,123",
            MidColor = "255,235,132",
            MaxColor = "248,105,107",
            // ColorScaleMidType defaults to Percentile (matching the dialog's default midpoint
            // type), so a value is required — mirrors the dialog's own "50" default text.
            ColorScaleMidValue = "50"
        };

        schema.Validate(input).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ColorScale_Invalid_WhenMinColorUnparseable()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.ColorScale);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = false,
            MinColor = "not-a-color",
            MaxColor = "248,105,107"
        };

        var result = schema.Validate(input);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == CfInputField.ColorScaleMinColor);
    }

    [Fact]
    public void ColorScale_ThreeColor_Invalid_WhenMidColorMissing()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.ColorScale);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinColor = "0,0,0",
            MidColor = null,
            MaxColor = "255,255,255"
        };

        var result = schema.Validate(input);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == CfInputField.ColorScaleMidColor);
    }

    [Fact]
    public void ColorScale_TwoColor_IgnoresMissingMidColor()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.ColorScale);
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = false,
            MinColor = "0,0,0",
            MidColor = null,
            MaxColor = "255,255,255"
        };

        schema.Validate(input).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(CfRuleType.ContainsText)]
    [InlineData(CfRuleType.NotContainsText)]
    [InlineData(CfRuleType.BeginsWith)]
    [InlineData(CfRuleType.EndsWith)]
    public void TextRules_Invalid_WhenTextMissing(CfRuleType ruleType)
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(ruleType);
        var input = new CfRuleInput { RuleType = ruleType, Text = " " };

        var result = schema.Validate(input);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Field.Should().Be(CfInputField.Text);
    }

    [Fact]
    public void TextRule_Valid_WhenTextProvided()
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(CfRuleType.ContainsText);
        var input = new CfRuleInput { RuleType = CfRuleType.ContainsText, Text = "error" };

        schema.Validate(input).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(CfRuleType.DateOccurring)]
    [InlineData(CfRuleType.DuplicateValues)]
    [InlineData(CfRuleType.UniqueValues)]
    [InlineData(CfRuleType.Blanks)]
    [InlineData(CfRuleType.Errors)]
    [InlineData(CfRuleType.AboveAverage)]
    public void ChoiceOnlyRules_AlwaysValid(CfRuleType ruleType)
    {
        var schema = ConditionalFormatRuleSchema.ForRuleType(ruleType);
        var input = new CfRuleInput { RuleType = ruleType };

        schema.Validate(input).IsValid.Should().BeTrue();
    }
}
