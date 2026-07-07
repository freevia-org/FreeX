using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for round-11 bucket R9 finding R11-commands-undo-3: Fill Down/Up/Right/Left
/// and Autofill (drag fill-handle) must reject a fill that would overwrite only part of a
/// dynamic-array spill (or legacy CSE array) range, matching Excel's "You cannot change part of an
/// array" behavior. Before the fix, neither FillCellsCommand.Apply nor AutofillCommand.Apply called
/// CommandGuards.RejectIfSplitsArray, so filling over a single non-anchor spill member silently
/// overwrote it while leaving the spill anchor/extent bookkeeping pointing at a now-inconsistent
/// range.
/// </summary>
public sealed class FreeXR11B9Tests
{
    [Fact]
    public void FillCellsCommand_OverSpillMember_IsBlocked()
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
        // exactly the "changing part of an array" case Excel rejects.
        var command = new FillCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            FillCellsDirection.Down);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("You cannot change part of an array.");
        // The spill members must be untouched - no silent corruption of the array.
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void AutofillCommand_OverSpillMember_IsBlocked()
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
        // A1:A2. Excel's inward-drag gesture clears the cells beyond the new boundary (A3 alone),
        // which would split the still-live A1:A3 spill (A1/A2 kept, A3 cleared in isolation).
        // This exercises the same missing-guard bug via AutofillCommand's ApplyInwardClear path.
        var command = new AutofillCommand(
            sheet.Id,
            new GridRange(anchor, new CellAddress(sheet.Id, 3, 1)),
            new GridRange(anchor, new CellAddress(sheet.Id, 2, 1)));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("You cannot change part of an array.");
        // The spill members must be untouched - no silent corruption of the array.
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
    }
}
