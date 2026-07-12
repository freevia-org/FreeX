using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R35-deferred-cf-manage-priority-1/2: adding a second single-rule conditional format via
/// ApplyConditionalFormatCommand (any Highlight Cells/Icon Set/Data Bar/Color Scale preset or the
/// New Rule dialog), or pasting a conditional format via PasteConditionalFormatsCommand, must not
/// leave two active rules on the same sheet tied at the same Priority. ConditionalFormat.Priority
/// defaults to 1 and none of the single-rule builders assign a distinct one, so without a fresh-slot
/// assignment in the command's "add" path the duplicate priority gets written verbatim into the
/// saved .xlsx (two &lt;cfRule priority="1"&gt; blocks), which Excel itself never produces.
/// </summary>
public sealed class R35_CfManagePriorityTests
{
    private static ConditionalFormat MakeRule(SheetId sheetId, uint fromRow, uint toRow, uint col, int priority = 1) =>
        new()
        {
            AppliesTo = new GridRange(new CellAddress(sheetId, fromRow, col), new CellAddress(sheetId, toRow, col)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "5",
            Priority = priority,
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        };

    [Fact]
    public void ApplyConditionalFormatCommand_SecondRuleAdd_GetsDistinctPriorityFromFirst()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // First rule, applied on A1:A10 -- comes in at the model default Priority = 1, exactly like
        // every single-rule preset/New-Rule builder produces (none of them assign Priority).
        var first = MakeRule(sheet.Id, 1, 10, 1);
        new ApplyConditionalFormatCommand(sheet.Id, first)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        // Second rule, applied on B1:B10 -- also arrives at the default Priority = 1 (mirrors real
        // preset-gallery/New-Rule construction, none of which sets Priority before building the command).
        var second = MakeRule(sheet.Id, 1, 10, 2);
        new ApplyConditionalFormatCommand(sheet.Id, second)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        sheet.ConditionalFormats.Should().HaveCount(2);
        sheet.ConditionalFormats[0].Priority.Should().Be(1);
        sheet.ConditionalFormats[1].Priority.Should().NotBe(sheet.ConditionalFormats[0].Priority,
            "Excel never writes two active rules on one sheet with the same cfRule priority");
        sheet.ConditionalFormats.Select(f => f.Priority).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public void ApplyConditionalFormatCommand_EditingExistingRuleById_KeepsItsOwnPriority()
    {
        // Sibling no-regression case: the "replace by Id" branch (editing a rule via the same command)
        // must be untouched by the new-add priority renumbering -- it should keep whatever priority the
        // edited rule specifies, not get bumped past the sheet's max.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        var rule = MakeRule(sheet.Id, 1, 10, 1);
        new ApplyConditionalFormatCommand(sheet.Id, rule)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        var edited = rule.Clone();
        edited.Value1 = "10";
        new ApplyConditionalFormatCommand(sheet.Id, edited)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        sheet.ConditionalFormats.Should().HaveCount(1);
        sheet.ConditionalFormats[0].Priority.Should().Be(1);
        sheet.ConditionalFormats[0].Value1.Should().Be("10");
    }

    [Fact]
    public void PasteConditionalFormatsCommand_PastedRule_GetsDistinctPriorityFromExisting()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // Existing rule R1 on A1:A10, Priority = 1.
        sheet.ConditionalFormats.Add(MakeRule(sheet.Id, 1, 10, 1));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1));
        var destination = new CellAddress(sheet.Id, 1, 3); // paste onto column C, same sheet

        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, destination, transpose: false)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        sheet.ConditionalFormats.Should().HaveCount(2);
        var pasted = sheet.ConditionalFormats[1];
        pasted.Priority.Should().NotBe(sheet.ConditionalFormats[0].Priority,
            "Excel's paste-with-formatting never leaves two active rules tied at the same priority");
        sheet.ConditionalFormats.Select(f => f.Priority).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public void PasteConditionalFormatsCommand_PasteOntoEmptySheet_KeepsFirstPastedRuleAtPriorityOne()
    {
        // Sibling no-regression case: pasting onto a sheet with no existing rules must still start
        // the pasted rule's priority sequence at 1 (not skip ahead), matching a from-scratch paste.
        var wb = new Workbook("test");
        var sourceSheet = wb.AddSheet("Sheet1");
        var targetSheet = wb.AddSheet("Sheet2");

        sourceSheet.ConditionalFormats.Add(MakeRule(sourceSheet.Id, 1, 10, 1));

        var sourceRange = new GridRange(new CellAddress(sourceSheet.Id, 1, 1), new CellAddress(sourceSheet.Id, 10, 1));
        var destination = new CellAddress(targetSheet.Id, 1, 1);

        new PasteConditionalFormatsCommand(targetSheet.Id, sourceRange, destination, transpose: false)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        targetSheet.ConditionalFormats.Should().HaveCount(1);
        targetSheet.ConditionalFormats[0].Priority.Should().Be(1);
    }
}
