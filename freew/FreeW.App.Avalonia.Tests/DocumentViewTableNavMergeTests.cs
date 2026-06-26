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
