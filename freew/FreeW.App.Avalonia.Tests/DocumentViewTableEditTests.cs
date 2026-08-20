using System.Collections.Generic;
using System.Linq;
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

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

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

    [Fact]
    public async Task Floating_table_offsets_move_the_complete_cell_surface()
    {
        Rect inlineRect = default;
        Rect floatingRect = default;
        var ran = await OnUiThread(() =>
        {
            static Rect RenderFirstCell(TableFloatingPosition? position)
            {
                var document = TextDocument.CreateEmpty();
                document.Blocks.Clear();
                var table = Table.Create(1, 1);
                table.Rows[0].Cells[0] = new TableCell("positioned");
                table.ColumnWidthsPt.Add(120);
                table.FloatingPosition = position;
                document.Blocks.Add(table);

                var view = new DocumentView();
                view.LoadDocument(document);
                view.Measure(new Size(816, 2000));
                return view.TableCellRects.Single().Rect;
            }

            inlineRect = RenderFirstCell(position: null);
            floatingRect = RenderFirstCell(new TableFloatingPosition(
                HorizontalAnchor: TableHorizontalAnchor.Text,
                VerticalAnchor: TableVerticalAnchor.Text,
                HorizontalOffsetPt: 36,
                VerticalOffsetPt: 24));
        });

        if (!ran) return;
        floatingRect.Left.Should().BeApproximately(inlineRect.Left + 48, 0.01);
        floatingRect.Top.Should().BeApproximately(inlineRect.Top + 32, 0.01);
        floatingRect.Width.Should().BeApproximately(inlineRect.Width, 0.01);
        floatingRect.Height.Should().BeApproximately(inlineRect.Height, 0.01);
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

    // ── BE2+BE1: multi-paragraph cell layout and per-paragraph sentinels ─────────────────────────

    /// <summary>
    /// BE2: a cell with 2 paragraphs ("ab" then "cd") must render on 2 distinct visual Y bands.
    /// BE1: each paragraph must have its own sentinel so the caret is findable at the end of each.
    /// </summary>
    [Fact]
    public async Task MultiParagraphCell_renders_paragraphs_on_separate_lines_with_per_para_sentinels()
    {
        IReadOnlyList<(char Ch, double X, double Y, double LineHeight, bool Sentinel, int ParaOffset)>? para0 = null;
        IReadOnlyList<(char Ch, double X, double Y, double LineHeight, bool Sentinel, int ParaOffset)>? para1 = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            var tbl = Table.Create(1, 1);
            // Cell with 2 paragraphs.
            var cell = new TableCell();
            cell.Paragraphs.Clear();
            cell.Paragraphs.Add(new Paragraph("ab"));
            cell.Paragraphs.Add(new Paragraph("cd"));
            tbl.Rows[0].Cells[0] = cell;
            doc.Blocks.Add(tbl);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            var tblIdx = doc.Blocks.IndexOf(tbl);
            para0 = view.GetCellPlaced(tblIdx, row: 0, col: 0, paraIdx: 0);
            para1 = view.GetCellPlaced(tblIdx, row: 0, col: 0, paraIdx: 1);
        });

        if (!ran) return;

        // BE2: paragraph 0 and paragraph 1 must be on different Y positions.
        var para0Y = para0!.Where(p => !p.Sentinel).Select(p => p.Y).Distinct().ToList();
        var para1Y = para1!.Where(p => !p.Sentinel).Select(p => p.Y).Distinct().ToList();
        para0Y.Should().NotBeEmpty("paragraph 0 must have placed chars");
        para1Y.Should().NotBeEmpty("paragraph 1 must have placed chars");
        para0Y[0].Should().BeLessThan(para1Y[0],
            "BE2: paragraph 0 must render ABOVE paragraph 1 (separate visual lines)");

        // BE1: each paragraph must have exactly one sentinel.
        para0!.Count(p => p.Sentinel).Should().Be(1, "BE1: paragraph 0 must have exactly one sentinel");
        para1!.Count(p => p.Sentinel).Should().Be(1, "BE1: paragraph 1 must have exactly one sentinel");

        // BE1: sentinel for para0 must be at para0's Y (end of 'ab' line).
        var sent0 = para0!.First(p => p.Sentinel);
        var sent1 = para1!.First(p => p.Sentinel);
        sent0.Y.Should().BeApproximately(para0Y[0], 2, "BE1: para0 sentinel must sit on para0's line");
        sent1.Y.Should().BeApproximately(para1Y[0], 2, "BE1: para1 sentinel must sit on para1's line");
    }

    [Fact]
    public async Task MultiPageTable_UsesLiveSectionAndSectionPagesFieldsWithoutMutatingCache()
    {
        string? visible = null;
        var pageCount = 0;
        var sectionField = Run.ComplexFieldRun(" SECTION \\* ROMAN ", "stale-section");
        var sectionPagesField = Run.ComplexFieldRun(" SECTIONPAGES ", "stale-pages");
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Section one")
            {
                SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage)
            });

            var table = Table.Create(55, 1);
            var firstParagraph = table.Rows[0].Cells[0].Paragraphs[0];
            firstParagraph.Runs.Clear();
            firstParagraph.Runs.Add(sectionField);
            firstParagraph.Runs.Add(new Run("/"));
            firstParagraph.Runs.Add(sectionPagesField);
            for (var row = 1; row < table.Rows.Count; row++)
            {
                table.Rows[row].Cells[0].Paragraphs.Clear();
                table.Rows[row].Cells[0].Paragraphs.Add(new Paragraph($"Table row {row + 1}"));
            }
            doc.Blocks.Add(table);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 6000));
            pageCount = view.PageCount;
            visible = string.Concat(view.GetCellPlaced(1, row: 0, col: 0, paraIdx: 0)
                .Where(item => !item.Sentinel)
                .Select(item => item.Ch));
        });

        if (!ran)
            return;

        pageCount.Should().BeGreaterThan(2);
        visible.Should().Be($"II/{pageCount - 1}");
        sectionField.Text.Should().Be("stale-section");
        sectionPagesField.Text.Should().Be("stale-pages");
    }

    // ── BE4: multi-char cell insertion is in-order ───────────────────────────────────────────────

    [Fact]
    public async Task MultiChar_InsertText_into_cell_inserts_in_order()
    {
        string? result = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTableView();
            // Place caret at offset 0 in cell A1 (text = "A1") and type "xyz".
            view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
            view.InsertText("xyz");
            result = tbl.Rows[0].Cells[0].Paragraphs[0].PlainText;
        });

        if (!ran) return;
        result.Should().Be("xyzA1", "BE4: 'xyz' typed at offset 0 should insert in-order, not reversed");
    }

    // ── BE3: typing over a cell selection replaces it ────────────────────────────────────────────

    [Fact]
    public async Task InsertText_over_cell_selection_replaces_selected_range()
    {
        string? result = null;
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? caretAfter = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTableView();
            // Cell A1 = "A1". Place caret at offset 2, anchor at offset 0 → selects "A1".
            view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 2);
            view.SetCellSelectionAnchorForTest(idx, row: 0, col: 0, paraIdx: 0, anchorOffset: 0);
            view.InsertText("Z");
            result = tbl.Rows[0].Cells[0].Paragraphs[0].PlainText;
            caretAfter = view.CellCaretInfo;
        });

        if (!ran) return;
        result.Should().Be("Z", "BE3: typing 'Z' over 'A1' selection should replace it with 'Z'");
        caretAfter!.Value.Offset.Should().Be(1, "BE3: caret should be after the inserted char");
    }

    [Fact]
    public async Task Backspace_over_cell_selection_deletes_selected_range()
    {
        string? result = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTableView();
            // Cell B1 = "B1". Select both chars: caret at 2, anchor at 0.
            view.PlaceCaretInCell(idx, row: 0, col: 1, paraIdx: 0, offset: 2);
            view.SetCellSelectionAnchorForTest(idx, row: 0, col: 1, paraIdx: 0, anchorOffset: 0);
            view.SimulateBackspace();
            result = tbl.Rows[0].Cells[1].Paragraphs[0].PlainText;
        });

        if (!ran) return;
        result.Should().Be("", "BE3: Backspace over 'B1' selection should delete the whole selection");
    }

    // ── BE5: DeleteSelection on a table block does NOT crash ─────────────────────────────────────

    [Fact]
    public async Task DeleteSelection_on_table_block_does_not_throw()
    {
        var ran = await OnUiThread(() =>
        {
            // Build: empty para (block 0) + table (block 1).
            var doc = TextDocument.CreateEmpty();
            var tbl = Table.Create(1, 1);
            tbl.Rows[0].Cells[0] = new TableCell("hello");
            doc.Blocks.Add(tbl);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));

            // Simulate a selection where _caret lands on the table block (block 1) and
            // _selectionAnchor is also on block 1 but at a different offset.
            // Invoking TryDeleteSelection should no-op safely without throwing.
            view.TryDeleteSelection();
        });

        // The test passes if OnUiThread did not throw.
        ran.Should().BeTrue("BE5: TryDeleteSelection must not throw when the block is a Table");
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
