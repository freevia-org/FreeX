using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for R25-spill-dynamic-deep-3: selecting ONLY the anchor cell of a live
/// dynamic-array spill and pressing Delete must clear the formula and the whole spill, matching
/// Excel and mirroring the analogous R25-spill-dynamic-deep-1 fix already applied to
/// MoveRangeCommand.
///
/// R123-dynamic-spill-member-write superseded the two "still blocked" tests this file originally
/// had for a non-anchor member alone / anchor+partial body: real Excel has NO "you cannot change
/// part of an array" restriction on a live DYNAMIC array's spill footprint at all (unlike a legacy
/// CSE array) -- clearing any subset of it, anchor included or not, is a normal allowed edit. Both
/// tests are renamed "...IsAllowed_R123" below and now assert the allowed outcome.
/// </summary>
public class ClearContentsSpillAnchorTests
{
    private const string CannotChangePartOfArrayMessage = "You cannot change part of an array.";

    private static (Workbook Workbook, Sheet Sheet, CellAddress Anchor, ICommandContext Ctx) MakeLiveSpillSetup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetCell(anchor, Cell.FromFormula("SEQUENCE(3,1)"));
        var cells = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        };
        sheet.SetSpillRange(anchor, new RangeValue(cells)); // spills A1:A3 = 1,2,3
        return (wb, sheet, anchor, new TestCommandContext(wb));
    }

    [Fact]
    public void ClearContentsCommand_OnSpillAnchorAlone_ClearsWholeSpill()
    {
        var (_, sheet, anchor, ctx) = MakeLiveSpillSetup();
        var row2 = new CellAddress(sheet.Id, 2, 1); // A2
        var row3 = new CellAddress(sheet.Id, 3, 1); // A3

        var outcome = new ClearContentsCommand(sheet.Id, new GridRange(anchor, anchor)).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(anchor).Should().Be(BlankValue.Instance);
        sheet.GetValue(row2).Should().Be(BlankValue.Instance);
        sheet.GetValue(row3).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void ClearContentsCommand_OnNonAnchorSpillMemberAlone_IsAllowed_R123()
    {
        // R123-dynamic-spill-member-write: a single non-anchor member of a live DYNAMIC array's
        // spill, selected alone, is no longer rejected -- there is no "whole array must be
        // selected" restriction for a modern dynamic array at all (unlike a legacy CSE array). A2
        // has no independent cell content of its own (it exists only as a computed spill overlay
        // value), so -- matching Excel, where Delete on such a cell is a genuine no-op -- clearing
        // it leaves its displayed (spilled) value and the rest of the array completely unchanged;
        // only the earlier hard REJECTION is gone.
        var (_, sheet, anchor, ctx) = MakeLiveSpillSetup();
        var row2 = new CellAddress(sheet.Id, 2, 1); // A2 - covered, non-anchor
        var row3 = new CellAddress(sheet.Id, 3, 1); // A3

        var outcome = new ClearContentsCommand(sheet.Id, new GridRange(row2, row2)).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(row2).Should().Be(new NumberValue(2));
        // The anchor formula and the untouched sibling member survive -- nothing else changed.
        sheet.GetCell(anchor)!.FormulaText.Should().Be("SEQUENCE(3,1)");
        sheet.GetValue(row3).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void ClearContentsCommand_OnAnchorPlusPartialBody_IsAllowed_R123()
    {
        // R123-dynamic-spill-member-write: the anchor together with only SOME of the body (missing
        // A3) is likewise no longer a special "partial array" shape for a dynamic array -- clearing
        // the anchor removes the formula and its whole live spill (matching the anchor-alone case
        // above), so A3 ends up cleared too even though it wasn't in the selected range.
        var (_, sheet, anchor, ctx) = MakeLiveSpillSetup();
        var row2 = new CellAddress(sheet.Id, 2, 1); // A2
        var row3 = new CellAddress(sheet.Id, 3, 1); // A3
        var partialRange = new GridRange(anchor, row2); // A1:A2, missing A3

        var outcome = new ClearContentsCommand(sheet.Id, partialRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(anchor).Should().Be(BlankValue.Instance);
        sheet.GetValue(row2).Should().Be(BlankValue.Instance);
        sheet.GetValue(row3).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void ClearContentsCommand_OnWholeSpillRangeAsUnit_IsStillAllowed()
    {
        // Already-working case that must not regress: selecting the full spill footprint and
        // clearing it as a unit continues to work.
        var (_, sheet, anchor, ctx) = MakeLiveSpillSetup();
        var row3 = new CellAddress(sheet.Id, 3, 1); // A3
        var wholeRange = new GridRange(anchor, row3); // A1:A3

        var outcome = new ClearContentsCommand(sheet.Id, wholeRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
    }
}
