using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class GroupedSheetRangePlannerTests
{
    [Fact]
    public void RemapRangeToSheet_PreservesCoordinatesAndChangesSheet()
    {
        var sourceSheet = SheetId.New();
        var targetSheet = SheetId.New();
        var range = new GridRange(
            new CellAddress(sourceSheet, 2, 3),
            new CellAddress(sourceSheet, 5, 8));

        var remapped = GroupedSheetRangePlanner.RemapRangeToSheet(range, targetSheet);

        remapped.Start.Should().Be(new CellAddress(targetSheet, 2, 3));
        remapped.End.Should().Be(new CellAddress(targetSheet, 5, 8));
    }

    [Fact]
    public void CloneConditionalFormatForSheet_RemapsRangeAndClonesFormatDiff()
    {
        var targetSheet = SheetId.New();
        var source = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(SheetId.New(), 1, 1), new CellAddress(SheetId.New(), 4, 2)),
            Priority = 3,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "10",
            FormatIfTrue = new CellStyle { Bold = true, FillColor = new CellColor(1, 2, 3) },
            StopIfTrue = true
        };

        var clone = GroupedSheetRangePlanner.CloneConditionalFormatForSheet(source, targetSheet);

        clone.Should().NotBeSameAs(source);
        clone.AppliesTo.Start.Sheet.Should().Be(targetSheet);
        clone.AppliesTo.End.Sheet.Should().Be(targetSheet);
        clone.Priority.Should().Be(3);
        clone.RuleType.Should().Be(CfRuleType.CellValue);
        clone.Operator.Should().Be(CfOperator.GreaterThan);
        clone.Value1.Should().Be("10");
        clone.StopIfTrue.Should().BeTrue();
        clone.FormatIfTrue.Should().NotBeSameAs(source.FormatIfTrue);
        clone.FormatIfTrue.Should().Be(source.FormatIfTrue);
    }

    [Fact]
    public void CloneConditionalFormatForSheet_PreservesAdvancedFields()
    {
        var targetSheet = SheetId.New();
        var source = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(SheetId.New(), 1, 1), new CellAddress(SheetId.New(), 4, 2)),
            Priority = 3,
            RuleType = CfRuleType.IconSet,
            MinThresholdType = CfThresholdType.Number,
            MinThresholdValue = "5",
            MinThresholdGreaterThanOrEqual = false,
            MidThresholdType = CfThresholdType.Percent,
            MidThresholdValue = "50",
            MidThresholdGreaterThanOrEqual = true,
            MaxThresholdType = CfThresholdType.Formula,
            MaxThresholdValue = "A1",
            MaxThresholdGreaterThanOrEqual = false,
            DataBarMinThresholdType = CfThresholdType.Percentile,
            DataBarMinThresholdValue = "10",
            DataBarMaxThresholdType = CfThresholdType.Number,
            DataBarMaxThresholdValue = "99",
            DataBarShowValue = false,
            DataBarMinLength = 5,
            DataBarMaxLength = 95,
            IconSetStyle = "5Arrows",
            IconSetShowValue = false,
            IconSetReverse = true,
            TopBottomRank = 3,
            TopBottomPercent = true,
            TextRuleText = "urgent",
            DateOccurringPeriod = "last7Days"
        };

        var clone = GroupedSheetRangePlanner.CloneConditionalFormatForSheet(source, targetSheet);

        clone.Should().BeEquivalentTo(source, options => options
            .Excluding(rule => rule.Id)
            .Excluding(rule => rule.AppliesTo));
        clone.AppliesTo.Start.Sheet.Should().Be(targetSheet);
        clone.AppliesTo.End.Sheet.Should().Be(targetSheet);
    }

    [Fact]
    public void CloneConditionalFormatForSheet_DropsExistingX14IdNativeChild()
    {
        var targetSheet = SheetId.New();
        var source = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(SheetId.New(), 1, 1), new CellAddress(SheetId.New(), 4, 2)),
            RuleType = CfRuleType.DataBar,
            NativeChildXmls =
            [
                """<extLst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><ext uri="{B025F937-6E4E-48BE-B07C-B91C50BE2FA4}"><x14:id xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">{11111111-2222-3333-4444-555555555555}</x14:id></ext><ext uri="{FUTURE}" /></extLst>""",
                """<future xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" />"""
            ],
            NativePayloadChildXmls = ["""<axisColor xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main" theme="1" />"""]
        };

        var clone = GroupedSheetRangePlanner.CloneConditionalFormatForSheet(source, targetSheet);

        clone.Id.Should().NotBe(source.Id);
        clone.NativeChildXmls.Should().HaveCount(2);
        clone.NativeChildXmls.Should().Contain(xml => xml.Contains("{FUTURE}", StringComparison.Ordinal));
        clone.NativeChildXmls.Should().Contain(xml => xml.Contains("future", StringComparison.Ordinal));
        clone.NativeChildXmls.Should().NotContain(xml => xml.Contains("11111111-2222-3333-4444-555555555555", StringComparison.Ordinal));
        clone.NativePayloadChildXmls.Should().BeEquivalentTo(source.NativePayloadChildXmls);
    }

    [Fact]
    public void CloneDataValidationForSheet_RemapsRangeAndCopiesPromptAndErrorFields()
    {
        var targetSheet = SheetId.New();
        var sourceSheet = SheetId.New();
        var source = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sourceSheet, 2, 2), new CellAddress(sourceSheet, 6, 2)),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = true,
            ShowDropdown = false,
            AlertStyle = DvAlertStyle.Warning,
            ShowInputMessage = true,
            ShowErrorMessage = true,
            ErrorTitle = "Invalid",
            ErrorMessage = "Use 1-10",
            PromptTitle = "Value",
            PromptMessage = "Enter a whole number"
        };
        source.AdditionalRanges.Add(new GridRange(new CellAddress(sourceSheet, 8, 4), new CellAddress(sourceSheet, 10, 4)));

        var clone = GroupedSheetRangePlanner.CloneDataValidationForSheet(source, targetSheet);

        clone.Should().NotBeSameAs(source);
        clone.AppliesTo.Start.Sheet.Should().Be(targetSheet);
        clone.AppliesTo.End.Sheet.Should().Be(targetSheet);
        clone.AdditionalRanges.Should().ContainSingle().Which.Start.Sheet.Should().Be(targetSheet);
        clone.Type.Should().Be(DvType.WholeNumber);
        clone.Operator.Should().Be(DvOperator.Between);
        clone.Formula1.Should().Be("1");
        clone.Formula2.Should().Be("10");
        clone.AllowBlank.Should().BeTrue();
        clone.ShowDropdown.Should().BeFalse();
        clone.AlertStyle.Should().Be(DvAlertStyle.Warning);
        clone.ShowInputMessage.Should().BeTrue();
        clone.ShowErrorMessage.Should().BeTrue();
        clone.ErrorTitle.Should().Be("Invalid");
        clone.ErrorMessage.Should().Be("Use 1-10");
        clone.PromptTitle.Should().Be("Value");
        clone.PromptMessage.Should().Be("Enter a whole number");
    }
}
