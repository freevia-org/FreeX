using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for round-11 bucket R9 finding R11-commands-undo-3: Fill Down/Up/Right/Left
/// and Autofill (drag fill-handle) must reject a fill that would overwrite only part of a
/// legacy CSE array range, matching Excel's "You cannot change part of an array" behavior. Before
/// the fix, neither FillCellsCommand.Apply nor AutofillCommand.Apply called
/// CommandGuards.RejectIfSplitsArray, so filling over a single non-anchor legacy-array member
/// silently overwrote it while leaving the spill anchor/extent bookkeeping pointing at a
/// now-inconsistent range.
///
/// R123-dynamic-spill-member-write later established that this rule does NOT apply to a live
/// DYNAMIC array's spill member (only legacy CSE arrays keep the whole-range restriction) --
/// filling/autofilling over a single dynamic-array spill member, leaving the rest of the array
/// untouched, is a normal allowed edit in real Excel. Both tests below were renamed from
/// "..._IsBlocked" and now assert the allowed outcome.
/// </summary>
public sealed class FreeXR11B9Tests
{
    [Fact]
    public void FillCellsCommand_OverSpillMember_IsAllowed_R123()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // A1 spills SEQUENCE(3) into A1:A3 (dynamic array anchor at A1, extent 3 rows x 1 col).
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(anchor, Cell.FromFormula("SEQUENCE(3)"));
        var spillValues = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) }
        };
        sheet.SetSpillRange(anchor, new RangeValue(spillValues)); // spills to A1:A3
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(99)));

        // User selects A1:B2 (only rows 1-2, NOT the full A1:A3 spill) and does Fill Down. The
        // fill target set is row 2 only: A2 and B2. A2 is a non-anchor member of the still-live
        // A1:A3 spill, but A3 (the other member) is outside the selection/target set entirely --
        // no longer rejected for a dynamic array (see class summary).
        var command = new FillCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            FillCellsDirection.Down);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        // Fill Down clones the source (A1's SEQUENCE formula cell) into A2 as real cell content --
        // it is no longer just a computed spill-overlay value.
        var filledMember = sheet.GetCell(new CellAddress(sheet.Id, 2, 1));
        filledMember.Should().NotBeNull();
        filledMember!.FormulaText.Should().Be("SEQUENCE(3)");
        // A3 (outside the fill's target set) is completely untouched.
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void AutofillCommand_OverSpillMember_IsAllowed_R123()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // A1 spills SEQUENCE(3) into A1:A3 (dynamic array anchor at A1, extent 3 rows x 1 col).
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(anchor, Cell.FromFormula("SEQUENCE(3)"));
        var spillValues = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) }
        };
        sheet.SetSpillRange(anchor, new RangeValue(spillValues)); // spills to A1:A3

        // User drags the fill handle inward: source A1:A3 (the whole spill) shrunk to fill range
        // A1:A2 -- ApplyInwardClear clears exactly the passed fillRange (A1:A2), which includes the
        // spill's own anchor (A1). No longer rejected for a dynamic array (see class summary); the
        // anchor being cleared naturally tears down the whole live spill (A2 directly, A3 via
        // SetCell's ClearSpillRange side effect), same as ClearContentsCommand's anchor-alone case.
        var command = new AutofillCommand(
            sheet.Id,
            new GridRange(anchor, new CellAddress(sheet.Id, 3, 1)),
            new GridRange(anchor, new CellAddress(sheet.Id, 2, 1)));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(1, 1).Should().Be(BlankValue.Instance);
        sheet.GetValue(2, 1).Should().Be(BlankValue.Instance);
        sheet.GetValue(3, 1).Should().Be(BlankValue.Instance);
    }
}
