using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R119-commands-undo-byte-budget-1: CommandBus's 50 MB undo byte-budget
/// (CommandBus.MaxUndoByteBudget) is driven entirely by <see cref="IEstimatesMemory.EstimatedBytes"/>
/// -- a command that does not implement <see cref="IEstimatesMemory"/> is billed a flat 200-byte
/// default (CommandBus.DefaultCommandBytes) regardless of how much data its undo snapshot actually
/// retains. Before this fix, PasteCellsCommand, GroupedEditCellsCommand, SortCommand and
/// RemoveSheetCommand all held per-cell (or, for RemoveSheetCommand, whole-sheet) undo snapshots
/// but did not implement IEstimatesMemory, so the byte-budget branch of
/// UndoRedoStack.TrimUndoStack (`_undoStackBytes > _maxBytes`) could essentially never fire for
/// them -- only the 100-entry depth cap bounded the undo stack, even for full-sheet deletes or
/// 100k-cell pastes that can retain tens to hundreds of MB each.
/// </summary>
public sealed class R119_UndoByteBudgetRealCommandsTests
{
    private static readonly WorkbookId WbId = WorkbookId.New();

    private static CommandBus MakeBus(Workbook wb) => new(_ => new TestCommandContext(wb));

    private static List<(CellAddress Address, Cell Cell)> MakeCells(SheetId sheetId, int count)
    {
        var cells = new List<(CellAddress, Cell)>(count);
        for (var i = 0; i < count; i++)
        {
            var row = (uint)(i / 200) + 1;
            var col = (uint)(i % 200) + 1;
            cells.Add((new CellAddress(sheetId, row, col), Cell.FromValue(new NumberValue(i))));
        }
        return cells;
    }

    // ── PasteCellsCommand ──────────────────────────────────────────────────────

    [Fact]
    public void R119_PasteCellsCommand_ImplementsIEstimatesMemory_ScalingWithCellCount()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        IWorkbookCommand command = new PasteCellsCommand(sheet.Id, MakeCells(sheet.Id, 1_000));

        // `as` is used (rather than a direct cast) so this assertion still COMPILES against the
        // pre-fix source (where PasteCellsCommand does not implement IEstimatesMemory) and instead
        // fails at runtime with a null estimator -- the fail-before/after technique this round
        // requires, without a build break masking the behavioural gap.
        var estimator = command as IEstimatesMemory;

