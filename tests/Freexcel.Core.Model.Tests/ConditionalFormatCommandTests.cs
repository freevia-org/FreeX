using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Model.Tests;

public sealed class ConditionalFormatCommandTests
{
    [Fact]
    public void ApplyConditionalFormatCommand_AddsRuleAndUndoRemovesIt()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var rule = NewRule(sheet.Id);

        var command = new ApplyConditionalFormatCommand(sheet.Id, rule);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.ConditionalFormats.Should().ContainSingle().Which.Should().BeSameAs(rule);

        command.Revert(ctx);

        sheet.ConditionalFormats.Should().BeEmpty();
    }

    [Fact]
    public void ApplyConditionalFormatCommand_RejectsInvalidRuleChoices()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var original = NewRule(sheet.Id);
        sheet.ConditionalFormats.Add(original);

        var invalidType = NewRule(sheet.Id);
        invalidType.RuleType = (CfRuleType)99;
        var invalidOperator = NewRule(sheet.Id);
        invalidOperator.Operator = (CfOperator)99;

        foreach (var invalidRule in new[] { invalidType, invalidOperator })
        {
            var outcome = new ApplyConditionalFormatCommand(sheet.Id, invalidRule).Apply(ctx);

            outcome.Success.Should().BeFalse();
            sheet.ConditionalFormats.Should().ContainSingle().Which.Should().BeSameAs(original);
        }
    }

    [Fact]
    public void ApplyConditionalFormatCommand_RejectsRuleRangeOnAnotherSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var other = wb.AddSheet("Other");
        var ctx = new SimpleCtx(wb);

        var outcome = new ApplyConditionalFormatCommand(sheet.Id, NewRule(other.Id)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.ConditionalFormats.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceAllConditionalFormatsCommand_RejectsInvalidRulesBeforeClearingExistingRules()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var original = NewRule(sheet.Id);
        sheet.ConditionalFormats.Add(original);
        var invalid = NewRule(sheet.Id);
        invalid.RuleType = (CfRuleType)99;

        var outcome = new ReplaceAllConditionalFormatsCommand(sheet.Id, [invalid]).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.ConditionalFormats.Should().ContainSingle().Which.Should().BeSameAs(original);
    }

    private static ConditionalFormat NewRule(SheetId sheetId) =>
        new()
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 5, 1)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "10",
            FormatIfTrue = new CellStyle { Bold = true }
        };

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
