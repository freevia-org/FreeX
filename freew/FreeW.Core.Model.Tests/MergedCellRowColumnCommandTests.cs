namespace FreeW.Core.Model.Tests;

/// <summary>
/// Regression tests for merged-cell-aware row/column insert and delete commands (BF1, BF2, BF3).
///
/// BF1 — DeleteTableRowCommand: deleting a row that holds a VerticalMerge.Restart head must promote
///        the cell directly below to Restart so the merge is not orphaned. Undo must restore the
///        original states exactly.
///
/// BF2 — InsertTableRowCommand: inserting a row strictly inside a vertical-merged run must set the
///        new row's cells to VerticalMerge.Continue (extending the merge) rather than None
///        (severing it). Undo removes the inserted row.
///
/// BF3 — InsertTableColumnCommand: inserting a column at a grid position that falls strictly inside
///        a cell's GridSpan must widen that cell (GridSpan++) rather than inserting a standalone
///        cell at the wrong position. Boundary inserts still produce a new cell. Undo is exact.
///
/// Also verifies that non-merged tables are unaffected (no regressions).
/// </summary>
public class MergedCellRowColumnCommandTests
{
    private sealed class DocContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }

    private static (TextDocument Doc, DocumentCommandBus Bus) MakeDocWithTable(Table table)
    {
        var doc = new TextDocument();
        doc.Blocks.Add(table);
        return (doc, new DocumentCommandBus(new DocContext(doc)));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a 3-row × 1-column table with a full vertical merge in column 0:
    ///   Row 0: cell "A"  VerticalMerge = Restart
    ///   Row 1: cell "B"  VerticalMerge = Continue
    ///   Row 2: cell "C"  VerticalMerge = Continue
    /// </summary>
    private static (Table Table, TableCell A, TableCell B, TableCell C) MakeVerticalMergeTable()
    {
        var table = new Table();
        var cellA = new TableCell("A") { VerticalMerge = VerticalMergeState.Restart };
        var cellB = new TableCell("B") { VerticalMerge = VerticalMergeState.Continue };
        var cellC = new TableCell("C") { VerticalMerge = VerticalMergeState.Continue };
        var row0 = new TableRow(); row0.Cells.Add(cellA);
        var row1 = new TableRow(); row1.Cells.Add(cellB);
        var row2 = new TableRow(); row2.Cells.Add(cellC);
        table.Rows.Add(row0);
        table.Rows.Add(row1);
        table.Rows.Add(row2);
        return (table, cellA, cellB, cellC);
    }

    // ── BF1: DeleteTableRowCommand — vertical-merge promotion ───────────────────────────────────

    [Fact]
    public void BF1_DeleteRestartRow_PromotesNextContinueToRestart()
    {
        // Layout:  Row0=Restart, Row1=Continue, Row2=Continue
        // Delete row 0 → row 1 (now the first row) should become Restart, row 2 stays Continue.
        var (table, _, cellB, cellC) = MakeVerticalMergeTable();
        var (_, bus) = MakeDocWithTable(table);

        bus.Execute(new DeleteTableRowCommand(blockIndex: 0, rowIndex: 0));

        table.Rows.Should().HaveCount(2, "one row was deleted");
        cellB.VerticalMerge.Should().Be(VerticalMergeState.Restart,
            "the row below the deleted Restart head must be promoted to Restart (BF1)");
        cellC.VerticalMerge.Should().Be(VerticalMergeState.Continue,
            "the second continuation row is still Continue");
    }

    [Fact]
    public void BF1_DeleteRestartRow_Undo_RestoresOriginalMergeStates()
    {
        // Delete row 0 (Restart), then undo — all three rows and their original states must return.
        var (table, cellA, cellB, cellC) = MakeVerticalMergeTable();
        var (_, bus) = MakeDocWithTable(table);

        bus.Execute(new DeleteTableRowCommand(blockIndex: 0, rowIndex: 0));
        bus.Undo();

        table.Rows.Should().HaveCount(3, "undo restores the deleted row");
        table.Rows[0].Cells[0].Should().BeSameAs(cellA, "original Restart cell restored at row 0");
        cellA.VerticalMerge.Should().Be(VerticalMergeState.Restart, "cellA is still Restart after undo");
        cellB.VerticalMerge.Should().Be(VerticalMergeState.Continue, "cellB is still Continue after undo (promotion reverted)");
        cellC.VerticalMerge.Should().Be(VerticalMergeState.Continue, "cellC is still Continue after undo");
    }

    [Fact]
    public void BF1_DeleteContinueRow_DoesNotChangeOtherMergeStates()
    {
        // Delete row 1 (Continue) — the Restart head (row 0) remains Restart; the merge simply
        // shortens (row 0 = Restart, row 1 was Continue, row 2 = Continue). After deletion row 0
        // is Restart and the surviving row 1 (formerly row 2) stays Continue.
        var (table, cellA, _, cellC) = MakeVerticalMergeTable();
        var (_, bus) = MakeDocWithTable(table);

        bus.Execute(new DeleteTableRowCommand(blockIndex: 0, rowIndex: 1));

        table.Rows.Should().HaveCount(2);
        cellA.VerticalMerge.Should().Be(VerticalMergeState.Restart, "Restart head is unaffected");
        cellC.VerticalMerge.Should().Be(VerticalMergeState.Continue, "the remaining Continue stays Continue");
    }

    [Fact]
    public void BF1_DeleteRestartRow_LastContinueIsNextAndPromoted()
    {
        // 2-row table: Row0=Restart, Row1=Continue  → delete row 0 → row 1 becomes Restart.
        var table = new Table();
        var cellR = new TableCell("R") { VerticalMerge = VerticalMergeState.Restart };
        var cellK = new TableCell("K") { VerticalMerge = VerticalMergeState.Continue };
        var r0 = new TableRow(); r0.Cells.Add(cellR); table.Rows.Add(r0);
        var r1 = new TableRow(); r1.Cells.Add(cellK); table.Rows.Add(r1);
        var (_, bus) = MakeDocWithTable(table);

        bus.Execute(new DeleteTableRowCommand(blockIndex: 0, rowIndex: 0));

        cellK.VerticalMerge.Should().Be(VerticalMergeState.Restart,
            "the only remaining cell was Continue and must now be promoted to Restart");
    }

    [Fact]
    public void BF1_NonMergedTable_DeleteRow_NoVerticalMergeChange()
    {
        // Plain 3×2 table, no vertical merges — delete row 1 must leave all cells with None.
        var table = Table.Create(3, 2);
        var (_, bus) = MakeDocWithTable(table);

        bus.Execute(new DeleteTableRowCommand(blockIndex: 0, rowIndex: 1));

        table.Rows.Should().HaveCount(2);
        foreach (var row in table.Rows)
            foreach (var cell in row.Cells)
                cell.VerticalMerge.Should().Be(VerticalMergeState.None,
                    "non-merged cells are untouched by delete-row");
    }

    // ── BF2: InsertTableRowCommand — vertical-merge extension ───────────────────────────────────

    [Fact]
    public void BF2_InsertRowInsideVerticalMerge_NewCellIsContinue()
    {
        // Layout: Row0=Restart, Row1=Continue, Row2=Continue
        // Insert at row 1 (between rows 0 and 1, i.e. strictly inside the merge).
        // Expected: new row's cell = Continue (merge extended); rows 0,2,3 states unchanged.
        var (table, cellA, cellB, cellC) = MakeVerticalMergeTable();
        var (_, bus) = MakeDocWithTable(table);

        bus.Execute(new InsertTableRowCommand(blockIndex: 0, rowIndex: 1));

        table.Rows.Should().HaveCount(4, "one row was inserted");
        table.Rows[0].Cells[0].Should().BeSameAs(cellA);
        cellA.VerticalMerge.Should().Be(VerticalMergeState.Restart);

        // The NEW row at index 1 must be Continue (extends the merge).
        var newCell = table.Rows[1].Cells[0];
        newCell.VerticalMerge.Should().Be(VerticalMergeState.Continue,
            "inserting strictly inside a vertical-merged run must yield a Continue cell (BF2)");

        // Original continue cells are now at rows 2 and 3.
        cellB.VerticalMerge.Should().Be(VerticalMergeState.Continue, "original row-1 cell stays Continue");
        cellC.VerticalMerge.Should().Be(VerticalMergeState.Continue, "original row-2 cell stays Continue");
    }

    [Fact]
    public void BF2_InsertRowInsideVerticalMerge_Undo_RemovesInsertedRow()
    {
        var (table, _, _, _) = MakeVerticalMergeTable();
        var (_, bus) = MakeDocWithTable(table);

        bus.Execute(new InsertTableRowCommand(blockIndex: 0, rowIndex: 1));
        table.Rows.Should().HaveCount(4);

        bus.Undo();

        table.Rows.Should().HaveCount(3, "undo removes the inserted row");
    }

    [Fact]
    public void BF2_InsertRowAtBoundary_BeforeRestart_NewCellIsNone()
    {
        // Insert at row 0 (before the Restart head) — NOT inside any merge.
        // The new cell must be None (merge is not extended).
        var (table, _, _, _) = MakeVerticalMergeTable();
        var (_, bus) = MakeDocWithTable(table);

        bus.Execute(new InsertTableRowCommand(blockIndex: 0, rowIndex: 0));

        var newCell = table.Rows[0].Cells[0];
        newCell.VerticalMerge.Should().Be(VerticalMergeState.None,
            "inserting BEFORE the Restart head is not inside the merge — cell should be None");
    }

    [Fact]
    public void BF2_InsertRowAtBoundary_AfterLastContinue_NewCellIsNone()
    {
        // Insert at row 3 (after the last Continue) — NOT inside any merge.
        var (table, _, _, _) = MakeVerticalMergeTable();
        var (_, bus) = MakeDocWithTable(table);

        bus.Execute(new InsertTableRowCommand(blockIndex: 0, rowIndex: 3));

        var newCell = table.Rows[3].Cells[0];
        newCell.VerticalMerge.Should().Be(VerticalMergeState.None,
            "inserting AFTER the last Continue is not inside the merge — cell should be None");
    }

    [Fact]
    public void BF2_NonMergedTable_InsertRow_AllNewCellsAreNone()
    {
        // Plain 2×3 table, no vertical merges — inserted row must have all None cells.
        var table = Table.Create(2, 3);
        var (_, bus) = MakeDocWithTable(table);

        bus.Execute(new InsertTableRowCommand(blockIndex: 0, rowIndex: 1));

        table.Rows.Should().HaveCount(3);
        foreach (var cell in table.Rows[1].Cells)
            cell.VerticalMerge.Should().Be(VerticalMergeState.None,
                "new cells in a non-merged table must be None");
    }

    // ── BF3: InsertTableColumnCommand — horizontal-span widening ────────────────────────────────

    [Fact]
    public void BF3_InsertColumnInsideHorizontalSpan_WidensSpan()
    {
        // Row 0: [A(GridSpan=2, grid cols 0-1)] [B(grid 2)]
        // Row 1: [C(grid 0)] [D(grid 1)] [E(grid 2)]
        // Insert at grid column 1 (strictly inside A's span):
        //   Row 0: A's GridSpan should become 3 (widened); no new cell.
        //   Row 1: a new cell is inserted at grid column 1.
        var table = new Table();
        var cellA = new TableCell("A") { GridSpan = 2 };
        var cellB = new TableCell("B");
        var row0 = new TableRow();
        row0.Cells.Add(cellA);
        row0.Cells.Add(cellB);

        var cellC = new TableCell("C");
        var cellD = new TableCell("D");
        var cellE = new TableCell("E");
        var row1 = new TableRow();
        row1.Cells.Add(cellC);
        row1.Cells.Add(cellD);
        row1.Cells.Add(cellE);

        table.Rows.Add(row0);
        table.Rows.Add(row1);

        var (_, bus) = MakeDocWithTable(table);
        bus.Execute(new InsertTableColumnCommand(blockIndex: 0, columnIndex: 1));

        // Row 0: A widened, no extra cell.
        row0.Cells.Should().HaveCount(2, "row 0 still has 2 cells — A was widened, not split");
        cellA.GridSpan.Should().Be(3, "A's GridSpan grows from 2 to 3 (BF3 — widen inside span)");
        row0.Cells[1].Should().BeSameAs(cellB, "B is still at cell index 1");

        // Row 1: new cell inserted between C and D.
        row1.Cells.Should().HaveCount(4, "row 1 gains one new cell");
        row1.Cells[0].Should().BeSameAs(cellC);
        row1.Cells[2].Should().BeSameAs(cellD, "D shifts right by one");
        row1.Cells[3].Should().BeSameAs(cellE);

        // Grid totals must be equal across all rows (rectangular).
        var gridWidths = table.Rows.Select(r => r.Cells.Sum(c => Math.Max(1, c.GridSpan))).Distinct();
        gridWidths.Should().HaveCount(1, "all rows must have the same total grid width after insert");
    }

    [Fact]
    public void BF3_InsertColumnInsideHorizontalSpan_Undo_RestoresExactState()
    {
        var table = new Table();
        var cellA = new TableCell("A") { GridSpan = 2 };
        var cellB = new TableCell("B");
        var row0 = new TableRow();
        row0.Cells.Add(cellA);
        row0.Cells.Add(cellB);
        table.Rows.Add(row0);

        var (_, bus) = MakeDocWithTable(table);
        bus.Execute(new InsertTableColumnCommand(blockIndex: 0, columnIndex: 1));

        // Sanity: A is now GridSpan=3.
        cellA.GridSpan.Should().Be(3);

        bus.Undo();

        // After undo A must be back to GridSpan=2 and only 2 cells in the row.
        row0.Cells.Should().HaveCount(2, "undo restores to 2 cells");
        cellA.GridSpan.Should().Be(2, "undo restores A's original GridSpan=2");
        row0.Cells[0].Should().BeSameAs(cellA);
        row0.Cells[1].Should().BeSameAs(cellB);
    }

    [Fact]
    public void BF3_InsertColumnAtBoundary_InsertsNewCell()
    {
        // Row 0: [A(GridSpan=2, grid cols 0-1)] [B(grid 2)]
        // Insert at grid column 0 (the START of A — boundary, not inside).
        // Expected: a new cell is inserted before A, A stays GridSpan=2.
        var table = new Table();
        var cellA = new TableCell("A") { GridSpan = 2 };
        var cellB = new TableCell("B");
        var row0 = new TableRow();
        row0.Cells.Add(cellA);
        row0.Cells.Add(cellB);
        table.Rows.Add(row0);

        var (_, bus) = MakeDocWithTable(table);
        bus.Execute(new InsertTableColumnCommand(blockIndex: 0, columnIndex: 0));

        // A new cell is inserted at cell-list index 0; A and B shift right.
        row0.Cells.Should().HaveCount(3, "inserting at boundary adds a new cell");
        row0.Cells[0].GridSpan.Should().Be(1, "the new cell has GridSpan=1");
        row0.Cells[1].Should().BeSameAs(cellA, "A is now at cell index 1");
        cellA.GridSpan.Should().Be(2, "A's GridSpan is unchanged when inserting at a boundary");
        row0.Cells[2].Should().BeSameAs(cellB);
    }

    [Fact]
    public void BF3_InsertColumnAtBoundary_Undo_RestoresExactState()
    {
        var table = new Table();
        var cellA = new TableCell("A") { GridSpan = 2 };
        var cellB = new TableCell("B");
        var row0 = new TableRow();
        row0.Cells.Add(cellA);
        row0.Cells.Add(cellB);
        table.Rows.Add(row0);

        var (_, bus) = MakeDocWithTable(table);
        bus.Execute(new InsertTableColumnCommand(blockIndex: 0, columnIndex: 0));
        bus.Undo();

        row0.Cells.Should().HaveCount(2);
        row0.Cells[0].Should().BeSameAs(cellA);
        cellA.GridSpan.Should().Be(2);
        row0.Cells[1].Should().BeSameAs(cellB);
    }

    [Fact]
    public void BF3_DeleteColumnInsideHorizontalSpan_DecrementsSpan()
    {
        // Existing behaviour confirmed by H6 tests; verify here for symmetry with BF3.
        // Row 0: [A(GridSpan=2)] [B]   → delete grid col 1 (inside A) → A.GridSpan = 1, B stays.
        var table = new Table();
        var cellA = new TableCell("A") { GridSpan = 2 };
        var cellB = new TableCell("B");
        var row0 = new TableRow();
        row0.Cells.Add(cellA);
        row0.Cells.Add(cellB);
        table.Rows.Add(row0);
        // Add a second row to avoid "last column" guard.
        var row1 = new TableRow();
        row1.Cells.Add(new TableCell("C"));
        row1.Cells.Add(new TableCell("D"));
        table.Rows.Add(row1);

        var (_, bus) = MakeDocWithTable(table);
        bus.Execute(new DeleteTableColumnCommand(blockIndex: 0, columnIndex: 1));

        cellA.GridSpan.Should().Be(1, "span decremented from 2 to 1 when deleting inside the span");
        row0.Cells.Should().HaveCount(2, "no cell was removed from row 0 — just span decremented");
    }

    [Fact]
    public void BF3_DeleteColumnInsideHorizontalSpan_Undo_RestoresSpan()
    {
        var table = new Table();
        var cellA = new TableCell("A") { GridSpan = 2 };
        var cellB = new TableCell("B");
        var row0 = new TableRow(); row0.Cells.Add(cellA); row0.Cells.Add(cellB);
        var row1 = new TableRow(); row1.Cells.Add(new TableCell("C")); row1.Cells.Add(new TableCell("D"));
        table.Rows.Add(row0);
        table.Rows.Add(row1);

        var (_, bus) = MakeDocWithTable(table);
        bus.Execute(new DeleteTableColumnCommand(blockIndex: 0, columnIndex: 1));
        bus.Undo();

        cellA.GridSpan.Should().Be(2, "undo restores original GridSpan=2");
    }

    // ── Combined / rectangular-grid invariant ───────────────────────────────────────────────────

    [Fact]
    public void NonMergedTable_InsertThenDeleteColumn_GridRemainsRectangular()
    {
        // Plain 3×3 table — insert col 1 then delete col 1: grid must be 3 columns throughout.
        var table = Table.Create(3, 3);
        var (_, bus) = MakeDocWithTable(table);

        bus.Execute(new InsertTableColumnCommand(blockIndex: 0, columnIndex: 1));
        bus.Execute(new DeleteTableColumnCommand(blockIndex: 0, columnIndex: 1));

        foreach (var row in table.Rows)
            row.Cells.Sum(c => Math.Max(1, c.GridSpan)).Should().Be(3,
                "grid width must be 3 after insert+delete (no regressions on plain tables)");
    }

    [Fact]
    public void AllRowsHaveEqualGridWidth_AfterInsertColumnInsideSpan()
    {
        // 3-row table where row 0 has a span=3 cell and rows 1-2 are plain.
        // Insert at grid column 1 (inside the span of row 0).
        // ALL rows must have the same total grid width afterwards.
        var table = new Table();
        var spanCell = new TableCell("X") { GridSpan = 3 };
        var row0 = new TableRow(); row0.Cells.Add(spanCell);
        table.Rows.Add(row0);
        for (var r = 1; r <= 2; r++)
        {
            var row = new TableRow();
            for (var c = 0; c < 3; c++)
                row.Cells.Add(new TableCell($"R{r}C{c}"));
            table.Rows.Add(row);
        }

        var (_, bus) = MakeDocWithTable(table);
        bus.Execute(new InsertTableColumnCommand(blockIndex: 0, columnIndex: 1));

        var widths = table.Rows.Select(r => r.Cells.Sum(c => Math.Max(1, c.GridSpan))).ToList();
        widths.Should().AllBeEquivalentTo(4,
            "after inserting at grid col 1 inside the span, all rows must have 4 grid columns");
        spanCell.GridSpan.Should().Be(4, "the spanning cell is widened from 3 to 4");
    }
}