        estimator.Should().NotBeNull("PasteCellsCommand's undo snapshot is per-cell and must report its real size");
        estimator!.EstimatedBytes.Should().Be(1_000 * 300);
    }

    [Fact]
    public void R119_CommandBus_PasteCellsCommand_ByteBudgetEvictsOldestWhenRealSizeExceedsBudget()
    {
        // Two real PasteCellsCommand pushes of 90,000 cells each: at 300 bytes/cell that is
        // 27,000,000 bytes (~27 MB) per command. A single push is under the 50 MB budget, but two
        // in a row total 54,000,000 bytes, tripping MaxUndoByteBudget (52,428,800) and forcing the
        // oldest entry off the stack. Before this fix (flat 200-byte default) two such commands
        // would total only 400 bytes -- nowhere near the budget -- and BOTH would remain undoable.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var bus = MakeBus(wb);

        var cellsA = MakeCells(sheet.Id, 90_000);
        var cellsB = MakeCells(sheet.Id, 90_000);

        bus.Execute(WbId, new PasteCellsCommand(sheet.Id, cellsA)).Success.Should().BeTrue();
        bus.Execute(WbId, new PasteCellsCommand(sheet.Id, cellsB)).Success.Should().BeTrue();

        var undoCount = 0;
        while (bus.CanUndo(WbId))
        {
            bus.Undo(WbId).Success.Should().BeTrue();
            undoCount++;
        }

        undoCount.Should().Be(1,
            "the real ~27 MB-per-command estimate should trip the 50 MB budget after the second " +
            "push and evict the oldest paste, leaving only the newest undoable");
    }

    [Fact]
    public void R119_CommandBus_PasteCellsCommand_SmallPasteStaysUndoable_NoRegression()
    {
        // Sibling/no-regression coverage: an everyday small paste (well under the byte budget on
        // its own -- 50 cells * 300 bytes = 15,000 bytes) must NOT be prematurely evicted. This
        // guards against an over-aggressive per-cell constant swallowing normal small edits.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var bus = MakeBus(wb);

        bus.Execute(WbId, new PasteCellsCommand(sheet.Id, MakeCells(sheet.Id, 50))).Success.Should().BeTrue();

        bus.CanUndo(WbId).Should().BeTrue("a small paste is nowhere near the 50 MB budget and must remain undoable");
        bus.Undo(WbId).Success.Should().BeTrue();
        bus.CanUndo(WbId).Should().BeFalse();
    }

    // ── GroupedEditCellsCommand ────────────────────────────────────────────────

    [Fact]
    public void R119_GroupedEditCellsCommand_ImplementsIEstimatesMemory_ScalingWithSheetsTimesEdits()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        var edits = new List<(CellAddress Address, Cell NewCell)>();
        for (uint i = 1; i <= 500; i++)
            edits.Add((new CellAddress(sheet1.Id, i, 1), Cell.FromValue(new NumberValue(i))));

        IWorkbookCommand command = new GroupedEditCellsCommand([sheet1.Id, sheet2.Id], sheet1.Id, edits);

        var estimator = command as IEstimatesMemory;

        estimator.Should().NotBeNull("the grouped-edit undo snapshot is per-cell per-sheet and must report its real size");
        estimator!.EstimatedBytes.Should().Be(2 * 500 * 300);
    }

    // ── SortCommand ────────────────────────────────────────────────────────────

    [Fact]
    public void R119_SortCommand_ImplementsIEstimatesMemory_ScalingWithRangeCellCount()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 100, 100));

        IWorkbookCommand command = new SortCommand(sheet.Id, range, sortByColOffset: 0, ascending: true);

        var estimator = command as IEstimatesMemory;

        estimator.Should().NotBeNull("SortCommand's undo snapshot captures a full payload per cell in the sorted range");
        estimator!.EstimatedBytes.Should().Be(100 * 100 * 400);
    }

    [Fact]
    public void R119_CommandBus_SortCommand_ByteBudgetEvictsOldestWhenRealSizeExceedsBudget()
    {
        // 300x300 = 90,000 cells * 400 bytes/cell = 36,000,000 bytes (~34 MB) per sort. Two sorts
        // of the same large range back-to-back total ~68 MB, exceeding the 50 MB budget and
        // evicting the oldest -- before this fix, two such sorts would total only 400 bytes.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 300, 300));
        var bus = MakeBus(wb);

        bus.Execute(WbId, new SortCommand(sheet.Id, range, sortByColOffset: 0, ascending: true)).Success.Should().BeTrue();
        bus.Execute(WbId, new SortCommand(sheet.Id, range, sortByColOffset: 0, ascending: false)).Success.Should().BeTrue();

        var undoCount = 0;
        while (bus.CanUndo(WbId))
        {
            bus.Undo(WbId).Success.Should().BeTrue();
            undoCount++;
        }

        undoCount.Should().Be(1,
            "the real ~34 MB-per-sort estimate should trip the 50 MB budget after the second sort " +
            "and evict the oldest, leaving only the newest undoable");
    }

    // ── RemoveSheetCommand ─────────────────────────────────────────────────────

    [Fact]
    public void R119_RemoveSheetCommand_ImplementsIEstimatesMemory_ScalingWithOccupiedCellCountAfterApply()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var keep = wb.AddSheet("Sheet2"); // a second sheet so the delete is legal
        var ctx = new TestCommandContext(wb);

        for (uint i = 1; i <= 250; i++)
            sheet.SetCell(new CellAddress(sheet.Id, i, 1), Cell.FromValue(new NumberValue(i)));

        IWorkbookCommand command = new RemoveSheetCommand(sheet.Id);
        var estimator = command as IEstimatesMemory;

        estimator.Should().NotBeNull("RemoveSheetCommand retains the entire deleted Sheet for undo and must report its real size");

        // Before Apply, nothing has been captured yet -- must not fabricate a nonzero estimate.
        estimator!.EstimatedBytes.Should().Be(0);

        command.Apply(ctx).Success.Should().BeTrue();

        estimator.EstimatedBytes.Should().Be(250 * 200,
            "the removed sheet retains its full 250-cell contents for undo and must be billed accordingly");
    }

    [Fact]
    public void R119_CommandBus_RemoveSheetCommand_ByteBudgetEvictsOldestWhenRealSizeExceedsBudget()
    {
        // Two large-sheet deletes (~150,000 populated cells * 200 bytes = 30,000,000 bytes/~29 MB
        // each) exceed the 50 MB budget together, so the oldest full-sheet snapshot is evicted --
        // before this fix each RemoveSheetCommand would be billed only 200 bytes regardless of how
        // many cells the deleted sheet held.
        var wb = new Workbook("test");
        var sheetA = wb.AddSheet("A");
        var sheetB = wb.AddSheet("B");
        var keep = wb.AddSheet("Keep");
        var bus = MakeBus(wb);

        const int cellsPerSheet = 150_000;
        void Populate(Sheet sheet)
        {
            for (var i = 0; i < cellsPerSheet; i++)
            {
                var row = (uint)(i / 500) + 1;
                var col = (uint)(i % 500) + 1;
                sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(new NumberValue(i)));
            }
        }
        Populate(sheetA);
        Populate(sheetB);

        bus.Execute(WbId, new RemoveSheetCommand(sheetA.Id)).Success.Should().BeTrue();
        bus.Execute(WbId, new RemoveSheetCommand(sheetB.Id)).Success.Should().BeTrue();

        var undoCount = 0;
        while (bus.CanUndo(WbId))
        {
            bus.Undo(WbId).Success.Should().BeTrue();
            undoCount++;
        }

        undoCount.Should().Be(1,
            "the real ~29 MB-per-deleted-sheet estimate should trip the 50 MB budget after the " +
            "second delete and evict the oldest, leaving only the newest undoable");
    }
}
