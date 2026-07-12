using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Editing;

/// <summary>
/// R29-undo-redo-remaining-deep-1: "Insert Cut Cells" must MOVE the source data (clear it once the
/// shifted-in paste lands), not silently duplicate it the way a plain "Insert Copied Cells" does.
/// </summary>
public sealed class InsertCopiedCellsPlannerCutTests
{
    [Fact]
    public void CreateCommand_Cut_ClearsSourceRange_InsteadOfDuplicatingData()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // A1:A3 = 10/20/30 (the exact repro from the finding).
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(a2, Cell.FromValue(new NumberValue(20)));
        sheet.SetCell(a3, Cell.FromValue(new NumberValue(30)));

        var source = new GridRange(a1, a3);

        // Existing data at C1:C3 that must shift out of the way (right, into D1:D3).
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var c2 = new CellAddress(sheet.Id, 2, 3);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(c1, Cell.FromValue(new TextValue("oldC1")));
        sheet.SetCell(c2, Cell.FromValue(new TextValue("oldC2")));
        sheet.SetCell(c3, Cell.FromValue(new TextValue("oldC3")));

        var destination = new GridRange(c1, c1);
        var cells = new[]
        {
            (a1, sheet.GetCell(a1)!.Clone()),
            (a2, sheet.GetCell(a2)!.Clone()),
            (a3, sheet.GetCell(a3)!.Clone())
        };

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook,
            sheet.Id,
            source,
            cells,
            destination,
            KeyboardInsertDeleteDialogChoice.ShiftRight,
            isCut: true);

        command.Apply(ctx).Success.Should().BeTrue();

        // Destination correctly received the cut values, and the bumped-out old C cells moved right.
        sheet.GetValue(c1).Should().Be(new NumberValue(10));
        sheet.GetValue(c2).Should().Be(new NumberValue(20));
        sheet.GetValue(c3).Should().Be(new NumberValue(30));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 4)).Should().Be(new TextValue("oldC1"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 4)).Should().Be(new TextValue("oldC2"));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 4)).Should().Be(new TextValue("oldC3"));

        // The bug: the source range must be cleared (data MOVED, not duplicated).
        sheet.GetValue(a1).Should().Be(BlankValue.Instance);
        sheet.GetValue(a2).Should().Be(BlankValue.Instance);
        sheet.GetValue(a3).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void CreateCommand_Cut_UndoRestoresSourceAndDestination()
    {
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
        var c2 = new CellAddress(sheet.Id, 2, 3);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(c1, Cell.FromValue(new TextValue("oldC1")));
        sheet.SetCell(c2, Cell.FromValue(new TextValue("oldC2")));
        sheet.SetCell(c3, Cell.FromValue(new TextValue("oldC3")));

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
        command.Revert(ctx);

        // A clean Apply -> Undo round trip must restore the pre-insert state exactly, including the
        // source range that Apply cleared (the composite must not leave a permanent, unrecoverable
        // clear behind after undo).
        sheet.GetValue(a1).Should().Be(new NumberValue(10));
        sheet.GetValue(a2).Should().Be(new NumberValue(20));
        sheet.GetValue(a3).Should().Be(new NumberValue(30));
        sheet.GetValue(c1).Should().Be(new TextValue("oldC1"));
        sheet.GetValue(c2).Should().Be(new TextValue("oldC2"));
        sheet.GetValue(c3).Should().Be(new TextValue("oldC3"));
    }

    [Fact]
    public void CreateCommand_Copy_LeavesSourceRangeIntact()
    {
        // Sibling already-working case: an ordinary "Insert Copied Cells" (not cut) must keep
        // duplicating the source data on both sides -- isCut defaults to false/is explicitly false,
        // so no clear is appended and the pre-existing copy behavior is unchanged.
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
            KeyboardInsertDeleteDialogChoice.ShiftRight, isCut: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(c1).Should().Be(new NumberValue(10));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new NumberValue(20));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 3)).Should().Be(new NumberValue(30));

        // Source is untouched -- this is a genuine copy, so duplication here is correct.
        sheet.GetValue(a1).Should().Be(new NumberValue(10));
        sheet.GetValue(a2).Should().Be(new NumberValue(20));
        sheet.GetValue(a3).Should().Be(new NumberValue(30));
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
