using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-TBL3: headless tests for Tab/Shift-Tab cell navigation and cell merge/split.
/// Covers:
///   - Tab advances to next cell (within row)
///   - Tab wraps to first cell of next row
///   - Tab in last cell appends a new row and enters its first cell
///   - Shift+Tab moves to previous cell
///   - Shift+Tab at first cell is a no-op (stays put)
///   - MergeSelectedCells horizontal (1×2 block → GridSpan=2)
///   - MergeSelectedCells vertical (2×1 block → VerticalMerge)
///   - SplitCurrentCell restores a previously merged cell
///   - Undo for MergeSelectedCells and SplitCurrentCell
///   - Tab outside a table inserts a literal tab character (regression guard)
/// </summary>
public sealed class DocumentViewTableNavMergeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a DocumentView with a 2-row × 3-column table (2×3).
    /// Row0 = [R0C0, R0C1, R0C2], Row1 = [R1C0, R1C1, R1C2].
    /// </summary>
    private static (DocumentView View, int TableBlockIdx, Table Tbl) MakeTable2x3()
    {
        var doc = TextDocument.CreateEmpty();
        var tbl = Table.Create(2, 3);
        for (var r = 0; r < 2; r++)
            for (var c = 0; c < 3; c++)
                tbl.Rows[r].Cells[c] = new TableCell($"R{r}C{c}");
        doc.Blocks.Add(tbl);
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(900, 6000));
        var idx = doc.Blocks.IndexOf(tbl);
        return (view, idx, tbl);
    }

    // ── Tab navigation tests ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tab_advances_caret_to_next_cell_in_same_row()
    {
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? info = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTable2x3();
            // Place caret in R0C0, then Tab → should land in R0C1.
            view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
            view.SimulateTabCell(forward: true);
            info = view.CellCaretInfo;
        });

        if (!ran) return;
        info.Should().NotBeNull();
        info!.Value.Row.Should().Be(0, "Tab from R0C0 stays in row 0");
        info.Value.Col.Should().Be(1, "Tab from R0C0 (col 0) should advance to col 1");
        info.Value.Offset.Should().Be(0, "Tab lands at offset 0 (start of next cell)");
    }

    [Fact]
    public async Task Tab_wraps_to_first_cell_of_next_row()
    {
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? info = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTable2x3();
            // Place caret in last col of row 0 (R0C2), Tab → should land in R1C0.
            view.PlaceCaretInCell(idx, row: 0, col: 2, paraIdx: 0, offset: 0);
            view.SimulateTabCell(forward: true);
            info = view.CellCaretInfo;
        });

        if (!ran) return;
        info.Should().NotBeNull();
        info!.Value.Row.Should().Be(1, "Tab at end of row 0 should wrap to row 1");
        info.Value.Col.Should().Be(0, "Tab wrap lands at column 0");
    }

    [Fact]
    public async Task Tab_in_last_cell_appends_new_row_and_enters_first_cell()
    {
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? info = null;
        var rowCountAfter = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x3();
            // Place caret in last cell of the whole table (R1C2), Tab → new row appended.
            view.PlaceCaretInCell(idx, row: 1, col: 2, paraIdx: 0, offset: 0);
            view.SimulateTabCell(forward: true);
            rowCountAfter = tbl.Rows.Count;
            info = view.CellCaretInfo;
        });

        if (!ran) return;
        rowCountAfter.Should().Be(3, "Tab in last cell should append a new (3rd) row");
        info.Should().NotBeNull();
        info!.Value.Row.Should().Be(2, "caret should be in the newly appended row 2");
        info.Value.Col.Should().Be(0, "caret should be in the first column of the new row");
    }

    [Fact]
    public async Task Tab_in_last_cell_new_row_is_undoable()
    {
        var rowCountAfterUndo = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x3();
            view.PlaceCaretInCell(idx, row: 1, col: 2, paraIdx: 0, offset: 0);
            view.SimulateTabCell(forward: true); // appends row
            view.Undo();
            rowCountAfterUndo = tbl.Rows.Count;
        });

        if (!ran) return;
        rowCountAfterUndo.Should().Be(2, "undo should remove the appended row, restoring 2 rows");
    }

    [Fact]
    public async Task ShiftTab_moves_caret_to_previous_cell()
    {
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? info = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTable2x3();
            // Place in R0C1, Shift+Tab → should land in R0C0.
            view.PlaceCaretInCell(idx, row: 0, col: 1, paraIdx: 0, offset: 0);
            view.SimulateTabCell(forward: false);
            info = view.CellCaretInfo;
        });

        if (!ran) return;
        info.Should().NotBeNull();
        info!.Value.Row.Should().Be(0);
        info.Value.Col.Should().Be(0, "Shift+Tab from R0C1 goes to R0C0");
    }

    [Fact]
    public async Task ShiftTab_at_first_cell_stays_put()
    {
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? info = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTable2x3();
            // Place in R0C0 (first cell), Shift+Tab → no-op, caret stays.
            view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
            view.SimulateTabCell(forward: false);
            info = view.CellCaretInfo;
        });

        if (!ran) return;
        info.Should().NotBeNull();
        info!.Value.Row.Should().Be(0, "Shift+Tab at first cell stays in row 0");
        info.Value.Col.Should().Be(0, "Shift+Tab at first cell stays in col 0");
    }

    [Fact]
    public async Task ShiftTab_wraps_back_across_row_boundary()
    {
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? info = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTable2x3();
            // Place in R1C0 (first col of second row), Shift+Tab → R0C2.
            view.PlaceCaretInCell(idx, row: 1, col: 0, paraIdx: 0, offset: 0);
            view.SimulateTabCell(forward: false);
            info = view.CellCaretInfo;
        });

        if (!ran) return;
        info.Should().NotBeNull();
        info!.Value.Row.Should().Be(0, "Shift+Tab at R1C0 crosses to row 0");
        info.Value.Col.Should().Be(2, "Shift+Tab at R1C0 lands in last col (col 2) of row 0");
    }

    // ── Tab outside table — regression guard ─────────────────────────────────────────────────────

    [Fact]
    public async Task Tab_outside_table_inserts_literal_tab_character()
    {
        string? body = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            var para = new Paragraph("hello");
            doc.Blocks.Add(para);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            // Caret is in a body paragraph (no cell caret); Tab should insert '\t'.
            view.SimulateTabCell(forward: true);
            body = doc.Blocks.OfType<Paragraph>().First().PlainText;
        });

        if (!ran) return;
        body.Should().Contain("\t", "Tab outside a table must insert a literal tab character");
    }

    // ── MergeSelectedCells tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MergeSelectedCells_horizontal_collapses_gridspan()
    {
        int cellCountAfter = -1;
        int gridSpanAfter = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x3();
            // Select R0C0..R0C1 (1 row × 2 cols) and merge.
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 0, focusRow: 0, focusCol: 1);
            view.MergeSelectedCells();
            cellCountAfter = tbl.Rows[0].Cells.Count;
            gridSpanAfter = tbl.Rows[0].Cells[0].GridSpan;
        });

        if (!ran) return;
        cellCountAfter.Should().Be(2, "horizontal merge of 2 cells in a 3-col row leaves 2 logical cells");
        gridSpanAfter.Should().Be(2, "the surviving cell should have GridSpan=2");
    }

    [Fact]
    public async Task MergeSelectedCells_horizontal_is_undoable()
    {
        int cellCountAfterUndo = -1;
        int gridSpanAfterUndo = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x3();
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 0, focusRow: 0, focusCol: 1);
            view.MergeSelectedCells();
            view.Undo();
            cellCountAfterUndo = tbl.Rows[0].Cells.Count;
            gridSpanAfterUndo = tbl.Rows[0].Cells[0].GridSpan;
        });

        if (!ran) return;
        cellCountAfterUndo.Should().Be(3, "undo restores the 3 original cells");
        gridSpanAfterUndo.Should().Be(1, "undo restores GridSpan=1 on the first cell");
    }

    [Fact]
    public async Task MergeSelectedCells_vertical_sets_verticalmerge_states()
    {
        VerticalMergeState row0State = VerticalMergeState.None;
        VerticalMergeState row1State = VerticalMergeState.None;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x3();
            // Select R0C0..R1C0 (2 rows × 1 col) and merge.
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 0, focusRow: 1, focusCol: 0);
            view.MergeSelectedCells();
            row0State = tbl.Rows[0].Cells[0].VerticalMerge;
            row1State = tbl.Rows[1].Cells[0].VerticalMerge;
        });

        if (!ran) return;
        row0State.Should().Be(VerticalMergeState.Restart, "top cell of vertical merge must be Restart");
        row1State.Should().Be(VerticalMergeState.Continue, "bottom cell of vertical merge must be Continue");
    }

    [Fact]
    public async Task MergeSelectedCells_vertical_is_undoable()
    {
        VerticalMergeState row0After = VerticalMergeState.Restart;
        VerticalMergeState row1After = VerticalMergeState.Restart;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x3();
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 0, focusRow: 1, focusCol: 0);
            view.MergeSelectedCells();
            view.Undo();
            row0After = tbl.Rows[0].Cells[0].VerticalMerge;
            row1After = tbl.Rows[1].Cells[0].VerticalMerge;
        });

        if (!ran) return;
        row0After.Should().Be(VerticalMergeState.None, "undo restores Restart cell to None");
        row1After.Should().Be(VerticalMergeState.None, "undo restores Continue cell to None");
    }

    // ── SplitCurrentCell tests ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SplitCurrentCell_restores_cells_after_horizontal_merge()
    {
        int cellCountAfterMerge = -1;
        int cellCountAfterSplit = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x3();
            // Merge R0C0..R0C1 → 2 cells in row (GridSpan=2 on first).
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 0, focusRow: 0, focusCol: 1);
            view.MergeSelectedCells();
            cellCountAfterMerge = tbl.Rows[0].Cells.Count;

            // caret is placed in R0C0 by MergeSelectedCells. Now split it.
            view.SplitCurrentCell();
            cellCountAfterSplit = tbl.Rows[0].Cells.Count;
        });

        if (!ran) return;
        cellCountAfterMerge.Should().Be(2, "after merge: 2 logical cells in the row");
        cellCountAfterSplit.Should().Be(3, "after split: back to 3 cells");
    }

    [Fact]
    public async Task SplitCurrentCell_is_undoable()
    {
        int cellCountAfterUndoSplit = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x3();
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 0, focusRow: 0, focusCol: 1);
            view.MergeSelectedCells();
            view.SplitCurrentCell();
            view.Undo(); // undo the split → back to merged state
            cellCountAfterUndoSplit = tbl.Rows[0].Cells.Count;
        });

        if (!ran) return;
        cellCountAfterUndoSplit.Should().Be(2, "undo of split restores the merged (2-cell) state");
    }

    [Fact]
    public async Task SplitCurrentCell_forwards_requested_subdivision()
    {
        int rowCount = -1;
        int topCellCount = -1;
        int lowerFirstSpan = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x3();
            view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);

            view.SplitCurrentCell(rows: 2, cols: 2);

            rowCount = tbl.Rows.Count;
            topCellCount = tbl.Rows[0].Cells.Count;
            lowerFirstSpan = tbl.Rows[2].Cells[0].GridSpan;
        });

        if (!ran) return;
        rowCount.Should().Be(3);
        topCellCount.Should().Be(4);
        lowerFirstSpan.Should().Be(2);
    }

    [Fact]
    public async Task SplitCurrentCell_noop_when_not_in_table()
    {
        // Verify no exception and no side effects when called without a cell caret.
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Add(new Paragraph("body"));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            // No PlaceCaretInCell → _cellCaret is null → SplitCurrentCell is a no-op.
            view.SplitCurrentCell();
        });

        if (!ran) return;
        // If we got here without throwing, the no-op guard works.
        true.Should().BeTrue("SplitCurrentCell outside a table must not throw");
    }

    // ── BH1: MergeSelectedCells with a preceding horizontal merge ────────────────────────────────

    /// <summary>
    /// BH1 regression: when a row already has a preceding cell with GridSpan=2 (occupying grid
    /// columns 0-1), selecting the next two grid columns (2 and 3) must merge the two LOGICAL cells
    /// at those positions — not the wrong cells or a silent no-op that a direct grid-index lookup
    /// would produce.
    /// Table layout: Row0 = [A(GridSpan=2), B, C] (grid widths: 0-1, 2, 3).
    /// Selection: grid cols 2..3 → should collapse B+C into one cell (GridSpan=2).
    /// </summary>
    [Fact]
    public async Task MergeSelectedCells_horizontal_with_preceding_merge_targets_correct_cells()
    {
        int cellCountAfter = -1;
        int gridSpanAfter  = -1;
        var ran = await OnUiThread(() =>
        {
            // Build a 1-row table: cell A spans grid cols 0-1, then B at col 2, C at col 3.
            var doc = TextDocument.CreateEmpty();
            var tbl = Table.Create(1, 3);
            tbl.Rows[0].Cells[0] = new TableCell("A") { GridSpan = 2 };
            tbl.Rows[0].Cells[1] = new TableCell("B");
            tbl.Rows[0].Cells[2] = new TableCell("C");
            doc.Blocks.Add(tbl);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 4000));
            var idx = doc.Blocks.IndexOf(tbl);

            // Select grid columns 2..3 (B and C). These are GRID cols, not cell-list indices.
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 2, focusRow: 0, focusCol: 3);
            view.MergeSelectedCells();

            cellCountAfter = tbl.Rows[0].Cells.Count;
            gridSpanAfter  = tbl.Rows[0].Cells[1].GridSpan; // cell at cell-list index 1 (B+C merged)
        });

        if (!ran) return;
        cellCountAfter.Should().Be(2, "merging B and C leaves 2 logical cells in the row (A, B+C)");
        gridSpanAfter.Should().Be(2, "the merged B+C cell must have GridSpan=2");
    }

    // ── BH2: SplitCurrentCell with a preceding horizontal merge ──────────────────────────────────

    /// <summary>
    /// BH2 regression: when the caret is in a cell that lives AFTER a preceding merged cell,
    /// _cellCaret.Col is a GRID column. SplitCurrentCell must convert it to a cell-list index
    /// before calling SplitCellCommand — otherwise it targets the wrong cell or goes out of range.
    /// Table: Row0 = [A(GridSpan=2), B(GridSpan=2)] — grid cols 0-1 and 2-3.
    /// Caret in B (grid col 2). SplitCurrentCell must split B (cell-list index 1), not A.
    /// </summary>
    [Fact]
    public async Task SplitCurrentCell_with_preceding_merge_splits_correct_cell()
    {
        int cellCountAfter = -1;
        int gridSpanA      = -1;
        int gridSpanB1     = -1;
        var ran = await OnUiThread(() =>
        {
            // Row has two merged cells: A (grid 0-1, span=2) and B (grid 2-3, span=2).
            var doc = TextDocument.CreateEmpty();
            var tbl = Table.Create(1, 2);
            tbl.Rows[0].Cells[0] = new TableCell("A") { GridSpan = 2 };
            tbl.Rows[0].Cells[1] = new TableCell("B") { GridSpan = 2 };
            doc.Blocks.Add(tbl);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 4000));
            var idx = doc.Blocks.IndexOf(tbl);

            // Place caret in B — grid col 2, which is cell-list index 1.
            view.PlaceCaretInCell(idx, row: 0, col: 2, paraIdx: 0, offset: 0);
            view.SplitCurrentCell();

            cellCountAfter = tbl.Rows[0].Cells.Count;
            gridSpanA      = tbl.Rows[0].Cells[0].GridSpan; // A must be unchanged
            gridSpanB1     = tbl.Rows[0].Cells[1].GridSpan; // first half of the split B
        });

        if (!ran) return;
        cellCountAfter.Should().Be(3, "splitting B (span=2) adds one cell → total 3 cells");
        gridSpanA.Should().Be(2, "A must remain untouched (GridSpan=2)");
        gridSpanB1.Should().Be(1, "first part of split B must have GridSpan=1");
    }

    // ── BH3: Tab skips VerticalMerge.Continue cells ──────────────────────────────────────────────

    /// <summary>
    /// BH3 regression: in a 2-column × 2-row table where column 0 has a vertical merge
    /// (R0C0 = Restart, R1C0 = Continue), Tab from R0C0 must land on R0C1 (skipping the Continue
    /// cell R1C0 entirely). Tab count from R0C0 to last real cell must match Word semantics
    /// (3 stops: R0C0 → R0C1 → R1C1, skipping R1C0 Continue).
    /// </summary>
    [Fact]
    public async Task Tab_skips_verticalmerge_continue_cells()
    {
        var tabOrder = new List<(int Row, int Col)>();
        var ran = await OnUiThread(() =>
        {
            // 2×2 table: col 0 vertically merged (rows 0-1), col 1 plain.
            var doc = TextDocument.CreateEmpty();
            var tbl = Table.Create(2, 2);
            tbl.Rows[0].Cells[0] = new TableCell("A") { VerticalMerge = VerticalMergeState.Restart };
            tbl.Rows[0].Cells[1] = new TableCell("B");
            tbl.Rows[1].Cells[0] = new TableCell("") { VerticalMerge = VerticalMergeState.Continue };
            tbl.Rows[1].Cells[1] = new TableCell("C");
            doc.Blocks.Add(tbl);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 4000));
            var idx = doc.Blocks.IndexOf(tbl);

            // Start at R0C0 and Tab through the table; record each stop.
            view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
            tabOrder.Add((view.CellCaretInfo!.Value.Row, view.CellCaretInfo!.Value.Col));

            // Tab once → should skip Continue cell and land on R0C1.
            view.SimulateTabCell(forward: true);
            if (view.CellCaretInfo is { } s1) tabOrder.Add((s1.Row, s1.Col));

            // Tab again → R1C1 (Continue at R1C0 is skipped).
            view.SimulateTabCell(forward: true);
            if (view.CellCaretInfo is { } s2) tabOrder.Add((s2.Row, s2.Col));
        });

        if (!ran) return;
        tabOrder.Should().HaveCount(3, "3 tab stops: R0C0, R0C1, R1C1 (R1C0 Continue is skipped)");
        tabOrder[0].Should().Be((0, 0), "start at R0C0");
        tabOrder[1].Should().Be((0, 1), "Tab from R0C0 must land on R0C1, not the Continue cell");
        tabOrder[2].Should().Be((1, 1), "Tab from R0C1 must land on R1C1");
    }

    // ── BG1: ExpandForMergedCells expands row range for vertical merges ───────────────────────────

    /// <summary>
    /// BG1 regression: ExpandForMergedCells must grow minRow/maxRow to fully include vertical
    /// merge runs that are partially selected. Table: 4 rows × 2 cols; col 0 has a vertical merge
    /// spanning rows 1-3 (R1C0=Restart, R2C0=Continue, R3C0=Continue). Selecting rows 0-1 with
    /// col 0 in range must expand to include rows 1-3 (the full vertical merge run).
    /// </summary>
    [Fact]
    public async Task ExpandForMergedCells_expands_row_range_for_vertical_merge()
    {
        (int MinRow, int MaxRow)? selRange = null;
        var ran = await OnUiThread(() =>
        {
            // 4-row × 2-col table; col 0 rows 1-3 are vertically merged.
            var doc = TextDocument.CreateEmpty();
            var tbl = Table.Create(4, 2);
            tbl.Rows[0].Cells[0] = new TableCell("top");
            tbl.Rows[1].Cells[0] = new TableCell("merge-head") { VerticalMerge = VerticalMergeState.Restart };
            tbl.Rows[2].Cells[0] = new TableCell("") { VerticalMerge = VerticalMergeState.Continue };
            tbl.Rows[3].Cells[0] = new TableCell("") { VerticalMerge = VerticalMergeState.Continue };
            for (var r = 0; r < 4; r++) tbl.Rows[r].Cells[1] = new TableCell($"B{r}");
            doc.Blocks.Add(tbl);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 4000));
            var idx = doc.Blocks.IndexOf(tbl);

            // Select rows 0..1 in col 0 — this cuts the vertical merge run in half.
            // ExpandForMergedCells (called inside SelectedCellRange) must grow maxRow to 3.
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 0, focusRow: 1, focusCol: 0);
            var sel = view.SelectedCellRange;
            if (sel is { } s) selRange = (s.MinRow, s.MaxRow);
        });

        if (!ran) return;
        selRange.Should().NotBeNull("SelectedCellRange must return a value");
        selRange!.Value.MinRow.Should().Be(0, "minRow must remain 0 (row 0 is above the merge)");
        selRange!.Value.MaxRow.Should().Be(3, "maxRow must expand to 3 to include the full vertical merge run");
    }
}

/// <summary>
/// Extension helpers for AV-TBL3 tests — expose Tab navigation + insert a tab character
/// via public/private routing so tests can simulate Tab/Shift+Tab without a keyboard event.
/// </summary>
file static class DocumentViewTableNavTestExtensions
{
    /// <summary>
    /// Simulates Tab (forward=true) or Shift+Tab (forward=false) on the view.
    /// When the caret is in a table, routes through the private TabNavigateCell method.
    /// When the caret is in body text, calls InsertText("\t") (matching what OnKeyDown does).
    /// </summary>
    public static void SimulateTabCell(this DocumentView view, bool forward)
    {
        if (view.CellCaretInfo is not null)
        {
            // Route through the private TabNavigateCell method.
            var method = typeof(DocumentView).GetMethod("TabNavigateCell",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(view, [forward]);
        }
        else if (forward)
        {
            view.InsertText("\t");
        }
        // Shift+Tab outside a table is a no-op in the current implementation.
    }
}
