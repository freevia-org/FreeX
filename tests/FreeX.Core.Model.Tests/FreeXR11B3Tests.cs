using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for round-11 bucket R3 finding R11-commands-undo-1: Paste Special must
/// reject a paste that would overwrite only part of a dynamic-array spill (or legacy CSE array)
/// range, matching Excel's "You cannot change part of an array" behavior. Before the fix,
/// PasteSpecialCellsCommand.Apply never called CommandGuards.RejectIfSplitsArray, so pasting into
/// a single non-anchor spill member silently overwrote it while leaving the spill anchor/extent
/// bookkeeping pointing at a now-inconsistent range.
/// </summary>
public sealed class FreeXR11B3Tests
{
    [Fact]
    public void PasteSpecialCellsCommand_OnSingleSpillMember_IsBlocked()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // A1 spills SEQUENCE(3) into A1:A1..A3 (dynamic array anchor at A1, extent 3 rows x 1 col).
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(anchor, Cell.FromFormula("SEQUENCE(3)"));
        var spillValues = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) }
        };
        sheet.SetSpillRange(anchor, new RangeValue(spillValues)); // spills to A1:A3

        // User copies a single unrelated cell and Paste-Specials into A2 (a non-anchor spill member).
        var member = new CellAddress(sheet.Id, 2, 1); // A2
        var source = new[]
        {
            (new CellAddress(sheet.Id, 10, 10), Cell.FromValue(new NumberValue(999)))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 10, 10), new CellAddress(sheet.Id, 10, 10)),
            source,
            member,
            new PasteSpecialOptions());

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("You cannot change part of an array.");
        // The spill member must be untouched - no silent corruption of the array.
        sheet.GetValue(member).Should().Be(new NumberValue(2));
    }
}
