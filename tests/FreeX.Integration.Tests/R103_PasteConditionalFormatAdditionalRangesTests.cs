using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R103-commands-1: PasteConditionalFormatsCommand.Apply selected candidate rules with
/// <c>rule.AppliesTo.Overlaps(_sourceRange)</c> alone, completely ignoring
/// <see cref="ConditionalFormat.AdditionalRanges"/> -- even though
/// <see cref="ConditionalFormat.AllRanges"/> exists precisely to enumerate AppliesTo plus
/// AdditionalRanges together, and ApplyConditionalFormatCommand routinely produces rules with
/// AdditionalRanges populated (applying one CF rule to a Ctrl+click multi-area selection, e.g.
/// A1:A5 and C1:C5, produces ONE rule with AppliesTo=A1:A5 and AdditionalRanges=[C1:C5]). Copying a
/// cell that is only covered by the rule's AdditionalRanges entry (never by AppliesTo) silently
/// dropped the rule from the paste entirely. Mirrors PasteDataValidationCommand's
/// EnumerateRuleRanges/IntersectWithSource handling of the identical multi-area shape
/// (R78-commands-paste-special-5-4).
/// </summary>
public sealed class R103_PasteConditionalFormatAdditionalRangesTests
{
    [Fact]
    public void PasteConditionalFormatsCommand_CopyingCellCoveredOnlyByAdditionalRanges_PastesTheRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // One rule applied to the multi-area selection A1:A5 (AppliesTo) plus C1:C5
        // (AdditionalRanges) -- exactly the shape ApplyConditionalFormatCommand produces for a
        // Ctrl+click multi-area CF application.
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            AdditionalRanges = [new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 5, 3))],
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        // Copy C3 -- covered ONLY via AdditionalRanges (C1:C5), never via AppliesTo (A1:A5) -- and
        // paste it to E3.
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 3, 3), new CellAddress(sheet.Id, 3, 3)); // C3
        var destination = new CellAddress(sheet.Id, 3, 5); // E3

        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, destination, transpose: false)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        // The rule must NOT have been silently dropped: a second rule must now exist, anchored at
        // the destination cell E3.
        sheet.ConditionalFormats.Should().HaveCount(2);
        var pasted = sheet.ConditionalFormats[^1];
        pasted.AppliesTo.Should().Be(new GridRange(new CellAddress(sheet.Id, 3, 5), new CellAddress(sheet.Id, 3, 5)));
        pasted.RuleType.Should().Be(CfRuleType.CellValue);

        // Behavioral proof through the real evaluator: E3 = 5 (>0, condition true) must now render
        // the rule's fill.
        sheet.SetCell(new CellAddress(sheet.Id, 3, 5), Cell.FromValue(new NumberValue(5)));

        var svc = new ViewportService();
        var vp = svc.GetViewport(wb, sheet.Id, new ViewportRequest(1, 1, 500, 500));
        var e3 = vp.Cells.Single(c => c.Row == 3 && c.Col == 5);

        e3.Style?.FillColor.Should().Be(new CellColor(255, 0, 0),
            "the rule anchored purely by AdditionalRanges must still be carried by copy-paste of a cell it covers");
    }

    [Fact]
    public void PasteConditionalFormatsCommand_CopyingCellCoveredByAppliesTo_StillPastesTheRule()
    {
        // No-regression sibling: the ordinary case (a rule matched via its primary AppliesTo, with
        // no AdditionalRanges at all) must keep working exactly as before.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 3, 1)); // A3
        var destination = new CellAddress(sheet.Id, 3, 5); // E3

        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, destination, transpose: false)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        sheet.ConditionalFormats.Should().HaveCount(2);
        var pasted = sheet.ConditionalFormats[^1];
        pasted.AppliesTo.Should().Be(new GridRange(new CellAddress(sheet.Id, 3, 5), new CellAddress(sheet.Id, 3, 5)));
    }

    [Fact]
    public void PasteConditionalFormatsCommand_CopyingCellNotCoveredByEitherRange_StillDropsTheRule()
    {
        // No-regression sibling: a rule that genuinely does not overlap the copied cell in EITHER
        // AppliesTo or AdditionalRanges must still be correctly excluded from the paste (the fix
        // must not start pasting unrelated rules).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            AdditionalRanges = [new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 5, 3))],
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        // Copy G3 -- not covered by AppliesTo (A1:A5) nor AdditionalRanges (C1:C5).
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 3, 7), new CellAddress(sheet.Id, 3, 7)); // G3
        var destination = new CellAddress(sheet.Id, 3, 9); // I3

        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, destination, transpose: false)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        sheet.ConditionalFormats.Should().HaveCount(1, "the copied cell was never covered by this rule");
    }
}
