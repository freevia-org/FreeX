using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R15-cf-rule-management-3: ClearConditionalFormatsCommand must consider a rule's
/// AdditionalRanges (secondary sqref ranges), not just AppliesTo, when deciding whether
/// the rule overlaps the cells being cleared.
/// </summary>
public sealed class R15_cf_clear_Tests
{
    [Fact]
    public void ClearConditionalFormats_SelectionOverlapsOnlyAdditionalRange_RuleIsRemoved()
    {
        // Rule sqref is "A1:A5 D1:D5" (AppliesTo = A1:A5, AdditionalRanges = [D1:D5]).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var rule = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 1)),
            AdditionalRanges =
            [
                new GridRange(
                    new CellAddress(sheet.Id, 1, 4),
                    new CellAddress(sheet.Id, 5, 4))
            ],
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "10",
            FormatIfTrue = new CellStyle { Bold = true }
        };
        sheet.ConditionalFormats.Add(rule);

        // Clear Rules from Selected Cells over D1:D5 only (the secondary range).
        var clearRange = new GridRange(
            new CellAddress(sheet.Id, 1, 4),
            new CellAddress(sheet.Id, 5, 4));

        var outcome = new ClearConditionalFormatsCommand(sheet.Id, clearRange).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ConditionalFormats.Should().BeEmpty();
    }

    [Fact]
    public void ClearConditionalFormats_SelectionOverlapsOnlyAdditionalRange_UndoRestoresRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var rule = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 1)),
            AdditionalRanges =
            [
                new GridRange(
                    new CellAddress(sheet.Id, 1, 4),
                    new CellAddress(sheet.Id, 5, 4))
            ],
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "10",
            FormatIfTrue = new CellStyle { Bold = true }
        };
        sheet.ConditionalFormats.Add(rule);

        var clearRange = new GridRange(
            new CellAddress(sheet.Id, 1, 4),
            new CellAddress(sheet.Id, 5, 4));

        var command = new ClearConditionalFormatsCommand(sheet.Id, clearRange);
        command.Apply(ctx);
        sheet.ConditionalFormats.Should().BeEmpty();

        command.Revert(ctx);

        sheet.ConditionalFormats.Should().ContainSingle().Which.Should().BeSameAs(rule);
    }
}
