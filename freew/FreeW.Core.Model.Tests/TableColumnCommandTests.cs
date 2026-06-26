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
        // Z5 regression: InsertTableColumnCommand.Revert previously re-ran GridColumnToCellIndex on
        // the ALREADY-MODIFIED row, which resolved to the wrong cell when a spanning cell preceded the
        // insert position, corrupting the merged cell. After the fix, Revert removes the exact cell
        // instance that was inserted rather than recomputing the grid position.
        //
        // Layout (before insert):
        //   Row 0: [A (GridSpan=2, grid 0-1)]  [B (grid 2)]
        //   Row 1: [C (grid 0)]  [D (grid 1)]  [E (grid 2)]
        //
        // Insert at grid column 1:
        //   Row 0: [new (grid 1)]  [A (now grid 0 & 2)]  [B (grid 3)]  — cell-list index 0
        //   Row 1: [C (grid 0)]    [new (grid 1)]         [D (grid 2)]  [E (grid 3)]
        //
        // Undo must restore the original layout exactly (A still GridSpan=2, B intact, row1=C,D,E).
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

        // Sanity: after insert, rows should have gained one cell each.
        row0.Cells.Should().HaveCount(3, "row 0 gains one cell after insert");
        row1.Cells.Should().HaveCount(4, "row 1 gains one cell after insert");

        // Undo.
        bus.Undo();

        // Row 0: A (GridSpan=2) and B must be restored exactly — no corruption of A's span.
        row0.Cells.Should().HaveCount(2, "undo must restore row 0 to 2 cells");
        row0.Cells[0].Should().BeSameAs(cellA, "original cell A must be back at index 0");
        row0.Cells[0].GridSpan.Should().Be(2, "A's GridSpan=2 must be unchanged by undo");
        row0.Cells[1].Should().BeSameAs(cellB, "original cell B must be back at index 1");

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
}
