using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R125-commands-undo-byte-budget-further: extends R119/R120's IEstimatesMemory coverage to the
/// further candidates identified but never implemented: <see cref="EditCellsCommand"/> (the
/// single most-used per-cell edit command in the codebase -- backs plain typing AND Text to
/// Columns' per-row plan), <see cref="CompositeWorkbookCommand"/> (sums its children's estimates
/// so a composite wrapping many large sub-commands, e.g. Text to Columns' per-row EditCellsCommand
/// list, is billed correctly), <see cref="MergeCellsCommand"/>, <see cref="RemoveDuplicateRowsCommand"/>,
/// and <see cref="DuplicateSheetCommand"/> (whole-sheet retention, mirrors RemoveSheetCommand).
/// Each previously fell back to the flat 200-byte <c>CommandBus.DefaultCommandBytes</c> default
/// regardless of how much data its undo snapshot actually retained.
///
/// <c>ResizeStructuredTableCommand</c> ("table resize") was evaluated and deliberately NOT given
/// an estimator: its <c>_previousCells</c> dictionary only ever holds the table's totals-row
/// relocation and grown-calculated-column formula cells, both bounded by the table's COLUMN count
/// (not its full row*column extent) -- shrinking a table does not blank or otherwise snapshot the
/// dropped rows' cell contents (that data is simply left behind on the sheet, outside the new
/// table Range), so there is no real per-cell retention to report here.
/// </summary>
public sealed class R125_UndoByteBudgetFurtherCommandsTests
{
    private static readonly WorkbookId WbId = WorkbookId.New();

    private static CommandBus MakeBus(Workbook wb) => new(_ => new TestCommandContext(wb));

    // ── EditCellsCommand ───────────────────────────────────────────────────────

    [Fact]
    public void R125_EditCellsCommand_ImplementsIEstimatesMemory_ScalingWithEditCount()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var edits = new List<(CellAddress Address, Cell NewCell)>();
        for (uint r = 1; r <= 500; r++)
            edits.Add((new CellAddress(sheet.Id, r, 1), Cell.FromValue(new NumberValue(r))));

        IWorkbookCommand command = new EditCellsCommand(sheet.Id, edits);
        var estimator = command as IEstimatesMemory;

