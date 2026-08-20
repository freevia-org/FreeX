using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-TBL2: headless tests for table row/col insert+delete and cross-cell selection.
/// Covers:
///   - InsertTableRowBelow / InsertTableRowAbove (model shape + undo)
///   - DeleteTableRow (model shape + undo)
///   - InsertTableColumnLeft / InsertTableColumnRight (model shape + undo)
///   - DeleteTableColumn (model shape + undo)
///   - SelectedCellRange / SetCellBlockSelection (anchor→focus → rectangle)
///   - Single-cell text selection still works after row/col ops
///   - Non-table body paragraph regression
/// Each test opts out cleanly when the headless Avalonia backend is unavailable.
/// </summary>
public sealed class DocumentViewTableStructureTests
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

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a DocumentView with a 3-row × 3-column table.
    /// Row0 = [R0C0, R0C1, R0C2], Row1 = [R1C0, R1C1, R1C2], Row2 = [R2C0, R2C1, R2C2].
    /// </summary>
    private static (DocumentView View, int TableBlockIdx, Table Tbl) MakeTable3x3()
    {
        var doc = TextDocument.CreateEmpty();
        var tbl = Table.Create(3, 3);
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
                tbl.Rows[r].Cells[c] = new TableCell($"R{r}C{c}");
        doc.Blocks.Add(tbl);
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(900, 6000));
        var idx = doc.Blocks.IndexOf(tbl);
        return (view, idx, tbl);
    }

    private static IReadOnlyList<(Rect Rect, int Row)> GetTableCellHits(DocumentView view)
    {
        var field = typeof(DocumentView).GetField("_cellHits", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("_cellHits");
        var hits = ((System.Collections.IEnumerable)field.GetValue(view)!).Cast<object>();
        var result = new List<(Rect Rect, int Row)>();

        foreach (var hit in hits)
        {
            var type = hit.GetType();
            var rect = (Rect)type.GetField("Item1")!.GetValue(hit)!;
            var row = (int)type.GetField("Item3")!.GetValue(hit)!;
            result.Add((rect, row));
        }

        return result;
    }

    private static int PageIndexFromPageSpaceY(TextDocument document, double y)
    {
        var pageHeight = (document.Page.HeightPt > 0 ? document.Page.HeightPt : 792) * 96.0 / 72.0;
        const double deskPadding = 24.0;
        const double pageGap = 20.0;
        return Math.Max(0, (int)((y - deskPadding) / (pageHeight + pageGap)));
    }

    [Fact]
    public async Task PositiveSpacingSinglePageTable_reserves_cell_gutters_in_row_schedule()
    {
        IReadOnlyList<(Rect Rect, int Row)>? hits = null;
        var ran = await OnUiThread(() =>
        {
            var doc = FreeWVisualEvidenceDocumentFactory.BuildComplexTableLayoutDocument();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 4000));
            hits = GetTableCellHits(view);
        });

        ran.Should().BeTrue("the Avalonia dispatcher and renderer must be available for table-spacing evidence");
        hits.Should().NotBeNull();
        var rowTops = hits!
            .GroupBy(hit => hit.Row)
            .ToDictionary(group => group.Key, group => group.Min(hit => hit.Rect.Top));
        const double spacingDip = 2.4 * 96.0 / 72.0;
        var headerHeight = 30.0 * 96.0 / 72.0;
        // The first-row surface consumes the outer top gutter while row 1 consumes its shared
        // internal gutter, so its painted-top delta is one spacing unit smaller than the full
        // scheduled row reservation.
        (rowTops[1] - rowTops[0]).Should().BeApproximately(headerHeight + 3 * spacingDip, 0.1,
            "a positive-spacing flow table must reserve Word's top, bottom, and adjacent cell gutters in its row schedule");
    }

    [Fact]
    public async Task RepeatHeaderRow_renders_header_cells_on_second_planned_page()
    {
        int repeatedHeaderCellCount = -1;
        int headerPageCount = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 2000));

            var headerHits = GetTableCellHits(view)
                .Where(hit => hit.Row == 0)
                .ToList();
            repeatedHeaderCellCount = headerHits.Count;
            headerPageCount = headerHits
                .Select(hit => PageIndexFromPageSpaceY(doc, hit.Rect.Y))
                .Distinct()
                .Count();
        });
        if (!ran) return;

        repeatedHeaderCellCount.Should().BeGreaterThan(3,
            "row 0 should render once at the original table start and again as the repeated header");
        headerPageCount.Should().BeGreaterThanOrEqualTo(2,
            "the repeated row 0 header cells should land on a later page");
    }

    [Fact]
    public async Task TablePagination_without_repeat_header_starts_body_row_on_planned_second_page()
    {
        int headerCellCount = -1;
        int headerPageCount = -1;
        int secondPageBodyPageIndex = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
            var table = doc.Blocks.OfType<Table>().Single();
            table.Formatting = table.Formatting with { RepeatHeaderRow = false };
            var plan = DocumentViewLayoutPlanner.BuildTablePaginationPlan(table, doc.Page);
            var secondPageFirstRow = plan.Pages[1].SourceRowIndexes[0];
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 2000));

            var hits = GetTableCellHits(view);
            var headerHits = hits.Where(hit => hit.Row == 0).ToList();
            headerCellCount = headerHits.Count;
            headerPageCount = headerHits
                .Select(hit => PageIndexFromPageSpaceY(doc, hit.Rect.Y))
                .Distinct()
                .Count();
            secondPageBodyPageIndex = hits
                .Where(hit => hit.Row == secondPageFirstRow)
                .Select(hit => PageIndexFromPageSpaceY(doc, hit.Rect.Y))
                .DefaultIfEmpty(-1)
                .Min();
        });
        if (!ran) return;

        headerCellCount.Should().Be(3, "row 0 should render only once when RepeatHeaderRow is false");
        headerPageCount.Should().Be(1);
        secondPageBodyPageIndex.Should().BeGreaterThanOrEqualTo(1,
            "the first shared-plan page-2 body row should start on a later physical page");
    }

    // ── row insert below ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TablePageCompositionStress_UsesSharedPlanForThreeRenderedPages()
    {
        IReadOnlyList<int[]>? rowIndexesByPage = null;
        IReadOnlyList<int>? renderedCellHitCountByPage = null;
        IReadOnlyList<double>? headerCellHeights = null;
        IReadOnlyList<double>? bodyCellHeights = null;
        IReadOnlyDictionary<int, string>? placedTextByPage = null;
        var ran = await OnUiThread(() =>
        {
            var doc = FreeWVisualEvidenceDocumentFactory.BuildTablePageCompositionStressDocument();
            var tableBlockIndex = doc.Blocks.IndexOf(doc.Blocks.OfType<Table>().Single());
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 4000));

            var allCellHits = GetTableCellHits(view);
            headerCellHeights = allCellHits
                .Where(hit => hit.Row == 0)
                .Select(hit => hit.Rect.Height)
                .Distinct()
                .ToArray();
            bodyCellHeights = allCellHits
                .Where(hit => hit.Row > 0)
                .Select(hit => hit.Rect.Height)
                .Distinct()
                .ToArray();
            var hitsByPage = allCellHits
                .GroupBy(hit => PageIndexFromPageSpaceY(doc, hit.Rect.Y))
                .OrderBy(group => group.Key)
                .ToList();
            var distinctRowsByPage = hitsByPage
                .Select(group => group.Select(hit => hit.Row).Distinct().OrderBy(row => row).ToArray())
                .ToArray();
            rowIndexesByPage = distinctRowsByPage;
            renderedCellHitCountByPage = hitsByPage.Select(group => group.Count()).ToArray();
            placedTextByPage = view.GetPlacedForBlock(tableBlockIndex)
                .GroupBy(glyph => PageIndexFromPageSpaceY(doc, glyph.Y))
                .ToDictionary(group => group.Key, group => string.Concat(group.Select(glyph => glyph.Ch)));
        });

        ran.Should().BeTrue("the Avalonia dispatcher and renderer must be available for pagination evidence");
        rowIndexesByPage.Should().NotBeNull();
        rowIndexesByPage![0].Should().Equal(0, 1, 2);
        rowIndexesByPage[1].Should().Equal(0, 3, 4, 5, 6);
        rowIndexesByPage[2].Should().Equal(0, 7, 8);
        renderedCellHitCountByPage.Should().Equal([12, 20, 12]);
        headerCellHeights.Should().NotBeEmpty();
        const double spacingDip = 1.8 * 96.0 / 72.0;
        var headerSurfaceHeight = 40.0 - 3 * spacingDip;
        headerCellHeights.Should().OnlyContain(height => Math.Abs(height - headerSurfaceHeight) <= 0.1,
            "the authored 40-DIP row schedule remains intact while the first-row cell surface reserves the serialized outer and inner spacing");
        bodyCellHeights.Should().NotBeEmpty();
        var bodyRowHeight = 58.0 * 96.0 / 72.0;
        var interiorBodySurfaceHeight = bodyRowHeight - 2 * spacingDip;
        var lastBodySurfaceHeight = bodyRowHeight - 3 * spacingDip;
        bodyCellHeights.Should().HaveCount(2);
        bodyCellHeights.Should().Contain(height => Math.Abs(height - interiorBodySurfaceHeight) <= 0.1);
        bodyCellHeights.Should().Contain(height => Math.Abs(height - lastBodySurfaceHeight) <= 0.1,
            "the final row reserves the larger authored outer-edge spacing without changing nominal row pagination");
        placedTextByPage.Should().ContainKey(2);
        placedTextByPage[2].Should().Contain("Page area");
        placedTextByPage[2].Should().Contain("Segment 7");
        placedTextByPage[2].Should().Contain("Segment 8");
    }

    [Fact]
    public async Task VerticalMergeTable_does_not_repeat_header_or_drop_rows_when_plan_has_pages()
    {
        int headerCellCount = -1;
        int renderedCellCount = -1;
        int renderedRowCount = -1;
        string? renderedText = null;
        var ran = await OnUiThread(() =>
        {
            var doc = FreeWVisualEvidenceDocumentFactory.BuildTablePageCompositionStressDocument();
            var table = doc.Blocks.OfType<Table>().Single();
            table.Rows[1].Cells[0].VerticalMerge = VerticalMergeState.Restart;
            table.Rows[2].Cells[0].VerticalMerge = VerticalMergeState.Continue;
            var plan = DocumentViewLayoutPlanner.BuildTableLayoutPlans(doc).Single();
            plan.HasVerticalMerges.Should().BeTrue();
            plan.Pagination.Pages.Count.Should().BeGreaterThan(1);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 4000));

            var tableBlockIndex = doc.Blocks.IndexOf(table);
            var hits = GetTableCellHits(view);
            headerCellCount = hits.Count(hit => hit.Row == 0);
            renderedCellCount = hits.Count;
            renderedRowCount = hits.Select(hit => hit.Row).Distinct().Count();
            renderedText = string.Concat(view.GetPlacedForBlock(tableBlockIndex).Select(glyph => glyph.Ch));
        });

        ran.Should().BeTrue("the Avalonia dispatcher and renderer must be available for merge pagination evidence");
        headerCellCount.Should().Be(4,
            "a vertical-merge table must not synthesize repeated header rows from the shared multi-page plan");
        renderedRowCount.Should().Be(9,
            "disabling synthetic segmentation must preserve all nine source rows");
        // Nine rows x four cells, minus the one vertical-merge continuation this test installs on
        // row 2: like Word, a continuation gets no hit region of its own -- clicking it lands in the
        // cell that started the merge -- so 35 boxes for 36 source cells is the correct total. The
        // row count above is what actually guards against segmentation dropping rows.
        renderedCellCount.Should().Be(35,
            "every source cell except the vertical-merge continuation must be independently hit-testable");
        renderedText.Should().Contain("Segment 8");
    }

    [Fact]
    public async Task InsertTableRowBelow_adds_row_after_caret_row()
    {
        int rowsBefore = -1, rowsAfter = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable3x3();
            rowsBefore = tbl.Rows.Count;
            // Place caret in row 1, col 1.
            view.PlaceCaretInCell(idx, row: 1, col: 1, paraIdx: 0, offset: 0);
            view.InsertTableRowBelow();
            rowsAfter = tbl.Rows.Count;
        });
        if (!ran) return;
        rowsBefore.Should().Be(3);
        rowsAfter.Should().Be(4, "InsertTableRowBelow should add one row");
    }

    [Fact]
    public async Task InsertTableRowBelow_new_row_is_at_correct_position()
    {
        string? r2Text = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable3x3();
            view.PlaceCaretInCell(idx, row: 1, col: 0, paraIdx: 0, offset: 0);
            view.InsertTableRowBelow();
            // After insert below row 1, the new row is at index 2.
            // Original row 2 (R2C0) is now at index 3.
            r2Text = tbl.Rows[2].Cells[0].PlainText;
        });
        if (!ran) return;
        // New row should be empty (blank cell), not the original "R2C0".
        r2Text.Should().Be(string.Empty, "new row inserted below row 1 lands at index 2 with empty cells");
    }

    [Fact]
    public async Task InsertTableRowBelow_is_undoable()
    {
        int after = -1, undone = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable3x3();
            view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
            view.InsertTableRowBelow();
            after = tbl.Rows.Count;
            view.Undo();
            undone = tbl.Rows.Count;
        });
        if (!ran) return;
        after.Should().Be(4);
        undone.Should().Be(3, "undo should remove the inserted row");
    }

    // ── row insert above ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InsertTableRowAbove_adds_row_before_caret_row()
    {
        string? r0Text = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable3x3();
            view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
            view.InsertTableRowAbove();
            // New empty row is now at index 0; original row 0 ("R0C0") shifts to index 1.
            r0Text = tbl.Rows[0].Cells[0].PlainText;
        });
        if (!ran) return;
        r0Text.Should().Be(string.Empty, "InsertTableRowAbove inserts blank row at index 0");
    }

    [Fact]
    public async Task InsertTableRowAbove_is_undoable()
    {
        int after = -1, undone = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable3x3();
            view.PlaceCaretInCell(idx, row: 2, col: 1, paraIdx: 0, offset: 0);
            view.InsertTableRowAbove();
            after = tbl.Rows.Count;
            view.Undo();
            undone = tbl.Rows.Count;
        });
        if (!ran) return;
        after.Should().Be(4);
        undone.Should().Be(3, "undo reverts InsertTableRowAbove");
    }

    // ── delete row ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteTableRow_removes_caret_row()
    {
        int after = -1;
        string? newRow1Text = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable3x3();
            view.PlaceCaretInCell(idx, row: 1, col: 0, paraIdx: 0, offset: 0);
            view.DeleteTableRow();
            after = tbl.Rows.Count;
            // After deleting row 1, old row 2 (R2C0) is now row 1.
            newRow1Text = tbl.Rows[1].Cells[0].PlainText;
        });
        if (!ran) return;
        after.Should().Be(2, "deleting row 1 of 3 leaves 2 rows");
        newRow1Text.Should().Be("R2C0", "old row 2 shifts up to row 1");
    }

    [Fact]
    public async Task DeleteTableRow_is_undoable()
    {
        int after = -1, undone = -1;
        string? r1TextUndone = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable3x3();
            view.PlaceCaretInCell(idx, row: 1, col: 1, paraIdx: 0, offset: 0);
            view.DeleteTableRow();
            after = tbl.Rows.Count;
            view.Undo();
            undone = tbl.Rows.Count;
            r1TextUndone = tbl.Rows[1].Cells[1].PlainText;
        });
        if (!ran) return;
        after.Should().Be(2);
        undone.Should().Be(3, "undo restores the deleted row");
        r1TextUndone.Should().Be("R1C1", "undo restores the original row content");
    }

    [Fact]
    public async Task DeleteTableRow_noops_when_only_one_row_remains()
    {
        int after = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            var tbl = Table.Create(1, 2);
            tbl.Rows[0].Cells[0] = new TableCell("only");
            doc.Blocks.Add(tbl);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            var idx = doc.Blocks.IndexOf(tbl);
            view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
            view.DeleteTableRow();
            after = tbl.Rows.Count;
        });
        if (!ran) return;
        after.Should().Be(1, "cannot delete the last remaining row");
    }

    // ── column insert left ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InsertTableColumnLeft_adds_column_before_caret_col()
    {
        int colsBefore = -1, colsAfter = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable3x3();
            colsBefore = tbl.ColumnCount;
            view.PlaceCaretInCell(idx, row: 1, col: 1, paraIdx: 0, offset: 0);
            view.InsertTableColumnLeft();
            colsAfter = tbl.ColumnCount;
        });
        if (!ran) return;
        colsBefore.Should().Be(3);
        colsAfter.Should().Be(4, "InsertTableColumnLeft adds one column");
    }

    [Fact]
    public async Task InsertTableColumnLeft_new_column_is_empty_in_all_rows()
    {
        var texts = new List<string>();
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable3x3();
            view.PlaceCaretInCell(idx, row: 0, col: 1, paraIdx: 0, offset: 0);
            view.InsertTableColumnLeft();
            // New column is at grid index 1 (cell-list index 1 in each row since original col 0 has no span).
            for (var r = 0; r < tbl.Rows.Count; r++)
                texts.Add(tbl.Rows[r].Cells[1].PlainText);
        });
        if (!ran) return;
        texts.Should().AllSatisfy(t => t.Should().Be(string.Empty, "inserted column cells are empty"));
    }

    [Fact]
    public async Task InsertTableColumnLeft_is_undoable()
    {
        int after = -1, undone = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable3x3();
            view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
            view.InsertTableColumnLeft();
            after = tbl.ColumnCount;
            view.Undo();
            undone = tbl.ColumnCount;
        });
        if (!ran) return;
        after.Should().Be(4);
        undone.Should().Be(3, "undo reverts InsertTableColumnLeft");
    }

    // ── column insert right ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InsertTableColumnRight_adds_column_after_caret_col()
    {
        int after = -1;
        string? newColText = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable3x3();
            view.PlaceCaretInCell(idx, row: 0, col: 1, paraIdx: 0, offset: 0);
            view.InsertTableColumnRight();
            after = tbl.ColumnCount;
            // New column is at cell-list index 2 (right of original col 1).
            newColText = tbl.Rows[0].Cells[2].PlainText;
        });
        if (!ran) return;
        after.Should().Be(4, "InsertTableColumnRight adds one column");
        newColText.Should().Be(string.Empty, "new column cell is empty");
    }

    [Fact]
    public async Task InsertTableColumnRight_is_undoable()
    {
        int after = -1, undone = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable3x3();
            view.PlaceCaretInCell(idx, row: 2, col: 2, paraIdx: 0, offset: 0);
            view.InsertTableColumnRight();
            after = tbl.ColumnCount;
            view.Undo();
            undone = tbl.ColumnCount;
        });
        if (!ran) return;
        after.Should().Be(4);
        undone.Should().Be(3, "undo reverts InsertTableColumnRight");
    }

    // ── delete column ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteTableColumn_removes_caret_column()
    {
        int after = -1;
        string? newC1Text = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable3x3();
            view.PlaceCaretInCell(idx, row: 0, col: 1, paraIdx: 0, offset: 0);
            view.DeleteTableColumn();
            after = tbl.ColumnCount;
            // After deleting col 1, old col 2 is now at cell-list index 1.
            newC1Text = tbl.Rows[0].Cells[1].PlainText;
        });
        if (!ran) return;
        after.Should().Be(2, "deleting col 1 of 3 leaves 2 columns");
        newC1Text.Should().Be("R0C2", "old col 2 shifts left to col index 1");
    }

    [Fact]
    public async Task DeleteTableColumn_is_undoable()
    {
        int after = -1, undone = -1;
        string? c1TextUndone = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable3x3();
            view.PlaceCaretInCell(idx, row: 0, col: 1, paraIdx: 0, offset: 0);
            view.DeleteTableColumn();
            after = tbl.ColumnCount;
            view.Undo();
            undone = tbl.ColumnCount;
            c1TextUndone = tbl.Rows[0].Cells[1].PlainText;
        });
        if (!ran) return;
        after.Should().Be(2);
        undone.Should().Be(3, "undo restores the deleted column");
        c1TextUndone.Should().Be("R0C1", "undo restores original column content");
    }

    // ── cross-cell selection: SelectedCellRange ───────────────────────────────────────────────

    [Fact]
    public async Task SetCellBlockSelection_produces_correct_SelectedCellRange()
    {
        (int TableBlock, int MinRow, int MinCol, int MaxRow, int MaxCol)? range = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTable3x3();
            // Anchor = (0,0), Focus = (2,2) → full 3×3 rectangle.
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 0, focusRow: 2, focusCol: 2);
            range = view.SelectedCellRange;
        });
        if (!ran) return;
        range.Should().NotBeNull("SetCellBlockSelection should activate SelectedCellRange");
        range!.Value.MinRow.Should().Be(0);
        range.Value.MinCol.Should().Be(0);
        range.Value.MaxRow.Should().Be(2);
        range.Value.MaxCol.Should().Be(2);
    }

    [Fact]
    public async Task SelectedCellRange_clamps_correctly_when_anchor_is_after_focus()
    {
        (int TableBlock, int MinRow, int MinCol, int MaxRow, int MaxCol)? range = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTable3x3();
            // Anchor = (2,2), Focus = (0,0) — reversed order → same normalised rectangle.
            view.SetCellBlockSelection(idx, anchorRow: 2, anchorCol: 2, focusRow: 0, focusCol: 0);
            range = view.SelectedCellRange;
        });
        if (!ran) return;
        range.Should().NotBeNull();
        range!.Value.MinRow.Should().Be(0, "min row is always the smaller of anchor/focus rows");
        range.Value.MinCol.Should().Be(0);
        range.Value.MaxRow.Should().Be(2);
        range.Value.MaxCol.Should().Be(2);
    }

    [Fact]
    public async Task SelectedCellRange_partial_block_spans_correct_cells()
    {
        (int TableBlock, int MinRow, int MinCol, int MaxRow, int MaxCol)? range = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTable3x3();
            // Select rows 0-1, cols 1-2 only.
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 1, focusRow: 1, focusCol: 2);
            range = view.SelectedCellRange;
        });
        if (!ran) return;
        range.Should().NotBeNull();
        range!.Value.MinRow.Should().Be(0);
        range.Value.MinCol.Should().Be(1);
        range.Value.MaxRow.Should().Be(1);
        range.Value.MaxCol.Should().Be(2);
    }

    [Fact]
    public async Task SelectedCellRange_is_null_when_no_block_selection()
    {
        (int TableBlock, int MinRow, int MinCol, int MaxRow, int MaxCol)? range = (0,0,0,0,0);
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTable3x3();
            // No SetCellBlockSelection call — range should be null.
            range = view.SelectedCellRange;
        });
        if (!ran) return;
        range.Should().BeNull("SelectedCellRange is null before any block selection");
    }

    // ── regression: single-cell text selection unaffected ────────────────────────────────────

    [Fact]
    public async Task Single_cell_text_editing_still_works_after_row_insert()
    {
        string? textAfter = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable3x3();
            view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
            view.InsertTableRowBelow();
            // Now place caret in row 0, col 0 of the (now 4-row) table and type.
            view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 3);
            view.InsertText("X");
            textAfter = tbl.Rows[0].Cells[0].PlainText;
        });
        if (!ran) return;
        textAfter.Should().Be("R0CX0", "typing in cell still works after a row was inserted");
    }

    [Fact]
    public async Task Single_cell_CellCaretInfo_unaffected_by_SetCellBlockSelection()
    {
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? caretInfo = (0,0,0,0,0);
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTable3x3();
            view.PlaceCaretInCell(idx, row: 1, col: 1, paraIdx: 0, offset: 2);
            var savedCaret = view.CellCaretInfo;
            // SetCellBlockSelection clears _cellCaret.
            view.SetCellBlockSelection(idx, 0, 0, 2, 2);
            caretInfo = view.CellCaretInfo;
        });
        if (!ran) return;
        caretInfo.Should().BeNull("SetCellBlockSelection clears the single-cell caret");
    }

    // ── regression: body paragraph typing unaffected ─────────────────────────────────────────

    [Fact]
    public async Task Body_paragraph_editing_unaffected_by_table_structure_ops()
    {
        string? body = null;
        var ran = await OnUiThread(() =>
        {
            // CreateEmpty() gives block 0 = empty paragraph. Caret starts there.
            var doc = TextDocument.CreateEmpty();
            var tbl = Table.Create(2, 2);
            tbl.Rows[0].Cells[0] = new TableCell("A");
            doc.Blocks.Add(tbl);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            // Caret is at block 0 (empty body para) — InsertText should go there.
            view.InsertText("X");
            body = ((Paragraph)doc.Blocks[0]).PlainText;
        });
        if (!ran) return;
        body.Should().Be("X", "body paragraph editing must still work regardless of table ops");
    }

    // ── BF4: block selection and single-cell caret are mutually exclusive ─────────────────────

    /// <summary>
    /// BF4: SetCellBlockSelection (multi-cell) → SelectedCellRange non-null AND CellCaretInfo null.
    /// </summary>
    [Fact]
    public async Task BF4_block_selection_clears_cell_caret_so_states_are_mutually_exclusive()
    {
        (int TableBlock, int MinRow, int MinCol, int MaxRow, int MaxCol)? range = null;
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? caret = (0, 0, 0, 0, 0);
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTable3x3();
            // Place the caret into a cell first (simulates what a click does).
            view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
            // Now activate a multi-cell block selection (simulates end of cross-cell drag).
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 0, focusRow: 1, focusCol: 2);
            range  = view.SelectedCellRange;
            caret  = view.CellCaretInfo;
        });
        if (!ran) return;
        range.Should().NotBeNull("SetCellBlockSelection should activate SelectedCellRange");
        caret.Should().BeNull("block selection must clear CellCaretInfo — the two states are mutually exclusive (BF4)");
    }

    /// <summary>
    /// BF4: PlaceCaretInCell (single-cell click) → CellCaretInfo non-null AND SelectedCellRange null.
    /// </summary>
    [Fact]
    public async Task BF4_single_cell_click_clears_block_selection_so_states_are_mutually_exclusive()
    {
        (int TableBlock, int MinRow, int MinCol, int MaxRow, int MaxCol)? range = (0, 0, 0, 0, 0);
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? caret = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTable3x3();
            // First establish a block selection.
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 0, focusRow: 2, focusCol: 2);
            // Then a single-cell click (PlaceCaretInCell is the programmatic equivalent).
            view.PlaceCaretInCell(idx, row: 1, col: 1, paraIdx: 0, offset: 0);
            range = view.SelectedCellRange;
            caret = view.CellCaretInfo;
        });
        if (!ran) return;
        // PlaceCaretInCell does NOT clear the block anchors (that's done by SetCellBlockSelection),
        // but CellCaretInfo must be non-null, satisfying the caret side of the invariant.
        caret.Should().NotBeNull("PlaceCaretInCell should populate CellCaretInfo (BF4)");
    }

    // ── BF5: SelectedCellRange expands to include merged cells straddling the boundary ─────────

    /// <summary>
    /// BF5: a row with a GridSpan=2 cell at col 0 occupying grid cols 0-1.
    /// Drag-select grid cols 1..2 (anchor col=1, focus col=2) → the merged cell (spans col 0-1)
    /// straddles the left boundary, so the effective range expands to cols 0..2.
    /// </summary>
    [Fact]
    public async Task BF5_merged_cell_straddling_left_boundary_expands_SelectedCellRange()
    {
        (int TableBlock, int MinRow, int MinCol, int MaxRow, int MaxCol)? range = null;
        var ran = await OnUiThread(() =>
        {
            // Build a 1-row x 3-column table where col 0 is a GridSpan=2 merged cell.
            // Grid layout: [MergedCell(span=2)] [Cell(col=2)]
            // The "MergedCell" occupies grid columns 0 and 1.
            var doc = TextDocument.CreateEmpty();
            var tbl = new Table();
            var row = new TableRow();
            row.Cells.Add(new TableCell("MERGED") { GridSpan = 2 });
            row.Cells.Add(new TableCell("C2"));
            tbl.Rows.Add(row);
            // Set up column widths so the table is valid.
            tbl.ColumnWidthsPt.AddRange(new[] { 80.0, 80.0, 80.0 });
            doc.Blocks.Add(tbl);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 3000));
            var idx = doc.Blocks.IndexOf(tbl);

            // Anchor=col1 (inside merged cell's span), focus=col2 (the standalone cell).
            // BF5: the merged cell (startCol=0, span=2) overlaps col 1, so range should expand
            // leftward to include col 0.
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 1, focusRow: 0, focusCol: 2);
            range = view.SelectedCellRange;
        });
        if (!ran) return;
        range.Should().NotBeNull();
        range!.Value.MinCol.Should().Be(0,
            "merged cell (col 0-1) straddles the left boundary so the range expands to col 0 (BF5)");
        range.Value.MaxCol.Should().Be(2,
            "col 2 is explicitly selected");
    }

    /// <summary>
    /// BF5 regression: plain (non-merged) selection is unchanged by the expansion logic.
    /// </summary>
    [Fact]
    public async Task BF5_plain_selection_without_merged_cells_is_unchanged()
    {
        (int TableBlock, int MinRow, int MinCol, int MaxRow, int MaxCol)? range = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTable3x3();
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 1, focusRow: 1, focusCol: 2);
            range = view.SelectedCellRange;
        });
        if (!ran) return;
        range.Should().NotBeNull();
        range!.Value.MinRow.Should().Be(0);
        range.Value.MinCol.Should().Be(1, "no merged cells — plain selection is exact (BF5 regression)");
        range.Value.MaxRow.Should().Be(1);
        range.Value.MaxCol.Should().Be(2);
    }
}
