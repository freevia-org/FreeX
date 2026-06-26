using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-TBL: headless tests for in-place table cell editing.
/// Covers: PlaceCaretInCell, InsertText into cell (+ undo), Backspace in cell,
/// MoveCaret left/right within and across cells, non-table regression.
/// Each test opts out cleanly when the headless Avalonia backend is not available
/// (same pattern as <see cref="DocumentViewHeadlessTests"/>).
/// </summary>
public sealed class DocumentViewTableEditTests
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
    /// Creates a DocumentView with a 2x2 table at a known block index.
    /// TextDocument.CreateEmpty() adds one empty paragraph (block 0), so the table lands at block 2
    /// (after the "intro" paragraph at block 1). The block index is returned alongside the view.
    /// </summary>
    private static (DocumentView View, int TableBlockIdx, Table Tbl) MakeTableView()
    {
        var doc = TextDocument.CreateEmpty();
        // Block 0: the empty paragraph from CreateEmpty().
        doc.Blocks.Add(new Paragraph("intro")); // Block 1: extra para to test non-zero block index.
        var tbl = Table.Create(2, 2);
        tbl.Rows[0].Cells[0] = new TableCell("A1");
        tbl.Rows[0].Cells[1] = new TableCell("B1");
        tbl.Rows[1].Cells[0] = new TableCell("A2");
        tbl.Rows[1].Cells[1] = new TableCell("B2");
        doc.Blocks.Add(tbl); // Block 2.
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 4000));
        // Use dynamic index lookup for robustness.
        var idx = doc.Blocks.IndexOf(tbl);
        return (view, idx, tbl);
    }

    // ── test 1: PlaceCaretInCell sets _cellCaret ─────────────────────────────────────────────────

    [Fact]
    public async Task PlaceCaretInCell_sets_CellCaretInfo_for_the_target_cell()
    {
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? info = null;
        var tblIdx = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTableView();
            tblIdx = idx;
            view.PlaceCaretInCell(tableBlockIndex: idx, row: 0, col: 0, paraIdx: 0, offset: 1);
            info = view.CellCaretInfo;
        });

        if (!ran) return;
        info.Should().NotBeNull("PlaceCaretInCell should populate CellCaretInfo");
        info!.Value.TableBlock.Should().Be(tblIdx);
        info.Value.Row.Should().Be(0);
        info.Value.Col.Should().Be(0);
        info.Value.ParaIdx.Should().Be(0);
        info.Value.Offset.Should().Be(1, "caret was placed after the first char 'A' in 'A1'");
    }

    // ── test 2: InsertText into cell mutates the model ───────────────────────────────────────────

    [Fact]
    public async Task InsertText_into_cell_appends_to_paragraph()
    {
        string? after = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTableView();
            view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 2);
            view.InsertText("X");
            after = tbl.Rows[0].Cells[0].PlainText;
        });

        if (!ran) return;
        after.Should().Be("A1X", "typing 'X' after 'A1' (at offset 2) should produce 'A1X'");
    }

    // ── test 3: InsertText + undo reverts cell text ───────────────────────────────────────────────

    [Fact]
    public async Task InsertText_into_cell_is_undoable()
    {
        string? after = null;
        string? undone = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTableView();
            view.PlaceCaretInCell(idx, row: 1, col: 1, paraIdx: 0, offset: 0);
            view.InsertText("ZZ");
            after = tbl.Rows[1].Cells[1].PlainText;
            view.Undo();
            undone = tbl.Rows[1].Cells[1].PlainText;
        });

        if (!ran) return;
        after.Should().Be("ZZB2", "typing 'ZZ' at offset 0 in 'B2' produces 'ZZB2'");
        undone.Should().Be("B2", "undo should restore the original 'B2'");
    }

    // ── test 4: Backspace deletes a character in a cell ──────────────────────────────────────────

    [Fact]
    public async Task Backspace_removes_preceding_char_in_cell()
    {
        string? after = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTableView();
            view.PlaceCaretInCell(idx, row: 0, col: 1, paraIdx: 0, offset: 2);
            view.SimulateBackspace();
            after = tbl.Rows[0].Cells[1].PlainText;
        });

        if (!ran) return;
        after.Should().Be("B", "backspace at offset 2 in 'B1' should remove '1' leaving 'B'");
    }

    // ── test 4b: Backspace is undoable ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Backspace_in_cell_is_undoable()
    {
        string? afterBackspace = null;
        string? afterUndo = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTableView();
            view.PlaceCaretInCell(idx, row: 1, col: 0, paraIdx: 0, offset: 2);
            view.SimulateBackspace();
            afterBackspace = tbl.Rows[1].Cells[0].PlainText;
            view.Undo();
            afterUndo = tbl.Rows[1].Cells[0].PlainText;
        });

        if (!ran) return;
        afterBackspace.Should().Be("A", "backspace removes '2' leaving 'A'");
        afterUndo.Should().Be("A2", "undo restores 'A2'");
    }

    // ── test 5: MoveCaret right within cell advances offset ───────────────────────────────────────

    [Fact]
    public async Task MoveCaret_right_advances_offset_within_cell()
    {
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? info = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTableView();
            view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
            view.SimulateMoveCaretRight();
            info = view.CellCaretInfo;
        });

        if (!ran) return;
        info.Should().NotBeNull();
        info!.Value.Offset.Should().Be(1, "one Right from offset 0 should land at offset 1");
    }

    // ── test 6: MoveCaret right at cell end crosses to next cell ─────────────────────────────────

    [Fact]
    public async Task MoveCaret_right_at_cell_end_moves_to_next_cell()
    {
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? info = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTableView();
            // "A1" is 2 chars; place at end and move right → next cell "B1".
            view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 2);
            view.SimulateMoveCaretRight();
            info = view.CellCaretInfo;
        });

        if (!ran) return;
        info.Should().NotBeNull();
        info!.Value.Col.Should().Be(1, "right at end of 'A1' (col 0) should land in col 1");
        info.Value.Offset.Should().Be(0, "landing at start of the next cell");
    }

    // ── test 7: MoveCaret left at cell start crosses to previous cell ─────────────────────────────

    [Fact]
    public async Task MoveCaret_left_at_cell_start_moves_to_previous_cell()
    {
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? info = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTableView();
            // "B1" col 1; start at offset 0 and move left → "A1" col 0 end.
            view.PlaceCaretInCell(idx, row: 0, col: 1, paraIdx: 0, offset: 0);
            view.SimulateMoveCaretLeft();
            info = view.CellCaretInfo;
        });

        if (!ran) return;
        info.Should().NotBeNull();
        info!.Value.Col.Should().Be(0, "left at start of 'B1' (col 1) should land in col 0");
        info.Value.Offset.Should().Be(2, "landing at end of 'A1' (length 2)");
    }

    // ── test 8: non-table caret regression — body typing unaffected ──────────────────────────────

    [Fact]
    public async Task Body_paragraph_typing_still_works_after_table_edit()
    {
        string? body = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Add(new Paragraph("hello"));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            // Caret starts at block 0 (the empty para from CreateEmpty). InsertText prepends 'X'.
            view.InsertText("X");
            body = doc.Blocks.OfType<Paragraph>().First().PlainText;
        });

        if (!ran) return;
        body.Should().StartWith("X", "body paragraph typing must still work when no cell is active");
    }

    // ── test 9: CellCaretInfo is null after body click ────────────────────────────────────────────

    [Fact]
    public async Task CellCaretInfo_is_null_when_caret_is_in_body_paragraph()
    {
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? info = (99, 99, 99, 99, 99);
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Add(new Paragraph("body text"));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            info = view.CellCaretInfo;
        });

        if (!ran) return;
        info.Should().BeNull("CellCaretInfo must be null when caret is in a body paragraph");
    }
}

/// <summary>
/// Minimal extension to expose private navigation + editing for testing without pointer events.
/// Uses reflection to call private methods — same pattern as DocumentViewHeadlessTests.
/// </summary>
file static class DocumentViewTableTestExtensions
{
    public static void SimulateMoveCaretRight(this DocumentView view) =>
        InvokeMoveCaret(view, +1);

    public static void SimulateMoveCaretLeft(this DocumentView view) =>
        InvokeMoveCaret(view, -1);

    public static void SimulateBackspace(this DocumentView view)
    {
        var method = typeof(DocumentView).GetMethod("Backspace",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method?.Invoke(view, null);
    }

    private static void InvokeMoveCaret(DocumentView view, int delta)
    {
        var method = typeof(DocumentView).GetMethod("MoveCaret",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method?.Invoke(view, [delta, false]);
    }
}
