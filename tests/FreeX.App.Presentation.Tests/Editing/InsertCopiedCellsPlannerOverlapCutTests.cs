using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Editing;

/// <summary>
/// R30-meta-2: "Insert Cut Cells" must clear the source range unconditionally when the cut source
/// overlaps the insertion destination, not just in the non-overlapping case. The clear runs BEFORE the
/// insert/shift and always targets the pre-shift (original) source coordinates, so an overlap between
/// source and destination can never collide with where the pasted cells land -- the overlap guard in
/// ClipboardPastePlanner.ShouldClearCutSourceAfterPaste exists for the in-place overwrite paste and does
/// not apply here.
/// </summary>
public sealed class InsertCopiedCellsPlannerOverlapCutTests
{
    [Fact]
    public void CreateCommand_Cut_OverlappingDestination_MovesDataOnceWithNoRemnant()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // Cut A1:A5 = 10/20/30/40/50.
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        var a4 = new CellAddress(sheet.Id, 4, 1);
        var a5 = new CellAddress(sheet.Id, 5, 1);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(a2, Cell.FromValue(new NumberValue(20)));
        sheet.SetCell(a3, Cell.FromValue(new NumberValue(30)));
        sheet.SetCell(a4, Cell.FromValue(new NumberValue(40)));
        sheet.SetCell(a5, Cell.FromValue(new NumberValue(50)));

        var source = new GridRange(a1, a5);
        var cells = new[]
        {
            (a1, sheet.GetCell(a1)!.Clone()),
            (a2, sheet.GetCell(a2)!.Clone()),
            (a3, sheet.GetCell(a3)!.Clone()),
            (a4, sheet.GetCell(a4)!.Clone()),
            (a5, sheet.GetCell(a5)!.Clone())
        };

        // Insert Cut Cells at A3, ShiftDown: destination (A3) overlaps the source range (A1:A5).
        var destination = new GridRange(a3, a3);

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook,
            sheet.Id,
            source,
            cells,
            destination,
            KeyboardInsertDeleteDialogChoice.ShiftDown,
            isCut: true);

        command.Apply(ctx).Success.Should().BeTrue();

        // Cut data landed once at the destination (moved, not duplicated).
        sheet.GetValue(a3).Should().Be(new NumberValue(10));
        sheet.GetValue(a4).Should().Be(new NumberValue(20));
        sheet.GetValue(a5).Should().Be(new NumberValue(30));
        sheet.GetValue(new CellAddress(sheet.Id, 6, 1)).Should().Be(new NumberValue(40));
        sheet.GetValue(new CellAddress(sheet.Id, 7, 1)).Should().Be(new NumberValue(50));

        // The un-shifted head of the source (A1:A2) must be cleared, not left with stale data.
        sheet.GetValue(a1).Should().Be(BlankValue.Instance);
        sheet.GetValue(a2).Should().Be(BlankValue.Instance);

        // No remnant of the pre-clear source surviving the shift further down the column.
        sheet.GetValue(new CellAddress(sheet.Id, 8, 1)).Should().Be(BlankValue.Instance);
        sheet.GetValue(new CellAddress(sheet.Id, 9, 1)).Should().Be(BlankValue.Instance);
        sheet.GetValue(new CellAddress(sheet.Id, 10, 1)).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void CreateCommand_Cut_NonOverlappingDestination_StillMovesData()
    {
        // Sibling already-working case (R29-undo-redo-remaining-deep-1): a non-overlapping cut-insert
        // must keep clearing the source exactly as before this fix.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(a2, Cell.FromValue(new NumberValue(20)));
        sheet.SetCell(a3, Cell.FromValue(new NumberValue(30)));
        var source = new GridRange(a1, a3);

        var c1 = new CellAddress(sheet.Id, 1, 3);
        var destination = new GridRange(c1, c1);
        var cells = new[]
        {
            (a1, sheet.GetCell(a1)!.Clone()),
            (a2, sheet.GetCell(a2)!.Clone()),
            (a3, sheet.GetCell(a3)!.Clone())
        };

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook, sheet.Id, source, cells, destination,
            KeyboardInsertDeleteDialogChoice.ShiftRight, isCut: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(c1).Should().Be(new NumberValue(10));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new NumberValue(20));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 3)).Should().Be(new NumberValue(30));

        sheet.GetValue(a1).Should().Be(BlankValue.Instance);
        sheet.GetValue(a2).Should().Be(BlankValue.Instance);
        sheet.GetValue(a3).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void CreateCommand_Copy_OverlappingDestination_LeavesSourceIntact()
    {
        // Sibling already-working case: a plain copy (isCut=false) must never clear the source, even
        // when the destination overlaps it.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(a2, Cell.FromValue(new NumberValue(20)));
        sheet.SetCell(a3, Cell.FromValue(new NumberValue(30)));
        var source = new GridRange(a1, a3);

        var destination = new GridRange(a2, a2);
        var cells = new[]
        {
            (a1, sheet.GetCell(a1)!.Clone()),
            (a2, sheet.GetCell(a2)!.Clone()),
            (a3, sheet.GetCell(a3)!.Clone())
        };

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook, sheet.Id, source, cells, destination,
            KeyboardInsertDeleteDialogChoice.ShiftDown, isCut: false);

        command.Apply(ctx).Success.Should().BeTrue();

        // Source untouched -- a genuine copy, so the values are duplicated rather than moved.
        sheet.GetValue(a1).Should().Be(new NumberValue(10));
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