        estimator.Should().NotBeNull(
            "EditCellsCommand's undo snapshot is an 11-field per-cell tuple (even richer than " +
            "CopyRangeCommand's CellSnapshot, which already uses 400 bytes/cell) and backs both " +
            "plain typing and Text to Columns' per-row edit plan");
        estimator!.EstimatedBytes.Should().Be(500 * 400);
    }

    [Fact]
    public void R125_EditCellsCommand_SingleCellEdit_StaysNearDefault_NoRegression()
    {
        // An everyday single-cell edit (1 * 400 = 400 bytes) must not become disproportionately
        // large relative to the old flat 200-byte default -- guards against an over-aggressive
        // per-cell constant swallowing normal typing.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        IWorkbookCommand command = EditCellsCommand.ForValue(sheet.Id, new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        var estimator = (IEstimatesMemory)command;

        estimator.EstimatedBytes.Should().Be(400);
    }

    // ── CompositeWorkbookCommand ───────────────────────────────────────────────

    [Fact]
    public void R125_CompositeWorkbookCommand_ImplementsIEstimatesMemory_SumsAppliedChildren()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var edits1 = new List<(CellAddress, Cell)> { (new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1))) };
        var edits2 = new List<(CellAddress, Cell)> { (new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(2))) };
        IWorkbookCommand composite = new CompositeWorkbookCommand("Text to Columns", new IWorkbookCommand[]
        {
            new EditCellsCommand(sheet.Id, edits1),
            new EditCellsCommand(sheet.Id, edits2),
        });

        var estimator = composite as IEstimatesMemory;
        estimator.Should().NotBeNull("a composite retains every applied child command for undo, so its footprint must be the sum of its children's");

        estimator!.EstimatedBytes.Should().Be(0, "nothing has been applied yet");

        composite.Apply(ctx).Success.Should().BeTrue();

        estimator.EstimatedBytes.Should().Be(2 * 400, "two 1-cell EditCellsCommand children, each 1 * 400 bytes");
    }

    // ── MergeCellsCommand ──────────────────────────────────────────────────────

    [Fact]
    public void R125_MergeCellsCommand_ImplementsIEstimatesMemory_ScalingWithRangeCellCount()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 100, 5));

        IWorkbookCommand command = new MergeCellsCommand(sheet.Id, range);
        var estimator = command as IEstimatesMemory;

        estimator.Should().NotBeNull("MergeCellsCommand's undo snapshot is a full (Address, Cell) pair per non-top-left cell it blanks");
        estimator!.EstimatedBytes.Should().Be(100 * 5 * 300);
    }

    // ── RemoveDuplicateRowsCommand ─────────────────────────────────────────────

    [Fact]
    public void R125_RemoveDuplicateRowsCommand_ImplementsIEstimatesMemory_ScalingWithRangeCellCountAfterApply()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        for (uint r = 1; r <= 200; r++)
        {
            // Every even row is a full duplicate (both columns) of row 2, so RemoveDuplicates has
            // real work to do and a non-empty snapshot -- not the "nothing to do" no-op path.
            var key = r % 2 == 0 ? 2u : r;
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new TextValue("dup"));
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(key));
        }
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 200, 2));

        IWorkbookCommand command = new RemoveDuplicateRowsCommand(sheet.Id, range);
        var estimator = command as IEstimatesMemory;
        estimator.Should().NotBeNull("RemoveDuplicateRowsCommand's undo snapshot is a full per-cell capture across the whole operated range, plus optional comment/hyperlink/rich-text dictionaries");

        // Reported from the operated range up front (like ApplyStyleCommand/ClearContentsCommand),
        // since every in-range cell always gets exactly one snapshot entry regardless of how many
        // rows survive de-duplication.
        estimator!.EstimatedBytes.Should().Be(200 * 2 * 400);

        command.Apply(ctx).Success.Should().BeTrue();

        estimator.EstimatedBytes.Should().Be(200 * 2 * 400, "every in-range cell (200 rows * 2 cols) is captured regardless of how many rows survive de-duplication");
    }

    // ── DuplicateSheetCommand ──────────────────────────────────────────────────

    [Fact]
    public void R125_DuplicateSheetCommand_ImplementsIEstimatesMemory_ScalingWithOccupiedCellCountAfterApply()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        for (uint r = 1; r <= 50; r++)
            for (uint c = 1; c <= 4; c++)
                sheet.SetCell(new CellAddress(sheet.Id, r, c), Cell.FromValue(new NumberValue(r * 10 + c)));

        IWorkbookCommand command = new DuplicateSheetCommand(sheet.Id);
        var estimator = command as IEstimatesMemory;
        estimator.Should().NotBeNull("Undo of Duplicate Sheet removes the entire cloned sheet, mirroring RemoveSheetCommand's whole-sheet retention");

        estimator!.EstimatedBytes.Should().Be(0, "nothing has been captured before Apply runs");

        command.Apply(ctx).Success.Should().BeTrue();

        estimator.EstimatedBytes.Should().Be(50 * 4 * 200, "the cloned sheet's 200 occupied cells at RemoveSheetCommand's same 200 bytes/cell constant");
    }

    // ── SubtotalCommand (composite of sub-commands) ───────────────────────────

    [Fact]
    public void R125_SubtotalCommand_ImplementsIEstimatesMemory_SumsAppliedSubCommandsAfterApply()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Group"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(3));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));
        IWorkbookCommand command = new SubtotalCommand(sheet.Id, range, groupByColumnOffset: 0, subtotalColumnOffset: 1);
        var estimator = command as IEstimatesMemory;
        estimator.Should().NotBeNull("Subtotal's undo retains one InsertRowsCommand + one EditCellsCommand per detected group, each with its own real snapshot");

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        estimator!.EstimatedBytes.Should().BeGreaterThan(0, "at least one group's insert+edit sub-commands must have been applied and counted");
    }

    // ── Real product entry point: CommandBus byte-budget eviction ────────────

    [Fact]
    public void R125_CommandBus_EditCellsCommand_ByteBudgetEvictsOldestWhenRealSizeExceedsBudget()
    {
        // Two ~30 MB-class multi-cell edits (30,000 cells * 400 bytes/cell = 12,000,000 bytes
        // each) comfortably clear the 50 MB budget combined. Before this fix, EditCellsCommand
        // was billed only the flat 200-byte default regardless of _edits.Count, so neither edit
        // would ever approach the budget and both would remain undoable.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var bus = MakeBus(wb);

        List<(CellAddress, Cell)> MakeBigEdit(uint rowOffset)
        {
            // 400 rows * 200 cols = 80,000 cells * 400 bytes/cell = 32,000,000 bytes (~30 MB) per
            // edit; two in a row total ~64,000,000 bytes, comfortably over the 50 MB budget.
            var edits = new List<(CellAddress, Cell)>(80_000);
            for (uint r = 0; r < 400; r++)
                for (uint c = 1; c <= 200; c++)
                    edits.Add((new CellAddress(sheet.Id, rowOffset + r, c), Cell.FromValue(new NumberValue(r * 1000 + c))));
            return edits;
        }

        bus.Execute(WbId, new EditCellsCommand(sheet.Id, MakeBigEdit(1))).Success.Should().BeTrue();
        bus.Execute(WbId, new EditCellsCommand(sheet.Id, MakeBigEdit(1))).Success.Should().BeTrue();

        var undoCount = 0;
        while (bus.CanUndo(WbId))
        {
            bus.Undo(WbId).Success.Should().BeTrue();
            undoCount++;
        }

        undoCount.Should().Be(1,
            "two ~12 MB-per-edit real estimates total well over the 50 MB budget and must evict the oldest, leaving only the newest undoable");
    }

    // ── Sibling/no-regression coverage ─────────────────────────────────────────

    [Fact]
    public void R125_CommandBus_SmallEditCellsCommand_StaysUndoable_NoRegression()
    {
        // An everyday small multi-cell edit (25 cells * 400 bytes = 10,000 bytes) must NOT be
        // prematurely evicted. Guards against an over-aggressive per-cell constant swallowing
        // normal small edits (e.g. a filled-in form, a handful of typed cells).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var bus = MakeBus(wb);
        var edits = new List<(CellAddress, Cell)>();
        for (uint r = 1; r <= 5; r++)
            for (uint c = 1; c <= 5; c++)
                edits.Add((new CellAddress(sheet.Id, r, c), Cell.FromValue(new NumberValue(r * 10 + c))));

        bus.Execute(WbId, new EditCellsCommand(sheet.Id, edits)).Success.Should().BeTrue();

        bus.CanUndo(WbId).Should().BeTrue("a small edit is nowhere near the 50 MB budget and must remain undoable");
        bus.Undo(WbId).Success.Should().BeTrue();
        bus.CanUndo(WbId).Should().BeFalse();
    }
}
