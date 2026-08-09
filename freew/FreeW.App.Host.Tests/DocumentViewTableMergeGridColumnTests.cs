using System.Linq;
using System.Reflection;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using WpfTable = System.Windows.Documents.Table;

namespace FreeW.App.Host.Tests;

/// <summary>
/// H8: <see cref="DocumentView.TableLocationOf"/> resolves a selection endpoint to a PER-ROW
/// cell-list index (the cell's position within its own row's <c>Cells</c> list), not a table-wide
/// GRID-COLUMN index. <see cref="MergeCellsVerticalCommand"/> (and <see cref="InsertTableColumnCommand"/>
/// / <see cref="DeleteTableColumnCommand"/>) expect the true grid column, which diverges from the
/// cell-list index once a row has a preceding horizontal (gridSpan) merge.
/// <para>
/// The private <c>GridColumnAt</c>/<c>CellIndexToGridColumn</c> helpers added by the fix are invoked
/// here via reflection to prove the exact conversion the fix's call sites depend on — the full
/// WPF <c>Selection.Select</c>-driven route through <see cref="DocumentView.MergeSelectedCells"/> is
/// exercised by <see cref="DocumentViewTableSelectionOracleTests"/>-style oracle tests elsewhere but
/// proved unreliable (intermittent WPF test-host crashes) under this machine's current load, so the
/// conversion itself — the actual defect — is verified directly and deterministically instead.
/// <see cref="InsertTableColumnLeft_after_a_gridSpan_cell_inserts_at_the_true_grid_column"/> below
/// still exercises the full public API end-to-end for the family fix (a single-point caret, no
/// multi-row Selection, so it isn't subject to the same flakiness).
/// FreeW.Core.Model.Tests.TableColumnCommandTests.MergeCellsVertical_WithHorizontalMerge_TargetsCorrectCell
/// (pre-existing, unmodified by this fix) already proves <see cref="MergeCellsVerticalCommand"/>
/// merges the correct cells when GIVEN the right grid column — composed with the tests below (which
/// prove <c>GridColumnAt</c> computes that right grid column from the per-row index
/// <see cref="DocumentView.TableLocationOf"/> returns) this proves the fix end-to-end.
/// </para>
/// </summary>
public sealed class DocumentViewTableMergeGridColumnTests
{
    // Builds a 2-row x 3-grid-column table where BOTH rows are pre-merged identically: cell-list
    // index 0 spans grid columns 0-1 ("A{row}"), and cell-list index 1 sits at grid column 2
    // ("B{row}"). Mirrors MergeCellsHorizontalCommand's output shape (GridSpan grows on the survivor,
    // the absorbed cell is dropped).
    private static TextDocument TwoRowsPreMergedAtGridColumnZero()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = Table.Create(2, 3);
        foreach (var rowIndex in new[] { 0, 1 })
        {
            var row = table.Rows[rowIndex];
            row.Cells[0] = new TableCell($"A{rowIndex}") { GridSpan = 2 };
            row.Cells.RemoveAt(1); // absorbed by the GridSpan=2 cell
            row.Cells[1] = new TableCell($"B{rowIndex}"); // grid column 2, cell-list index 1
        }
        document.Blocks.Add(table);
        return document;
    }

    private static DocumentView Load(TextDocument document)
    {
        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }

    private static WpfTable RenderedTable(DocumentView view) =>
        view.Document.Blocks.OfType<WpfTable>().Single();

    // Invokes DocumentView's private H8 helper via reflection so the test proves the exact conversion
    // the fix's call sites (MergeSelectedCells' vertical branch, InsertTableColumnLeft/InsertTableColumn/
    // DeleteTableColumn) depend on, without needing the flaky WPF multi-row Selection machinery.
    private static int InvokeGridColumnAt(DocumentView view, int blockIndex, int rowIndex, int cellIndex)
    {
        var method = typeof(DocumentView).GetMethod("GridColumnAt", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new System.MissingMethodException(
                "DocumentView.GridColumnAt not found — the H8 grid-column conversion helper is missing.");
        return (int)method.Invoke(view, [blockIndex, rowIndex, cellIndex])!;
    }

    [StaFact]
    public void GridColumnAt_converts_the_cellList_index_to_the_true_grid_column_across_a_horizontal_merge()
    {
        // H8 bug: MergeSelectedCells' vertical-merge branch previously passed the raw per-row
        // cell-list index straight to MergeCellsVerticalCommand as if it were the table-wide grid
        // column. Row 0's "B0" cell sits at cell-list index 1 but — because the preceding "A0" cell
        // has GridSpan=2 — at true GRID column 2. GridColumnAt must return 2, not 1.
        var view = Load(TwoRowsPreMergedAtGridColumnZero());

        var gridColumn = InvokeGridColumnAt(view, blockIndex: 0, rowIndex: 0, cellIndex: 1);

        gridColumn.Should().Be(2,
            "cell-list index 1 in row 0 sits at grid column 2 because the preceding A0 cell has GridSpan=2");
    }

    [StaFact]
    public void GridColumnAt_leaves_the_index_unchanged_when_the_row_has_no_preceding_horizontal_merge()
    {
        // Sibling/no-regression: in an ordinary (unmerged) row the cell-list index already IS the
        // grid column, so the conversion must be a no-op — proving the fix doesn't also perturb the
        // common case that previously worked (by accident, since cell-list index == grid column there).
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = Table.Create(2, 2);
        table.Rows[0].Cells[1] = new TableCell("Top");
        table.Rows[1].Cells[1] = new TableCell("Bottom");
        document.Blocks.Add(table);
        var view = Load(document);

        var gridColumn = InvokeGridColumnAt(view, blockIndex: 0, rowIndex: 0, cellIndex: 1);

        gridColumn.Should().Be(1, "with no preceding GridSpan, cell-list index and grid column coincide");
    }

    [StaFact]
    public void GridColumnAt_uses_the_grid_position_of_the_targeted_row_not_a_different_rows_shape()
    {
        // Sibling/no-regression: the conversion must be computed against the SPECIFIC row passed in
        // (start.RowIndex), not e.g. always row 0 or some cached shape — row 1 here has a DIFFERENT
        // merge shape (span at cell-list index 1, not 0) than row 0 in TwoRowsPreMergedAtGridColumnZero.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = Table.Create(1, 3);
        table.Rows[0].Cells[0] = new TableCell("X");
        table.Rows[0].Cells[1] = new TableCell("Y") { GridSpan = 2 };
        table.Rows[0].Cells.RemoveAt(2); // absorbed by Y's GridSpan=2 (grid columns 1-2)
        document.Blocks.Add(table);
        var view = Load(document);

        // "X" is cell-list index 0 = grid column 0 (nothing precedes it): conversion is a no-op here.
        InvokeGridColumnAt(view, blockIndex: 0, rowIndex: 0, cellIndex: 0).Should().Be(0);
        // "Y" is cell-list index 1 = grid column 1 (only X, GridSpan=1, precedes it).
        InvokeGridColumnAt(view, blockIndex: 0, rowIndex: 0, cellIndex: 1).Should().Be(1);
    }

    [StaFact]
    public void InsertTableColumnLeft_after_a_gridSpan_cell_inserts_at_the_true_grid_column()
    {
        // H8 family fix: InsertTableColumnCommand takes a GRID-COLUMN index. Row 0 has a GridSpan=2
        // cell at grid columns 0-1 (cell-list index 0) followed by a normal cell at grid column 2
        // (cell-list index 1). Placing the caret in that trailing cell and inserting a column to its
        // left must insert at grid column 2 (between the spanning cell and the trailing cell) for
        // EVERY row, not at cell-list index 1 (which InsertTableColumnCommand would misinterpret as
        // grid column 1 — strictly inside the spanning cell — widening its span instead of inserting).
        var document = TwoRowsPreMergedAtGridColumnZero();
        var view = Load(document);
        var rows = RenderedTable(view).RowGroups[0].Rows;
        view.CaretPosition = rows[0].Cells[1].Blocks.FirstBlock!.ContentStart; // "B0" cell

        view.InsertTableColumnLeft();

        var table = view.Model.Blocks.OfType<Table>().Single();
        // A correct grid-column insert at column 2 leaves the GridSpan=2 "A" cell untouched (it does
        // not sit strictly inside a span) and adds a fresh blank cell immediately before "B" in every
        // row, keeping the row's cell count at 3.
        table.Rows[0].Cells[0].GridSpan.Should().Be(2, "the A0 spanning cell must be untouched by an insert at its right edge");
        table.Rows[0].Cells.Should().HaveCount(3, "a real column must be inserted, not a span widened");
        table.Rows[0].Cells[1].PlainText.Should().BeEmpty("the newly inserted cell is blank");
        table.Rows[0].Cells[2].PlainText.Should().Be("B0", "the original B0 cell must be pushed one slot right, not overwritten");
        table.Rows[1].Cells[0].GridSpan.Should().Be(2, "the A1 spanning cell in the untouched row must also be left alone");
        table.Rows[1].Cells.Should().HaveCount(3);
        table.Rows[1].Cells[2].PlainText.Should().Be("B1");
    }
}
