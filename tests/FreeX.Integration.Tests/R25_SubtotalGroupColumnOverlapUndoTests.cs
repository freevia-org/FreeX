using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for R25-undo-redo-newer-commands-1: Data &gt; Subtotal let a user check the
/// same column for both "At each change in" (the group-by column) and "Add subtotal to" (the
/// subtotal-sum columns). SubtotalCommand.ApplyInsertAndEdit always writes the group's "&lt;Label&gt;
/// Total" text into the group-by column first, then looped over every checked subtotal column
/// writing a SUBTOTAL formula — including, when the columns overlapped, a second edit at the
/// SAME address as the label. EditCellsCommand doesn't dedup by address, so it snapshotted the
/// label text (just written by the first edit) as the "old" value for the second, same-address
/// edit. On Revert, replaying the snapshot in the same forward order restored the true original
/// value first and then immediately clobbered it with that bogus "old" label text, permanently
/// corrupting the cell instead of restoring the pre-Apply state.
/// </summary>
public sealed class R25_SubtotalGroupColumnOverlapUndoTests
{
    private static (Workbook workbook, Sheet sheet) BuildFruitVegSheet()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Veg"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(5));
        return (workbook, sheet);
    }

    // The bug scenario: the group-by column (offset 0) is also checked as a subtotal column.
    [Fact]
    public void SubtotalCommand_GroupColumnAlsoCheckedAsSubtotalColumn_UndoFullyRestoresOriginalSheet()
    {
        var (workbook, sheet) = BuildFruitVegSheet();
        var context = new TestCommandContext(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));

        var command = new SubtotalCommand(
            sheet.Id,
            range,
            groupByColumnOffset: 0,
            subtotalColumnOffsets: [0u, 1u]);

        command.Apply(context).Success.Should().BeTrue();

        // The group column keeps its text label (not a second, overwriting SUBTOTAL formula) on
        // every total row, and the checked Amount column still gets its formula as usual.
        sheet.GetValue(4, 1).Should().Be(new TextValue("Fruit Total"));
        sheet.GetCell(4, 1)!.FormulaText.Should().BeNull();
        sheet.GetCell(4, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B2:B3)");
        sheet.GetValue(6, 1).Should().Be(new TextValue("Veg Total"));
        sheet.GetCell(6, 1)!.FormulaText.Should().BeNull();
        // The Veg group has one data row. Its SUBTOTAL formula is written referencing B4:B4 (Veg's
        // row at the time the Veg-total row is inserted), then the later Fruit-total insertion above
        // it shifts that reference down to B5:B5, matching where Veg's data row ends up for good.
        sheet.GetCell(6, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B5:B5)");
        sheet.GetValue(7, 1).Should().Be(new TextValue("Grand Total"));
        sheet.GetCell(7, 1)!.FormulaText.Should().BeNull();
        sheet.GetCell(7, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B2:B6)");

        command.Revert(context);

        // Undo must restore the sheet to exactly its pre-Apply state: no phantom "Total" label
        // left behind in column A, and every original cell back where it was.
        sheet.GetValue(1, 1).Should().Be(new TextValue("Category"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("Amount"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Fruit"));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(10));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Fruit"));
        sheet.GetValue(3, 2).Should().Be(new NumberValue(20));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Veg"));
        sheet.GetValue(4, 2).Should().Be(new NumberValue(5));
        sheet.GetCell(5, 1).Should().BeNull();
        sheet.GetCell(5, 2).Should().BeNull();
        sheet.GetCell(6, 1).Should().BeNull();
        sheet.GetCell(7, 1).Should().BeNull();

        // Redo (re-Apply after Undo) must reproduce the exact same result as the first Apply,
        // proving the round trip is stable rather than accumulating drift.
        command.Apply(context).Success.Should().BeTrue();

        sheet.GetValue(4, 1).Should().Be(new TextValue("Fruit Total"));
        sheet.GetCell(4, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B2:B3)");
        sheet.GetValue(6, 1).Should().Be(new TextValue("Veg Total"));
        sheet.GetCell(6, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B5:B5)");
        sheet.GetValue(7, 1).Should().Be(new TextValue("Grand Total"));
        sheet.GetCell(7, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B2:B6)");

        command.Revert(context);

        sheet.GetValue(4, 1).Should().Be(new TextValue("Veg"));
        sheet.GetCell(5, 1).Should().BeNull();
        sheet.GetCell(6, 1).Should().BeNull();
        sheet.GetCell(7, 1).Should().BeNull();
    }

    // Sibling/already-working case: the subtotal column list does NOT overlap the group-by
    // column (the ordinary case). This must keep behaving exactly as before the fix.
    [Fact]
    public void SubtotalCommand_NonOverlappingSubtotalColumn_StillWritesFormulaAndUndoRestores()
    {
        var (workbook, sheet) = BuildFruitVegSheet();
        var context = new TestCommandContext(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));

        var command = new SubtotalCommand(sheet.Id, range, groupByColumnOffset: 0, subtotalColumnOffset: 1);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetValue(4, 1).Should().Be(new TextValue("Fruit Total"));
        sheet.GetCell(4, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B2:B3)");
        sheet.GetValue(6, 1).Should().Be(new TextValue("Veg Total"));
        sheet.GetCell(6, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B5:B5)");
        sheet.GetValue(7, 1).Should().Be(new TextValue("Grand Total"));
        sheet.GetCell(7, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B2:B6)");

        command.Revert(context);

        sheet.GetValue(1, 1).Should().Be(new TextValue("Category"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Fruit"));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(10));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Fruit"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Veg"));
        sheet.GetValue(4, 2).Should().Be(new NumberValue(5));
        sheet.GetCell(5, 1).Should().BeNull();
        sheet.GetCell(6, 1).Should().BeNull();
        sheet.GetCell(7, 1).Should().BeNull();
    }
}
