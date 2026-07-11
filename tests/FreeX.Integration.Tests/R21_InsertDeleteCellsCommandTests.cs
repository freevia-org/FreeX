using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for the round-21 InsertDeleteCellsCommand (Insert/Delete Cells with a
/// shift-right/left/up/down, as opposed to whole-row/whole-column insert/delete) findings:
///
/// R21-undo-redo-deep-1: Insert/Delete Cells never relocated a spilling dynamic array's footprint
/// to its shifted address (the anchor formula moved, but the spill members stayed behind and were
/// torn down), and AffectedCells never included the shifted-to address, so a moved spill anchor
/// whose formula text is unchanged by the shift (e.g. SEQUENCE with no cell references) was never
/// queued for recalculation. Undo had the identical gap.
///
/// R21-defined-name-management-1: Insert/Delete Cells (band-scoped shift, not a whole row/column)
/// never shifted plain (GridRange-backed) named ranges in workbook.NamedRanges/ScopedNamedRanges,
/// leaving a name's RefersTo stale after the edit.
///
/// R21-defined-name-management-3: Delete Cells (Shift Up/Left) left a name pointing only at the
/// deleted cells silently stale instead of at least dropping it (Excel would show #REF!; GridRange
/// cannot represent that sentinel here - see Workbook.cs's RemoveNamedRangesForSheet - so dropping
/// the name is the closest matching behavior already used elsewhere in this codebase).
/// </summary>
public class R21_InsertDeleteCellsCommandTests
{
    [Fact]
    public void InsertCellsShiftDown_RelocatesSpillingArrayAndQueuesNewAnchorForRecalc()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetFormula(anchor, "SEQUENCE(3,1)");
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        var cells = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        };
        sheet.SetSpillRange(anchor, new RangeValue(cells)); // spills A1:A3 = 1,2,3
        var ctx = new TestCommandContext(wb);

        var insertRange = new GridRange(anchor, anchor); // A1:A1
        var command = new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Down);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var newAnchor = new CellAddress(sheet.Id, 2, 1); // A2 (A1's formula shifted down by 1 row)
        sheet.TryGetSpillExtent(newAnchor, out var rows, out var cols).Should().BeTrue(
            "the relocated formula cell must still be recognised as a live spill anchor");
        rows.Should().Be(3u);
        cols.Should().Be(1u);
        sheet.GetValue(2, 1).Should().Be(new NumberValue(1)); // A2 (anchor keeps its own cached value)
        sheet.GetValue(3, 1).Should().Be(new NumberValue(2)); // A3 (re-spilled member)
        sheet.GetValue(4, 1).Should().Be(new NumberValue(3)); // A4 (re-spilled member)

        // The moved anchor must be queued for recalculation even though SEQUENCE's formula text is
        // unchanged by the shift (RewriteAllFormulas alone would never record it).
        outcome.AffectedCells.Should().Contain(newAnchor);

        command.Revert(ctx);

        sheet.GetCell(anchor)!.FormulaText.Should().Be("SEQUENCE(3,1)");
        sheet.TryGetSpillExtent(anchor, out var undoRows, out var undoCols).Should().BeTrue(
            "undo of a spill-anchor shift must re-establish the spill at the restored (pre-Apply) address");
        undoRows.Should().Be(3u);
        undoCols.Should().Be(1u);
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));

        // The vacated post-Apply address must no longer claim to own a spill.
        sheet.TryGetSpillExtent(newAnchor, out _, out _).Should().BeFalse();
        sheet.GetValue(4, 1).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void InsertCellsShiftDown_ShiftsPlainNamedRangeAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var dataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)); // A1:A5
        wb.DefineNamedRange("Data", dataRange);
        var ctx = new TestCommandContext(wb);

        var insertRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)); // A1:A1
        var command = new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Down);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        wb.TryGetNamedRange("Data", out var shifted).Should().BeTrue();
        shifted.Should().Be(new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 6, 1)),
            "Data's RefersTo must track the moved cells, matching Excel, instead of staying at the stale $A$1:$A$5");

        command.Revert(ctx);

        wb.TryGetNamedRange("Data", out var restored).Should().BeTrue();
        restored.Should().Be(dataRange);
    }

    [Fact]
    public void DeleteCellsShiftUp_RemovesNamedRangeFullyInsideDeletedCellsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var totalRange = new GridRange(new CellAddress(sheet.Id, 5, 2), new CellAddress(sheet.Id, 5, 2)); // B5
        wb.DefineNamedRange("Total", totalRange);
        var ctx = new TestCommandContext(wb);

        var deleteRange = new GridRange(new CellAddress(sheet.Id, 5, 2), new CellAddress(sheet.Id, 5, 2)); // B5:B5
        var command = new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Up);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Excel would turn a name pointing only at deleted cells into #REF!; GridRange cannot
        // represent that sentinel here (see Workbook.cs's RemoveNamedRangesForSheet, which drops a
        // dangling range for the same reason), so the name must be dropped instead of silently left
        // pointing at whatever now occupies the old address (B6's former content, shifted up).
        wb.TryGetNamedRange("Total", out _).Should().BeFalse(
            "a name pointing only at deleted cells must not silently keep resolving to the wrong data");

        command.Revert(ctx);

        wb.TryGetNamedRange("Total", out var restored).Should().BeTrue();
        restored.Should().Be(totalRange);
    }
}
