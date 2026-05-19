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
    public void CloneDataValidationForSheet_RemapsRangeAndCopiesPromptAndErrorFields()
    {
        var targetSheet = SheetId.New();
        var source = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(SheetId.New(), 2, 2), new CellAddress(SheetId.New(), 6, 2)),
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

        var clone = GroupedSheetRangePlanner.CloneDataValidationForSheet(source, targetSheet);

        clone.Should().NotBeSameAs(source);
        clone.AppliesTo.Start.Sheet.Should().Be(targetSheet);
        clone.AppliesTo.End.Sheet.Should().Be(targetSheet);
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
