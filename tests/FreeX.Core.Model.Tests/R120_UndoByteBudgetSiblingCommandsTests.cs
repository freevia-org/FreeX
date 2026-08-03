using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R120-commands-undo-byte-budget-2: R119 gave <see cref="PasteCellsCommand"/>,
/// <see cref="GroupedEditCellsCommand"/>, <see cref="SortCommand"/> and
/// <see cref="RemoveSheetCommand"/> real <see cref="IEstimatesMemory"/> implementations so
/// CommandBus's 50 MB undo byte-budget (<c>CommandBus.MaxUndoByteBudget</c>) could actually see
/// their true per-cell undo-snapshot footprint instead of the flat 200-byte
/// (<c>CommandBus.DefaultCommandBytes</c>) default. That fix was never extended to the sibling
/// commands with an identical (or richer) per-cell snapshot shape: <see cref="CopyRangeCommand"/>,
/// <see cref="MoveRangeCommand"/>, <see cref="FillCellsCommand"/>, <see cref="AutofillCommand"/>,
/// <see cref="ClearContentsCommand"/>, <see cref="PasteFormatsCommand"/>,
/// <see cref="PasteSpecialCellsCommand"/>, <see cref="InsertCellsCommand"/>/
/// <see cref="DeleteCellsCommand"/>, and <see cref="DeleteRowsCommand"/>/
/// <see cref="InsertRowsCommand"/>/<see cref="InsertColumnsCommand"/>/
/// <see cref="DeleteColumnsCommand"/> -- so up to 100 (MaxUndoDepth) large bulk edits (repeated
/// large copy/paste-drags, Fill Down over a huge column, deleting rows out of a sheet with a large
/// used range below, ...) could retain gigabytes of undo history where the byte budget was designed
/// to cap it at 50 MB.
/// </summary>
public sealed class R120_UndoByteBudgetSiblingCommandsTests
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

    // ── CopyRangeCommand ───────────────────────────────────────────────────────

    [Fact]
    public void R120_CopyRangeCommand_ImplementsIEstimatesMemory_ScalingWithSourceRangeCellCount()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 100, 10));

        // `as` (not a direct cast) so this assertion compiles against pre-fix source too, and
        // instead fails at runtime with a null estimator -- the fail-before/after technique.
        IWorkbookCommand command = new CopyRangeCommand(sheet.Id, range, new CellAddress(sheet.Id, 200, 1));
        var estimator = command as IEstimatesMemory;

        estimator.Should().NotBeNull("CopyRangeCommand's undo snapshot is per-cell (and even richer than PasteCellsCommand's -- adds comment/threaded-comment fields) and must report its real size");
        estimator!.EstimatedBytes.Should().Be(100 * 10 * 400);
    }

    // ── MoveRangeCommand ───────────────────────────────────────────────────────

    [Fact]
    public void R120_MoveRangeCommand_ImplementsIEstimatesMemory_ScalingWithAffectedCellCountAfterApply()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 5)); // 50 cells
        var destination = new CellAddress(sheet.Id, 1, 20); // disjoint 10x5 target -> 50 more distinct cells

        IWorkbookCommand command = new MoveRangeCommand(sheet.Id, source, destination);
        var estimator = command as IEstimatesMemory;
        estimator.Should().NotBeNull("MoveRangeCommand's undo snapshot is per-cell across the union of source and destination");

        // Before Apply, nothing has been captured yet -- must not fabricate a nonzero estimate.
        estimator!.EstimatedBytes.Should().Be(0);

        command.Apply(ctx).Success.Should().BeTrue();

        estimator.EstimatedBytes.Should().Be(100 * 400,
            "the affected-cell set is the disjoint union of the 50-cell source and 50-cell destination ranges");
    }

    // ── FillCellsCommand ───────────────────────────────────────────────────────

    [Fact]
    public void R120_FillCellsCommand_ImplementsIEstimatesMemory_ScalingWithRangeCellCount()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5000, 1));

        IWorkbookCommand command = new FillCellsCommand(sheet.Id, range, FillCellsDirection.Down);
        var estimator = command as IEstimatesMemory;

        estimator.Should().NotBeNull("FillCellsCommand's undo snapshot is per-cell across the whole fill range -- exactly the 'fill a whole column' scenario");
        estimator!.EstimatedBytes.Should().Be(5000 * 300);
    }

    // ── AutofillCommand ────────────────────────────────────────────────────────

    [Fact]
    public void R120_AutofillCommand_ImplementsIEstimatesMemory_ScalingWithFillRangeCellCount()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4000, 1));

        IWorkbookCommand command = new AutofillCommand(sheet.Id, sourceRange, fillRange);
        var estimator = command as IEstimatesMemory;

        estimator.Should().NotBeNull("AutofillCommand's undo snapshot is per-cell across the whole fill range");
        estimator!.EstimatedBytes.Should().Be(4000 * 300);
    }

    // ── ClearContentsCommand ───────────────────────────────────────────────────

    [Fact]
    public void R120_ClearContentsCommand_ImplementsIEstimatesMemory_ScalingWithRangeCellCount()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1000, 20));

        IWorkbookCommand command = new ClearContentsCommand(sheet.Id, range);
        var estimator = command as IEstimatesMemory;

        estimator.Should().NotBeNull("ClearContentsCommand's undo snapshot is per-cell across the cleared range");
        estimator!.EstimatedBytes.Should().Be(1000 * 20 * 300);
    }

    // ── PasteFormatsCommand ────────────────────────────────────────────────────

    [Fact]
    public void R120_PasteFormatsCommand_ImplementsIEstimatesMemory_ScalingWithFormatCount()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var formats = new List<(CellAddress Address, StyleId StyleId)>();
        for (uint i = 1; i <= 800; i++)
            formats.Add((new CellAddress(sheet.Id, i, 1), new StyleId(1)));

        IWorkbookCommand command = new PasteFormatsCommand(sheet.Id, formats);
        var estimator = command as IEstimatesMemory;

        estimator.Should().NotBeNull("PasteFormatsCommand's undo snapshot is a full Cell clone per formatted cell");
        estimator!.EstimatedBytes.Should().Be(800 * 300);
    }

    // ── PasteSpecialCellsCommand ───────────────────────────────────────────────

    [Fact]
    public void R120_PasteSpecialCellsCommand_ImplementsIEstimatesMemory_ScalingWithSourceCellCount()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 30, 30));
        var sourceCells = MakeCells(sheet.Id, 900);

        IWorkbookCommand command = new PasteSpecialCellsCommand(
            sheet.Id, sourceRange, sourceCells, new CellAddress(sheet.Id, 40, 1), default);
        var estimator = command as IEstimatesMemory;

        estimator.Should().NotBeNull("PasteSpecialCellsCommand's undo snapshot is a full per-destination-cell payload");
        estimator!.EstimatedBytes.Should().Be(900 * 300);
    }

    // ── InsertCellsCommand / DeleteCellsCommand ──────────────────────────────

    [Fact]
    public void R120_InsertCellsCommand_ImplementsIEstimatesMemory_ScalingWithCapturedCellCountAfterApply()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        for (uint r = 1; r <= 3; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), Cell.FromValue(new NumberValue(r)));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        IWorkbookCommand command = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down);
        var estimator = command as IEstimatesMemory;
        estimator.Should().NotBeNull("InsertCellsCommand's undo snapshot is a full (Address, Cell) pair per shifted cell");

        estimator!.EstimatedBytes.Should().Be(0, "nothing has been captured before Apply runs");

        command.Apply(ctx).Success.Should().BeTrue();

        estimator.EstimatedBytes.Should().Be(3 * 400,
            "the 3 occupied cells in column A fall within the shift-down region and must all be captured");
    }

    [Fact]
    public void R120_DeleteCellsCommand_ImplementsIEstimatesMemory_ScalingWithCapturedCellCountAfterApply()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        for (uint r = 1; r <= 4; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), Cell.FromValue(new NumberValue(r)));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        IWorkbookCommand command = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Up);
        var estimator = command as IEstimatesMemory;
        estimator.Should().NotBeNull("DeleteCellsCommand's undo snapshot is a full (Address, Cell) pair per shifted/deleted cell");

        command.Apply(ctx).Success.Should().BeTrue();

        estimator!.EstimatedBytes.Should().Be(4 * 400,
            "the deleted cell plus every shifted-up survivor in column A must all be captured in one unified capture");
    }

    // ── DeleteRowsCommand / InsertRowsCommand ─────────────────────────────────

    [Fact]
    public void R120_DeleteRowsCommand_ImplementsIEstimatesMemory_ScalingWithDeletedPlusShiftedCellCountAfterApply()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        // 3 occupied cells in the deleted row, 2 occupied cells in the row that shifts up into it.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(2)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(3)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(4)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new NumberValue(5)));

        IWorkbookCommand command = new DeleteRowsCommand(sheet.Id, startRow: 1, count: 1);
        var estimator = command as IEstimatesMemory;
        estimator.Should().NotBeNull("DeleteRowsCommand's undo snapshot is the richest per-cell shape in the codebase (see the defect description)");

        estimator!.EstimatedBytes.Should().Be(0, "nothing has been captured before Apply runs");

        command.Apply(ctx).Success.Should().BeTrue();

        estimator.EstimatedBytes.Should().Be(5 * 400,
            "3 deleted-band cells plus 2 shifted-up cells must both be captured and billed");
    }

    [Fact]
    public void R120_InsertRowsCommand_ImplementsIEstimatesMemory_ScalingWithMovedCellCountAfterApply()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(2)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(3)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new NumberValue(4)));

        IWorkbookCommand command = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        var estimator = command as IEstimatesMemory;
        estimator.Should().NotBeNull("InsertRowsCommand's undo snapshot captures every cell shifted down by the insert");

        command.Apply(ctx).Success.Should().BeTrue();

        estimator!.EstimatedBytes.Should().Be(4 * 400,
            "all 4 occupied cells at or after the insert point must be captured for undo");
    }

    // ── DeleteColumnsCommand / InsertColumnsCommand ───────────────────────────

    [Fact]
    public void R120_DeleteColumnsCommand_ImplementsIEstimatesMemory_ScalingWithDeletedPlusShiftedCellCountAfterApply()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        // 2 occupied cells in the deleted column, 3 occupied cells in the column that shifts left into it.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(2)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(3)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new NumberValue(4)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromValue(new NumberValue(5)));

        IWorkbookCommand command = new DeleteColumnsCommand(sheet.Id, startCol: 1, count: 1);
        var estimator = command as IEstimatesMemory;
        estimator.Should().NotBeNull("DeleteColumnsCommand's undo snapshot mirrors DeleteRowsCommand's -- deleted band plus shifted survivors");

        command.Apply(ctx).Success.Should().BeTrue();

        estimator!.EstimatedBytes.Should().Be(5 * 400,
            "2 deleted-band cells plus 3 shifted-left cells must both be captured and billed");
    }

    [Fact]
    public void R120_InsertColumnsCommand_ImplementsIEstimatesMemory_ScalingWithMovedCellCountAfterApply()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(2)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(3)));

        IWorkbookCommand command = new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 1);
        var estimator = command as IEstimatesMemory;
        estimator.Should().NotBeNull("InsertColumnsCommand's undo snapshot captures every cell shifted right by the insert");

        command.Apply(ctx).Success.Should().BeTrue();

        estimator!.EstimatedBytes.Should().Be(3 * 400,
            "all 3 occupied cells at or after the insert point must be captured for undo");
    }

    // ── Real product entry point: CommandBus byte-budget eviction ────────────

    [Fact]
    public void R120_CommandBus_DeleteRowsCommand_ByteBudgetEvictsOldestWhenRealSizeExceedsBudget()
    {
        // Two deletes of a densely-populated 200-row band (30,000 cells each at 400 bytes/cell =
        // 12,000,000 bytes) plus a huge shifted-below region (also densely populated: another
        // ~35,000 cells) comfortably clears 50 MB combined across two deletes. Before this fix,
        // DeleteRowsCommand (the richest per-cell undo snapshot shape in the codebase, per the
        // defect) was billed only the flat 200-byte default regardless of real size, so neither
        // delete would ever approach the budget and BOTH would remain undoable.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var bus = MakeBus(wb);

        void PopulateBand(uint startRow, uint rowCount, uint cols)
        {
            for (uint r = startRow; r < startRow + rowCount; r++)
                for (uint c = 1; c <= cols; c++)
                    sheet.SetCell(new CellAddress(sheet.Id, r, c), Cell.FromValue(new NumberValue(r * 1000 + c)));
        }

        // Row band [1..200] (deleted) and [201..400] (shifted up) -- 200 * 200 = 40,000 cells each,
        // 80,000 total per delete * 400 bytes/cell = 32,000,000 bytes (~30 MB) per delete; two in a
        // row total ~64,000,000 bytes, comfortably over the 50 MB budget.
        PopulateBand(1, 200, 200);
        PopulateBand(201, 200, 200);

        bus.Execute(WbId, new DeleteRowsCommand(sheet.Id, startRow: 1, count: 200)).Success.Should().BeTrue();

        // Re-populate an equally large band so the second delete captures a comparably large
        // snapshot (the first delete already removed/shifted the original cells).
        PopulateBand(1, 200, 200);
        PopulateBand(201, 200, 200);

        bus.Execute(WbId, new DeleteRowsCommand(sheet.Id, startRow: 1, count: 200)).Success.Should().BeTrue();

        var undoCount = 0;
        while (bus.CanUndo(WbId))
        {
            bus.Undo(WbId).Success.Should().BeTrue();
            undoCount++;
        }

        undoCount.Should().Be(1,
            "two ~30 MB-per-delete real estimates total well over the 50 MB budget and must evict the oldest, " +
            "leaving only the newest undoable");
    }

    // ── Sibling/no-regression coverage ─────────────────────────────────────────

    [Fact]
    public void R120_CommandBus_SmallCopyRangeStaysUndoable_NoRegression()
    {
        // An everyday small copy (well under the byte budget on its own -- 25 cells * 400 bytes =
        // 10,000 bytes) must NOT be prematurely evicted. Guards against an over-aggressive
        // per-cell constant swallowing normal small edits.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var bus = MakeBus(wb);
        for (uint r = 1; r <= 5; r++)
            for (uint c = 1; c <= 5; c++)
                sheet.SetCell(new CellAddress(sheet.Id, r, c), Cell.FromValue(new NumberValue(r * 10 + c)));

        var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 5));
        bus.Execute(WbId, new CopyRangeCommand(sheet.Id, source, new CellAddress(sheet.Id, 20, 1)))
            .Success.Should().BeTrue();

        bus.CanUndo(WbId).Should().BeTrue("a small copy is nowhere near the 50 MB budget and must remain undoable");
        bus.Undo(WbId).Success.Should().BeTrue();
        bus.CanUndo(WbId).Should().BeFalse();
    }
}
