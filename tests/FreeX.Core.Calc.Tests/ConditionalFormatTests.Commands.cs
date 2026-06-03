using FreeX.Core.Commands;
using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class ConditionalFormatTests
{
    [Fact]
    public void ApplyConditionalFormatCommand_Revert_RemovesRule()
    {
        // Arrange
        var (wb, sheet) = MakeWorkbook();

        var cf = new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)),
            Priority     = 1,
            RuleType     = CfRuleType.CellValue,
            Operator     = CfOperator.GreaterThan,
            Value1       = "0",
            FormatIfTrue = new CellStyle { Bold = true }
        };

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        var cmd = new ApplyConditionalFormatCommand(sheet.Id, cf);

        // Apply
        bus.Execute(wb.Id, cmd);
        sheet.ConditionalFormats.Should().HaveCount(1);

        // Undo (revert)
        bus.Undo(wb.Id);
        sheet.ConditionalFormats.Should().BeEmpty("revert should remove the rule");
    }

    [Fact]
    public void ReplaceAllCF_Commit_ReplacesAllRules()
    {
        var (wb, sheet) = MakeWorkbook();

        var oldRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            Priority = 1, RuleType = CfRuleType.CellValue,
            FormatIfTrue = new CellStyle { Bold = true }
        };
        sheet.ConditionalFormats.Add(oldRule);

        var newRule1 = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1, RuleType = CfRuleType.Formula,
            FormulaText = "A2>0", FormatIfTrue = new CellStyle { FillColor = new CellColor(0, 255, 0) }
        };
        var newRule2 = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 2, RuleType = CfRuleType.CellValue,
            FormatIfTrue = new CellStyle { Italic = true }
        };

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        var cmd = new ReplaceAllConditionalFormatsCommand(sheet.Id, [newRule1, newRule2]);

        bus.Execute(wb.Id, cmd);

        sheet.ConditionalFormats.Should().HaveCount(2);
        sheet.ConditionalFormats.Should().NotContain(r => r.Id == oldRule.Id, "old rule replaced");
        sheet.ConditionalFormats.Should().ContainSingle(r => r.Id == newRule1.Id);
    }

    [Fact]
    public void ReplaceAllCF_Undo_RestoresOriginalRules()
    {
        var (wb, sheet) = MakeWorkbook();

        var original = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            Priority = 1, RuleType = CfRuleType.CellValue,
            FormatIfTrue = new CellStyle { Bold = true }
        };
        sheet.ConditionalFormats.Add(original);

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        var cmd = new ReplaceAllConditionalFormatsCommand(sheet.Id, []); // replace with empty

        bus.Execute(wb.Id, cmd);
        sheet.ConditionalFormats.Should().BeEmpty();

        bus.Undo(wb.Id);
        sheet.ConditionalFormats.Should().HaveCount(1);
        sheet.ConditionalFormats[0].Id.Should().Be(original.Id, "undo restores the original rule");
    }
}
