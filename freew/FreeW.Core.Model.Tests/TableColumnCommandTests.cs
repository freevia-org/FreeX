namespace FreeW.Core.Model.Tests;

/// <summary>
/// Regression tests for H4 (ColumnWidthsPt stays in sync after insert/delete-column commands,
/// and the BuildTable tblGrid count is reconciled to actual grid columns) and H6 (insert/delete/
/// vertical-merge commands use GRID-column indexing, not cell-list indexing, so rows with
/// GridSpan &gt; 1 target the correct cells).
/// </summary>
public class TableColumnCommandTests
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

    // ── H4: ColumnWidthsPt stays in sync ─────────────────────────────────────────────────────────

    [Fact]
    public void InsertColumn_UpdatesColumnWidthsPt()
    {
        // A 2-column table with explicit widths: after insert the width list grows by 1.
        var table = Table.Create(1, 2);
        table.ColumnWidthsPt.AddRange([100.0, 120.0]);
        var (_, bus) = MakeDocWithTable(table);

        bus.Execute(new InsertTableColumnCommand(blockIndex: 0, columnIndex: 1));

        table.ColumnWidthsPt.Should().HaveCount(3,
            "inserting a column should add one entry to ColumnWidthsPt");
    }

    [Fact]
    public void DeleteColumn_UpdatesColumnWidthsPt()
    {
        // A 2-column table with explicit widths: after delete the width list shrinks by 1.
        var table = Table.Create(1, 2);
        table.ColumnWidthsPt.AddRange([100.0, 120.0]);
        var (_, bus) = MakeDocWithTable(table);

        bus.Execute(new DeleteTableColumnCommand(blockIndex: 0, columnIndex: 0));

        table.ColumnWidthsPt.Should().HaveCount(1,
            "deleting a column should remove one entry from ColumnWidthsPt");
    }

    [Fact]
    public void InsertThenDeleteColumn_ColumnWidthsPtCountMatchesActualGridColumns()
    {
        // After insert then delete the ColumnWidthsPt list must exactly match the actual grid-column
        // count so the writer can emit a correct tblGrid without further reconciliation being needed.
        var table = Table.Create(2, 3);
        table.ColumnWidthsPt.AddRange([80.0, 90.0, 100.0]);
        var (_, bus) = MakeDocWithTable(table);

        bus.Execute(new InsertTableColumnCommand(blockIndex: 0, columnIndex: 1));
        bus.Execute(new DeleteTableColumnCommand(blockIndex: 0, columnIndex: 2));

        // After insert+delete the model should be back to 3 grid columns and 3 widths.
        var actualGridCols = table.Rows.Max(r => r.Cells.Sum(c => Math.Max(1, c.GridSpan)));
        table.ColumnWidthsPt.Should().HaveCount(actualGridCols,
            "ColumnWidthsPt must track the actual grid-column count after insert+delete");
    }

    // ── H6: Grid-column-aware insert / delete / vertical-merge ──────────────────────────────────

    [Fact]
    public void DeleteColumn_WithHorizontalMerge_TargetsCorrectGridColumn()
    {
        // Row 0: [A(span=2)] [C]          (2 cells, 3 grid columns)
        // Row 1: [D]         [E]  [F]     (3 cells, 3 grid columns)
        // Deleting grid column 1 (inside A's span) should DECREMENT A's span, not remove A or C.
        var table = new Table();
        var row0 = new TableRow();
        row0.Cells.Add(new TableCell("A") { GridSpan = 2 });
        row0.Cells.Add(new TableCell("C"));
        var row1 = new TableRow();
        row1.Cells.Add(new TableCell("D"));
        row1.Cells.Add(new TableCell("E"));
        row1.Cells.Add(new TableCell("F"));
        table.Rows.Add(row0);
        table.Rows.Add(row1);

        var (_, bus) = MakeDocWithTable(table);
        bus.Execute(new DeleteTableColumnCommand(blockIndex: 0, columnIndex: 1));

        // Row 0: A's span should shrink from 2 to 1; C stays.
        row0.Cells.Should().HaveCount(2, "row 0 still has 2 cells (A's span was decremented, not removed)");
        row0.Cells[0].PlainText.Should().Be("A");
        row0.Cells[0].GridSpan.Should().Be(1, "A's span decrements from 2 to 1 when grid col 1 is deleted");
        row0.Cells[1].PlainText.Should().Be("C");

        // Row 1: grid col 1 = cell E is removed.
        row1.Cells.Should().HaveCount(2, "row 1 loses one cell (E at grid col 1)");
        row1.Cells[0].PlainText.Should().Be("D");
        row1.Cells[1].PlainText.Should().Be("F");
    }

    [Fact]
    public void DeleteColumn_SingleCellPerRow_RemovesCorrectCell()
    {
        // No merges: grid-column index == cell-list index.
        // Row 0: [A] [B] [C]
        // Deleting grid col 1 should remove B in every row.
        var table = Table.Create(1, 3);
        table.Rows[0].Cells[0] = new TableCell("A");
        table.Rows[0].Cells[1] = new TableCell("B");
        table.Rows[0].Cells[2] = new TableCell("C");
        var (_, bus) = MakeDocWithTable(table);

        bus.Execute(new DeleteTableColumnCommand(blockIndex: 0, columnIndex: 1));

        table.Rows[0].Cells.Should().HaveCount(2);
        table.Rows[0].Cells[0].PlainText.Should().Be("A");
        table.Rows[0].Cells[1].PlainText.Should().Be("C");
    }

    [Fact]
    public void InsertColumn_Undo_WithHorizontalMerge_RestoresExactTable()
    {
        // BF3 (updated from Z5): InsertTableColumnCommand now widens a spanning cell rather than
        // inserting a standalone cell inside its span (Word behaviour).
        //
        // Layout (before insert):
        //   Row 0: [A (GridSpan=2, grid 0-1)]  [B (grid 2)]
        //   Row 1: [C (grid 0)]  [D (grid 1)]  [E (grid 2)]
        //
        // Insert at grid column 1 (strictly INSIDE A's span):
        //   Row 0: [A (GridSpan=3, grid 0-2)]  [B (grid 3)]   — A widened, no new cell
        //   Row 1: [C (grid 0)]  [new (grid 1)]  [D (grid 2)]  [E (grid 3)]
        //
        // Undo must restore the original layout exactly (A back to GridSpan=2, row1=C,D,E).
        var table = new Table();
        var row0 = new TableRow();
        var cellA = new TableCell("A") { GridSpan = 2 };
        var cellB = new TableCell("B");
        row0.Cells.Add(cellA);
        row0.Cells.Add(cellB);
        var row1 = new TableRow();
        var cellC = new TableCell("C");
        var cellD = new TableCell("D");
        var cellE = new TableCell("E");
        row1.Cells.Add(cellC);
        row1.Cells.Add(cellD);
        row1.Cells.Add(cellE);
        table.Rows.Add(row0);
        table.Rows.Add(row1);

        var (_, bus) = MakeDocWithTable(table);
        bus.Execute(new InsertTableColumnCommand(blockIndex: 0, columnIndex: 1));

        // Sanity: after insert, row 0 still has 2 cells (A widened), row 1 has 4 cells (new inserted).
        row0.Cells.Should().HaveCount(2, "row 0: A was widened (BF3) so still 2 cells");
        cellA.GridSpan.Should().Be(3, "A's GridSpan grows from 2 to 3 (BF3 — widen inside span)");
        row1.Cells.Should().HaveCount(4, "row 1 gains one new cell");

        // Undo.
        bus.Undo();

        // Row 0: A (GridSpan=2) and B must be restored exactly.
        row0.Cells.Should().HaveCount(2, "undo must restore row 0 to 2 cells");
        row0.Cells[0].Should().BeSameAs(cellA, "original cell A must be at index 0");
        row0.Cells[0].GridSpan.Should().Be(2, "A's GridSpan=2 must be restored by undo");
        row0.Cells[1].Should().BeSameAs(cellB, "original cell B must be at index 1");

        // Row 1: C, D, E must be restored exactly.
        row1.Cells.Should().HaveCount(3, "undo must restore row 1 to 3 cells");
        row1.Cells[0].Should().BeSameAs(cellC);
        row1.Cells[1].Should().BeSameAs(cellD);
        row1.Cells[2].Should().BeSameAs(cellE);
    }

    [Fact]
    public void MergeCellsVertical_WithHorizontalMerge_TargetsCorrectCell()
    {
        // Row 0: [X(span=2)] [Y]      (3 grid cols; cell-list index 0→grid 0-1, index 1→grid 2)
        // Row 1: [A]  [B]    [C]      (3 grid cols; cell-list index == grid col)
        // Vertical-merging grid column 2 (Y / C pair) must touch Y (cell index 1 in row 0) and C
        // (cell index 2 in row 1) — NOT X (which is cell index 0) or B (cell index 1 in row 1).
        var table = new Table();
        var row0 = new TableRow();
        row0.Cells.Add(new TableCell("X") { GridSpan = 2 });
        row0.Cells.Add(new TableCell("Y"));
        var row1 = new TableRow();
        row1.Cells.Add(new TableCell("A"));
        row1.Cells.Add(new TableCell("B"));
        row1.Cells.Add(new TableCell("C"));
        table.Rows.Add(row0);
        table.Rows.Add(row1);

        var (_, bus) = MakeDocWithTable(table);
        bus.Execute(new MergeCellsVerticalCommand(blockIndex: 0, columnIndex: 2, firstRow: 0, lastRow: 1));

        // Y (row 0, grid col 2 → cell index 1) becomes the merge head.
        row0.Cells[1].PlainText.Should().Be("Y");
        row0.Cells[1].VerticalMerge.Should().Be(VerticalMergeState.Restart,
            "Y is the head of the vertical merge at grid column 2");

        // C (row 1, grid col 2 → cell index 2) becomes the continuation.
        row1.Cells[2].PlainText.Should().Be("C");
        row1.Cells[2].VerticalMerge.Should().Be(VerticalMergeState.Continue,
            "C continues the vertical merge at grid column 2");

        // X, A, B must be untouched.
        row0.Cells[0].VerticalMerge.Should().Be(VerticalMergeState.None, "X is not part of the merge");
        row1.Cells[0].VerticalMerge.Should().Be(VerticalMergeState.None, "A is not part of the merge");
        row1.Cells[1].VerticalMerge.Should().Be(VerticalMergeState.None, "B is not part of the merge");
    }

    // ── BH4: SplitCell vertical uses grid-column mapping per row ─────────────────────────────────

    [Fact]
    public void SplitCell_Vertical_WithLowerRowHorizontalMerge_ClearsCorrectCells()
    {
        // Table layout (3 grid columns):
        //   Row 0: [A]         [Y(Restart)]           (grid col 0 = A,  grid col 1 = Y)
        //             cell idx 0             cell idx 1
        //   Row 1: [X(span=2)] [C(Continue)]          (grid col 0-1 = X, grid col 2 = ?)
        //             cell idx 0             cell idx 1
        //
        // Wait — to make a vertical merge between Y(row0,grid1) and C(row1,grid1), row1 needs:
        //   [X(span=1)] [C(Continue)] — simple 2-cell row first.
        //
        // Actual scenario: row0 has cells [A, Y(Restart)] (2 cells, no span), grid cols 0,1.
        //                  row1 has cells [X(span=2), C(Continue)] (2 cells), grid col 0 = X(covers 0+1), grid col 2 = C.
        //
        // That means Y is at grid col 1 in row0, but C would need to be at grid col 1 in row1.
        // With X having span=2, grid col 1 is INSIDE X, not C.
        //
        // To demonstrate BH4, we need a lower row where a preceding cell has GridSpan > 1 so that
        // the cell at the target GRID column has a DIFFERENT cell-list index than the head row.
        //
        // Correct scenario:
        //   Row 0: [A]  [Y(Restart)]   → A has GridSpan=1 (grid 0), Y at cell idx 1 (grid 1)
        //   Row 1: [X(span=1)][C(Continue)] is trivial (same layout); instead use:
        //   Row 1: [A cell at grid col 0] [B cell at grid col 1 = Continue]
        //          BUT row1 has an extra cell BEFORE the target: [X(span=2)] inserts at grid 0-1, pushing C to grid 2.
        //
        // Cleaner: 3-grid-column table:
        //   Row 0: [P(span=1)] [Q(Restart,span=1)] [R]   → grid cols: P=0, Q=1, R=2; cell idx: P=0, Q=1, R=2
        //   Row 1: [X(span=2)] [C(Continue)]              → grid cols: X=0..1, C=2;  cell idx: X=0, C=1
        //
        // MergeCellsVertical on grid col 1 would set Q=Restart, C=Continue (using GridColumnToCellIndex).
        // But SplitCellCommand is invoked with cell-list index of Q in row0 = 1.
        // Bug: the old code uses below[1] for row1, which is C — WAIT, that's accidentally correct here
        //      because C IS at cell-list index 1. The bug fires when the Continue cell is NOT at cell-list
        //      index == head row's cell-list index.
        //
        // Correct BH4 scenario: head-row cell at cell-list index N, but lower row's Continue cell
        // is at a DIFFERENT cell-list index.
        //   Row 0: [A(span=2)] [Q(Restart)]    → grid: A=0..1, Q=2; cell idx: A=0, Q=1
        //   Row 1: [X] [Y]     [C(Continue)]   → grid: X=0, Y=1, C=2; cell idx: X=0, Y=1, C=2
        //
        // SplitCellCommand called with (rowIndex=0, columnIndex=1) → cell Q (Restart).
        // BH4 bug: looks at row1.Cells[1] = Y (not C) → Y.VerticalMerge != Continue → breaks early, C never cleared.
        // Fix: CellIndexToGridColumn(row0, 1) = 2 → GridColumnToCellIndex(row1, 2) = 2 → row1.Cells[2] = C ✓
        var table = new Table();
        var row0 = new TableRow();
        row0.Cells.Add(new TableCell("A") { GridSpan = 2 });  // cell idx 0, grid cols 0-1
        row0.Cells.Add(new TableCell("Q") { VerticalMerge = VerticalMergeState.Restart }); // cell idx 1, grid col 2
        var row1 = new TableRow();
        row1.Cells.Add(new TableCell("X"));  // cell idx 0, grid col 0
        row1.Cells.Add(new TableCell("Y"));  // cell idx 1, grid col 1
        row1.Cells.Add(new TableCell("C") { VerticalMerge = VerticalMergeState.Continue }); // cell idx 2, grid col 2
        table.Rows.Add(row0);
        table.Rows.Add(row1);

        var (_, bus) = MakeDocWithTable(table);

        // Apply SplitCellCommand with the cell-list index of Q in row0 (= 1).
        bus.Execute(new SplitCellCommand(blockIndex: 0, rowIndex: 0, columnIndex: 1));

        // Q's Restart must be cleared.
        row0.Cells[1].VerticalMerge.Should().Be(VerticalMergeState.None,
            "Q (head) must be cleared to None after split");

        // C's Continue must be cleared — BH4 bug: old code looked at row1.Cells[1] = Y and stopped early.
        row1.Cells[2].VerticalMerge.Should().Be(VerticalMergeState.None,
            "C (continuation at cell-list idx 2, grid col 2) must be cleared by vertical split");

        // X and Y must be untouched.
        row1.Cells[0].VerticalMerge.Should().Be(VerticalMergeState.None, "X must not be touched");
        row1.Cells[1].VerticalMerge.Should().Be(VerticalMergeState.None, "Y must not be touched");

        // Undo must restore the vertical merge exactly.
        bus.Undo();
        row0.Cells[1].VerticalMerge.Should().Be(VerticalMergeState.Restart,
            "Q must be restored to Restart after undo");
        row1.Cells[2].VerticalMerge.Should().Be(VerticalMergeState.Continue,
            "C must be restored to Continue after undo");
        row1.Cells[0].VerticalMerge.Should().Be(VerticalMergeState.None, "X must remain None after undo");
        row1.Cells[1].VerticalMerge.Should().Be(VerticalMergeState.None, "Y must remain None after undo");
    }

    [Fact]
    public void SplitCell_Vertical_MergeAndSplitRoundTrip_WithLowerRowHorizontalMerge()
    {
        // Round-trip: MergeCellsVertical → SplitCell → all merge states back to None.
        // Same layout as above: row0=[A(span=2), Q], row1=[X, Y, C].
        // MergeCellsVertical uses grid col 2 (Q in row0, C in row1).
        // SplitCell uses cell-list index 1 in row0 (= Q, at grid col 2).
        var table = new Table();
        var row0 = new TableRow();
        row0.Cells.Add(new TableCell("A") { GridSpan = 2 });
        row0.Cells.Add(new TableCell("Q"));
        var row1 = new TableRow();
        row1.Cells.Add(new TableCell("X"));
        row1.Cells.Add(new TableCell("Y"));
        row1.Cells.Add(new TableCell("C"));
        table.Rows.Add(row0);
        table.Rows.Add(row1);

        var (_, bus) = MakeDocWithTable(table);

        // Merge: MergeCellsVerticalCommand takes a GRID column index (2 = Q/C column).
        bus.Execute(new MergeCellsVerticalCommand(blockIndex: 0, columnIndex: 2, firstRow: 0, lastRow: 1));
        row0.Cells[1].VerticalMerge.Should().Be(VerticalMergeState.Restart);
        row1.Cells[2].VerticalMerge.Should().Be(VerticalMergeState.Continue);

        // Split: SplitCellCommand takes the cell-list index in the head row (1 = Q).
        bus.Execute(new SplitCellCommand(blockIndex: 0, rowIndex: 0, columnIndex: 1));

        // After round-trip all cells must be None — round-trip complete.
        row0.Cells[1].VerticalMerge.Should().Be(VerticalMergeState.None,
            "Q must be None after merge→split round-trip");
        row1.Cells[2].VerticalMerge.Should().Be(VerticalMergeState.None,
            "C must be None after merge→split round-trip");
        row0.Cells[0].VerticalMerge.Should().Be(VerticalMergeState.None, "A untouched");
        row1.Cells[0].VerticalMerge.Should().Be(VerticalMergeState.None, "X untouched");
        row1.Cells[1].VerticalMerge.Should().Be(VerticalMergeState.None, "Y untouched");

        // Undo the split → merge is restored.
        bus.Undo();
        row0.Cells[1].VerticalMerge.Should().Be(VerticalMergeState.Restart, "Q restored to Restart after undo-split");
        row1.Cells[2].VerticalMerge.Should().Be(VerticalMergeState.Continue, "C restored to Continue after undo-split");
    }
}
