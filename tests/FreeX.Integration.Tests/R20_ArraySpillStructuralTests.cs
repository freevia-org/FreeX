using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for the R20 array/dynamic-array-spill structural-edit findings:
///
/// R20-array-dynamic-spill-1: Insert/Delete Rows or Columns (and a range Move) used to silently
/// destroy a dynamic-array spill instead of relocating it — the anchor's Cell object moved via
/// ClearCell+SetCell, but nothing re-established the spill (_spillAnchors/_spillValues) at the new
/// address, so the array permanently collapsed to a stale scalar with a blank spill area.
///
/// R20-array-dynamic-spill-2: Sheet.IsSpillBlocked never consulted Sheet.StructuredTables, so a
/// dynamic array could silently spill straight through (and overwrite) an Excel Table's blank body
/// cells instead of yielding #SPILL! like Excel does.
///
/// R20-array-dynamic-spill-3: MoveRangeCommand omitted the CommandGuards.RejectIfSplitsArray guard
/// that every sibling mutating command (Copy/Paste/Autofill/ClearContents/Fill) applies, so
/// dragging/cutting only part of a spill array was silently discarded instead of rejected.
/// </summary>
public class R20_array_spill_Tests
{
    [Fact]
    public void DeleteRows_AboveSpillAnchor_RelocatesSpillInsteadOfCollapsingIt()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
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
        var ctx = new TestCommandContext(wb);

        // Delete row 2 - unrelated to the formula, above the anchor. Shifts B5 up to B4.
        var outcome = new DeleteRowsCommand(sheet.Id, startRow: 2, count: 1).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var newAnchor = new CellAddress(sheet.Id, 4, 2); // B4
        var oldAnchor = new CellAddress(sheet.Id, 5, 2); // stale slot must no longer be an anchor

        sheet.TryGetSpillExtent(newAnchor, out var rows, out var cols).Should().BeTrue(
            "the relocated formula cell must still be recognised as a live spill anchor");
        rows.Should().Be(3u);
        cols.Should().Be(1u);
        sheet.TryGetSpillExtent(oldAnchor, out _, out _).Should().BeFalse(
            "the vacated old anchor address must not still claim to own the spill");

        sheet.GetValue(4, 2).Should().Be(new NumberValue(1)); // B4 (anchor) keeps its own value
        sheet.GetValue(5, 2).Should().Be(new NumberValue(2)); // B5 (re-spilled member)
        sheet.GetValue(6, 2).Should().Be(new NumberValue(3)); // B6 (re-spilled member)
    }

    [Fact]
    public void Spill_OverrunningStructuredTable_YieldsSpillErrorInsteadOfCorruptingTable()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // Table1 spans B2:C10 (header row B2:C2, blank body rows below).
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 10, 3)),
        };
        sheet.StructuredTables.Add(table);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Col1")); // B2 header
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("Col2")); // C2 header

        // A1 = SEQUENCE(10,3): a 10x3 array anchored at A1 would spill A1:C10, overlapping the
        // table's B2:C10 footprint through several currently-blank table body cells.
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "SEQUENCE(10,3)");

        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(wb);

        sheet.GetValue(1, 1).Should().Be(ErrorValue.Spill, "Excel refuses to spill through a Table");
        sheet.TryGetSpillExtent(new CellAddress(sheet.Id, 1, 1), out _, out _).Should().BeFalse(
            "a blocked spill must not register a live spill anchor");

        // The table's blank body cells must remain untouched - not silently overwritten with
        // computed spill numbers.
        sheet.GetValue(3, 2).Should().Be(BlankValue.Instance);
        sheet.GetValue(3, 3).Should().Be(BlankValue.Instance);
        // Header text must survive too.
        sheet.GetValue(2, 2).Should().Be(new TextValue("Col1"));
        sheet.GetValue(2, 3).Should().Be(new TextValue("Col2"));
    }

    [Fact]
    public void MoveRangeCommand_MovingOnlyPartOfSpillArray_IsRejected()
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

        var member = new CellAddress(sheet.Id, 2, 1); // A2 - non-anchor spill member, not the whole array
        var destination = new CellAddress(sheet.Id, 2, 4); // D2

        var outcome = new MoveRangeCommand(sheet.Id, new GridRange(member, member), destination).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("You cannot change part of an array.");
        // The array must be untouched - no silent partial move/discard.
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2)); // A2 still holds its spill value
        sheet.GetValue(2, 4).Should().Be(BlankValue.Instance); // D2 must remain untouched
    }

    [Fact]
    public void MoveRangeCommand_MovingWholeSpillArrayAsUnit_RelocatesSpillAndIsAllowed()
    {
        // Companion happy-path check: moving the entire array (anchor + all members) together must
        // still be allowed, and (R20-array-dynamic-spill-1) the spill must relocate with it rather
        // than collapsing to a stale scalar at the destination.
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

        var outcome = new MoveRangeCommand(sheet.Id, wholeSource, destination).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var newAnchor = new CellAddress(sheet.Id, 1, 4); // D1
        sheet.TryGetSpillExtent(newAnchor, out var rows, out var cols).Should().BeTrue();
        rows.Should().Be(3u);
        cols.Should().Be(1u);
        sheet.GetValue(1, 4).Should().Be(new NumberValue(1)); // D1
        sheet.GetValue(2, 4).Should().Be(new NumberValue(2)); // D2
        sheet.GetValue(3, 4).Should().Be(new NumberValue(3)); // D3

        // Old source addresses must be vacated.
        sheet.TryGetSpillExtent(anchor, out _, out _).Should().BeFalse();
        sheet.GetValue(1, 1).Should().Be(BlankValue.Instance);
        sheet.GetValue(2, 1).Should().Be(BlankValue.Instance);
        sheet.GetValue(3, 1).Should().Be(BlankValue.Instance);
    }
}
