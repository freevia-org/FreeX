using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for round-11 bucket R3 finding R11-commands-undo-1: Paste Special must
/// reject a paste that would overwrite only part of a legacy CSE array range, matching Excel's
/// "You cannot change part of an array" behavior. Before the fix, PasteSpecialCellsCommand.Apply
/// never called CommandGuards.RejectIfSplitsArray, so pasting into a single non-anchor
/// legacy-array member silently overwrote it while leaving the spill anchor/extent bookkeeping
/// pointing at a now-inconsistent range.
///
/// R123-dynamic-spill-member-write later established that this rule does NOT apply to a live
/// DYNAMIC array's spill member (only legacy CSE arrays keep the whole-range restriction) --
/// Paste Special over a single dynamic-array spill member, leaving the rest of the array
/// untouched, is a normal allowed edit in real Excel. Renamed from "..._IsBlocked" below and now
/// asserts the allowed outcome.
/// </summary>
public sealed class FreeXR11B3Tests
{
    [Fact]
    public void PasteSpecialCellsCommand_OnSingleSpillMember_IsAllowed_R123()
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

        // User copies a single unrelated cell and Paste-Specials into A2 (a non-anchor spill
        // member) -- no longer rejected for a dynamic array (see class summary).
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

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        // A plain (non-arithmetic) Paste Special replaces the destination's value outright with the
        // source's -- the spill member is real cell content now, not a spill-overlay value.
        sheet.GetValue(member).Should().Be(new NumberValue(999));
        // The rest of the array -- the anchor's formula and the untouched sibling member -- survives.
        sheet.GetCell(anchor)!.FormulaText.Should().Be("SEQUENCE(3)");
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
    }
}
