using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for R21-undo-redo-deep-2: MoveRangeCommand.Apply correctly relocates a moved
/// dynamic array's live spill to the destination (R20-array-dynamic-spill-1), but Revert never
/// replayed the captured spill payload back at the original source address - it only restored the
/// anchor's formula cell via RestoreCellSnapshot, leaving the array's spilled members permanently
/// blank after Ctrl+Z instead of reappearing at the source the way they did before the move.
/// </summary>
public class R21_MoveRangeSpillUndoTests
{
    [Fact]
    public void Revert_AfterMovingSpillAnchor_RestoresSpillAtSourceAddress()
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

        var wholeSource = new GridRange(anchor, new CellAddress(sheet.Id, 3, 1)); // A1:A3
        var destination = new CellAddress(sheet.Id, 1, 4); // D1

        var command = new MoveRangeCommand(sheet.Id, wholeSource, destination);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Sanity: spill relocated to the destination (already covered by R20 tests).
        sheet.TryGetSpillExtent(new CellAddress(sheet.Id, 1, 4), out _, out _).Should().BeTrue();

        command.Revert(ctx);

        // The anchor's formula must be back at the source...
        sheet.GetCell(anchor)!.FormulaText.Should().Be("SEQUENCE(3,1)");

        // ...and (the bug) its spill must be re-established too, not left blank.
        sheet.TryGetSpillExtent(anchor, out var rows, out var cols).Should().BeTrue(
            "undo of a spill-anchor move must re-establish the spill at the restored source cell");
        rows.Should().Be(3u);
        cols.Should().Be(1u);
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1)); // A1 (anchor)
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2)); // A2 (re-spilled member)
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3)); // A3 (re-spilled member)

        // The destination must be fully vacated by the undo.
        sheet.TryGetSpillExtent(new CellAddress(sheet.Id, 1, 4), out _, out _).Should().BeFalse();
        sheet.GetValue(1, 4).Should().Be(BlankValue.Instance);
        sheet.GetValue(2, 4).Should().Be(BlankValue.Instance);
        sheet.GetValue(3, 4).Should().Be(BlankValue.Instance);
    }
}
