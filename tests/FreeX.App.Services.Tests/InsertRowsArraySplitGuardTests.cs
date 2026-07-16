using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R46-commands-insert-delete-shift-2-1: whole-row Insert never guarded against splitting a legacy
/// CSE array or a live dynamic-array spill the way the sibling band-scoped Insert/Delete Cells command
/// already does (via CommandGuards.RejectIfSplitsArray). Inserting a row through the middle of a
/// spill's own row extent (e.g. B5 = SEQUENCE(3,1) spilling B5:B7, inserting at row 6) let the insert
/// silently succeed: the anchor stayed at its own row (untouched, since it sits above the insert
/// point) while nothing relocated or protected the still-live spill member rows, desyncing the array
/// from the rest of the sheet's shifted row numbering with no error — unlike real Excel, which refuses
/// with "You cannot change part of an array."
/// </summary>
public sealed class InsertRowsArraySplitGuardTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void InsertRows_ThroughMiddleOfDynamicArraySpill_IsRejected()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 5, 2); // B5
        sheet.SetFormula(anchor, "SEQUENCE(3,1)");
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        var spillCells = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        };
        sheet.SetSpillRange(anchor, new RangeValue(spillCells)); // spills B5:B7 = 1,2,3
        var ctx = new TestCommandContext(workbook);

        // Insert point (row 6) falls strictly inside the spill's own row extent (5..7) — some of the
        // array's rows (5) are above the insert point, some (6,7) are at/below it.
        var outcome = new InsertRowsCommand(sheet.Id, beforeRow: 6, count: 1).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("You cannot change part of an array.");

        // The array must be completely untouched — no partial/silent insert.
        sheet.TryGetSpillExtent(anchor, out var rows, out var cols).Should().BeTrue();
        rows.Should().Be(3u);
        cols.Should().Be(1u);
        sheet.GetValue(5, 2).Should().Be(new NumberValue(1));
        sheet.GetValue(6, 2).Should().Be(new NumberValue(2));
        sheet.GetValue(7, 2).Should().Be(new NumberValue(3));
    }

    // Sibling no-regression check: an insert entirely ABOVE the spill's row extent must still succeed
    // and relocate the whole array as one unit (the already-correct case this guard must not break).
    [Fact]
    public void InsertRows_AboveDynamicArraySpill_StillSucceedsAndRelocatesSpill()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 5, 2); // B5
        sheet.SetFormula(anchor, "SEQUENCE(3,1)");
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        var spillCells = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        };
        sheet.SetSpillRange(anchor, new RangeValue(spillCells)); // spills B5:B7 = 1,2,3
        var ctx = new TestCommandContext(workbook);

        // Insert a row above row 5 (unrelated to the array) — the whole array shifts down as a unit.
        var outcome = new InsertRowsCommand(sheet.Id, beforeRow: 2, count: 1).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var newAnchor = new CellAddress(sheet.Id, 6, 2); // B6
        sheet.TryGetSpillExtent(newAnchor, out var rows, out var cols).Should().BeTrue(
            "the relocated formula cell must still be recognised as a live spill anchor");
        rows.Should().Be(3u);
        cols.Should().Be(1u);
        sheet.TryGetSpillExtent(anchor, out _, out _).Should().BeFalse(
            "the vacated old anchor address must not still claim to own the spill");

        sheet.GetValue(6, 2).Should().Be(new NumberValue(1));
        sheet.GetValue(7, 2).Should().Be(new NumberValue(2));
        sheet.GetValue(8, 2).Should().Be(new NumberValue(3));
    }
}
