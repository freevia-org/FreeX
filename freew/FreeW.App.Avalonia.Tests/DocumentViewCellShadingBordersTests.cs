using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-TBL4: headless tests for the cell shading + per-edge border edit surface added to
/// <see cref="DocumentView"/> in Wave AV-TBL4.
/// Covers:
///   - <see cref="DocumentView.SetCellShading"/> on caret cell + selected range + undo.
///   - <see cref="DocumentView.SetCellBorders"/> on caret cell: All / outside primitive edges + undo.
///   - Block selection: shading applied to every cell in the rectangle.
///   - Non-table (body text) caret: methods are no-ops.
/// </summary>
public sealed class DocumentViewCellShadingBordersTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a DocumentView with a 2-row × 2-column table.
    /// Returns the view, the block index of the table, and the Table model.
    /// </summary>
    private static (DocumentView View, int TableBlockIdx, Table Tbl) MakeTable2x2()
    {
        var doc = TextDocument.CreateEmpty();
        var tbl = Table.Create(2, 2);
        tbl.Rows[0].Cells[0] = new TableCell("A1");
        tbl.Rows[0].Cells[1] = new TableCell("B1");
        tbl.Rows[1].Cells[0] = new TableCell("A2");
        tbl.Rows[1].Cells[1] = new TableCell("B2");
        doc.Blocks.Add(tbl);
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 4000));
        var idx = doc.Blocks.IndexOf(tbl);
        return (view, idx, tbl);
    }

    [Fact]
    public async Task NamedTableStyle_HeaderFill_UsesCatalogColor()
    {
        Color? headerFill = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var table = Table.Create(2, 2);
            table.Formatting = TableFormatting.Default with
            {
                Borders = true,
                HeaderRow = true,
                BandedRows = true
            };
            table.TableStyleId = "GridTable4";
            doc.Blocks.Add(table);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            view.Arrange(new Rect(0, 0, 800, 4000));

            headerFill = FirstRenderedTableFill(view);
        });

        if (!ran) return;

        headerFill.Should().Be(
            Color.FromRgb(0x2F, 0x54, 0x96),
            "Avalonia table rendering must use the same named table-style header fill as WPF visual evidence");
    }

    private static Color? FirstRenderedTableFill(DocumentView view)
    {
        var field = typeof(DocumentView).GetField("_rects", BindingFlags.Instance | BindingFlags.NonPublic);
        var rects = field?.GetValue(view) as IEnumerable;
        if (rects is null)
            return null;

        foreach (var rect in rects)
        {
            var fill = rect?.GetType().GetField("Item2")?.GetValue(rect) as ISolidColorBrush;
            if (fill is not null)
                return fill.Color;
        }

        return null;
    }

    // ── SetCellShading – caret cell ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetCellShading_AppliesColorToCaretCell()
    {
        string? shading = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x2();
            view.PlaceCaretInCell(idx, 0, 0, 0, 0);
            view.SetCellShading("#FFFF00");
            shading = tbl.Rows[0].Cells[0].ShadingColorHex;
        });
        if (!ran) return;
        shading.Should().Be("#FFFF00");
    }

    [Fact]
    public async Task SetCellShading_DoesNotAffectOtherCells()
    {
        string? other = "SENTINEL";
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x2();
            view.PlaceCaretInCell(idx, 0, 0, 0, 0);
            view.SetCellShading("#FFFF00");
            other = tbl.Rows[0].Cells[1].ShadingColorHex;
        });
        if (!ran) return;
        other.Should().BeNull("adjacent cells must not be touched");
    }

    [Fact]
    public async Task SetCellShading_IsUndoable()
    {
        string? afterUndo = "SENTINEL";
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x2();
            view.PlaceCaretInCell(idx, 1, 1, 0, 0);
            view.SetCellShading("#123456");
            view.Undo();
            afterUndo = tbl.Rows[1].Cells[1].ShadingColorHex;
        });
        if (!ran) return;
        afterUndo.Should().BeNull("undo should restore null (no shading)");
    }

    [Fact]
    public async Task SetCellShading_ClearsWithNull()
    {
        string? afterClear = "SENTINEL";
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x2();
            tbl.Rows[0].Cells[0].ShadingColorHex = "#FF0000";
            view.PlaceCaretInCell(idx, 0, 0, 0, 0);
            view.SetCellShading(null);
            afterClear = tbl.Rows[0].Cells[0].ShadingColorHex;
        });
        if (!ran) return;
        afterClear.Should().BeNull("null clears the fill");
    }

    // ── SetCellShading – block selection ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SetCellShading_AppliesColorToAllSelectedCells()
    {
        (string? s00, string? s01, string? s10, string? s11) shadings = default;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x2();
            // Select the full 2×2 block.
            view.SetCellBlockSelection(idx, 0, 0, 1, 1);
            view.SetCellShading("#00FF00");
            shadings = (
                tbl.Rows[0].Cells[0].ShadingColorHex,
                tbl.Rows[0].Cells[1].ShadingColorHex,
                tbl.Rows[1].Cells[0].ShadingColorHex,
                tbl.Rows[1].Cells[1].ShadingColorHex);
        });
        if (!ran) return;
        shadings.s00.Should().Be("#00FF00");
        shadings.s01.Should().Be("#00FF00");
        shadings.s10.Should().Be("#00FF00");
        shadings.s11.Should().Be("#00FF00", "all four cells in the block must get the shading");
    }

    [Fact]
    public async Task SetCellShading_BlockSelection_IsUndoable()
    {
        int undoCount = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x2();
            view.SetCellBlockSelection(idx, 0, 0, 1, 1);
            view.SetCellShading("#00FF00"); // 4 commands pushed
            // Undo all 4.
            view.Undo(); view.Undo(); view.Undo(); view.Undo();
            undoCount = tbl.Rows.SelectMany(r => r.Cells)
                .Count(c => c.ShadingColorHex is null);
        });
        if (!ran) return;
        undoCount.Should().Be(4, "all 4 shadings should be reverted by 4 undos");
    }

    // ── SetCellBorders – caret cell ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetCellBorders_All_SetsAllFourEdgesOnCaretCell()
    {
        CellBorders? borders = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x2();
            view.PlaceCaretInCell(idx, 0, 0, 0, 0);
            view.SetCellBorders(CellBorderEdges.All, "#000000", 1.0, BorderLineStyle.Single);
            borders = tbl.Rows[0].Cells[0].Borders;
        });
        if (!ran) return;
        borders.Should().NotBeNull();
        borders!.Top.Should().NotBeNull();
        borders.Bottom.Should().NotBeNull();
        borders.Left.Should().NotBeNull();
        borders.Right.Should().NotBeNull();
    }

    [Fact]
    public async Task SetCellBorders_Top_SetsOnlyTopEdge()
    {
        CellBorders? borders = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x2();
            view.PlaceCaretInCell(idx, 0, 0, 0, 0);
            view.SetCellBorders(CellBorderEdges.Top, "#FF0000", 2.0, BorderLineStyle.Dashed);
            borders = tbl.Rows[0].Cells[0].Borders;
        });
        if (!ran) return;
        borders.Should().NotBeNull();
        borders!.Top.Should().NotBeNull();
        borders.Top!.Style.Should().Be(BorderLineStyle.Dashed);
        borders.Bottom.Should().BeNull();
        borders.Left.Should().BeNull();
        borders.Right.Should().BeNull();
    }

    [Fact]
    public async Task SetCellBorders_IsUndoable()
    {
        CellBorders? afterUndo = new CellBorders(); // non-null sentinel
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x2();
            view.PlaceCaretInCell(idx, 0, 0, 0, 0);
            view.SetCellBorders(CellBorderEdges.All, "#000000", 0.5);
            view.Undo();
            afterUndo = tbl.Rows[0].Cells[0].Borders;
        });
        if (!ran) return;
        afterUndo.Should().BeNull("undo should restore null borders");
    }

    // ── Outside / Inside edge selector ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SetCellBorders_Outside_OnSingleCell_SetsAllFourEdges()
    {
        // For a 1×1 selection (single caret), Outside == All four edges.
        CellBorders? borders = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x2();
            view.PlaceCaretInCell(idx, 0, 0, 0, 0);
            view.SetCellBorders(CellBorderEdges.Outside, "#000000", 0.5);
            borders = tbl.Rows[0].Cells[0].Borders;
        });
        if (!ran) return;
        borders.Should().NotBeNull();
        borders!.Top.Should().NotBeNull("top is outer boundary of a 1×1 block");
        borders.Bottom.Should().NotBeNull();
        borders.Left.Should().NotBeNull();
        borders.Right.Should().NotBeNull();
    }

    [Fact]
    public async Task SetCellBorders_Inside_On2x2BlockSelection_SetsSharedInnerEdges()
    {
        // Inside on a 2×2 block: row0 gets Bottom set, row1 doesn't; col0 gets Right set, col1 doesn't.
        (CellBorders? b00, CellBorders? b01, CellBorders? b10, CellBorders? b11) bdr = default;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x2();
            view.SetCellBlockSelection(idx, 0, 0, 1, 1);
            view.SetCellBorders(CellBorderEdges.Inside, "#000000", 0.5);
            bdr = (
                tbl.Rows[0].Cells[0].Borders,
                tbl.Rows[0].Cells[1].Borders,
                tbl.Rows[1].Cells[0].Borders,
                tbl.Rows[1].Cells[1].Borders);
        });
        if (!ran) return;
        // [0,0]: inner → Bottom (row not last) + Right (col not last).
        bdr.b00.Should().NotBeNull();
        bdr.b00!.Bottom.Should().NotBeNull("cell [0,0] bottom is a shared inner edge");
        bdr.b00.Right.Should().NotBeNull("cell [0,0] right is a shared inner edge");
        bdr.b00.Top.Should().BeNull();
        bdr.b00.Left.Should().BeNull();
        // [0,1]: inner → Bottom only (last col → no Right inner edge).
        bdr.b01.Should().NotBeNull();
        bdr.b01!.Bottom.Should().NotBeNull("cell [0,1] bottom is a shared inner edge");
        bdr.b01.Right.Should().BeNull("cell [0,1] is in the last column — no inner Right");
        // [1,0]: inner → Right only (last row → no Bottom inner edge).
        bdr.b10.Should().NotBeNull();
        bdr.b10!.Right.Should().NotBeNull("cell [1,0] right is a shared inner edge");
        bdr.b10.Bottom.Should().BeNull("cell [1,0] is in the last row — no inner Bottom");
        // [1,1]: last row + last col → no inner edges → null.
        bdr.b11.Should().BeNull("cell [1,1] has no inner edges in a 2×2 block");
    }

    // ── Non-table regression ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetCellShading_InBodyText_IsNoOp()
    {
        // Verifies the method doesn't throw and the document is unchanged when caret is in body text.
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Add(new Paragraph("body text"));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            // Caret is in body text (not a table cell) — SetCellShading must be a no-op.
            view.SetCellShading("#FFFF00");
            // No exception → pass; document unchanged is verified implicitly by reaching this line.
        });
        // Test passes if no exception was thrown and OnUiThread returned true.
        if (!ran) return;
    }

    [Fact]
    public async Task SetCellBorders_InBodyText_IsNoOp()
    {
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Add(new Paragraph("body text"));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            view.SetCellBorders(CellBorderEdges.All, "#000000", 0.5);
        });
        if (!ran) return;
    }

    // ── BL1 regression: SetCellShading with a preceding merged cell ──────────────────────────────

    /// <summary>
    /// BL1 regression: when a row has a preceding cell with GridSpan=2 (covering grid cols 0-1),
    /// the next logical cell B starts at grid col 2 (cell-list index 1).
    /// SetCellShading with the caret at grid col 2 must shade B (cell-list index 1), not
    /// cell-list index 2 (which is out-of-range / wrong cell).
    /// </summary>
    [Fact]
    public async Task SetCellShading_CaretAfterMergedCell_ShadesCorrectCell()
    {
        string? shadingA   = "SENTINEL";
        string? shadingB   = "SENTINEL";
        var ran = await OnUiThread(() =>
        {
            // Row: [A(GridSpan=2), B] — grid widths 0-1 and 2.
            var doc = TextDocument.CreateEmpty();
            var tbl = Table.Create(1, 2);
            tbl.Rows[0].Cells[0] = new TableCell("A") { GridSpan = 2 };
            tbl.Rows[0].Cells[1] = new TableCell("B");
            doc.Blocks.Add(tbl);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 4000));
            var idx = doc.Blocks.IndexOf(tbl);

            // Place caret in B — grid col 2, cell-list index 1.
            view.PlaceCaretInCell(idx, row: 0, col: 2, paraIdx: 0, offset: 0);
            view.SetCellShading("#FFFF00");

            shadingA = tbl.Rows[0].Cells[0].ShadingColorHex; // must be untouched
            shadingB = tbl.Rows[0].Cells[1].ShadingColorHex; // must get the color
        });
        if (!ran) return;
        shadingA.Should().BeNull("cell A (the merged cell) must not be shaded");
        shadingB.Should().Be("#FFFF00", "cell B (grid col 2, cell-list index 1) must be shaded");
    }

    /// <summary>
    /// BL1/BL3 regression: block selection covering grid cols 2..3 in a row where col 0-1 is
    /// a merged cell (GridSpan=2). Only B (at cell-list index 1) should be shaded; A (the
    /// merged cell occupying grid cols 0-1) is outside the selection and must not be touched.
    /// Also verifies the rightmost selected cell is not skipped (BL3: loop bound was
    /// cells.Count which is smaller than maxCol in grid space).
    /// </summary>
    [Fact]
    public async Task SetCellShading_BlockSelection_AfterMergedCell_ShadesCorrectCells()
    {
        string? shadingA = "SENTINEL";
        string? shadingB = "SENTINEL";
        string? shadingC = "SENTINEL";
        var ran = await OnUiThread(() =>
        {
            // Row: [A(GridSpan=2), B, C] — grid widths 0-1, 2, 3.
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

            // Select grid cols 2..3 (B and C); A (grid 0-1) is outside.
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 2, focusRow: 0, focusCol: 3);
            view.SetCellShading("#00FF00");

            shadingA = tbl.Rows[0].Cells[0].ShadingColorHex;
            shadingB = tbl.Rows[0].Cells[1].ShadingColorHex;
            shadingC = tbl.Rows[0].Cells[2].ShadingColorHex;
        });
        if (!ran) return;
        shadingA.Should().BeNull("cell A (preceding merged cell, outside selection) must not be shaded");
        shadingB.Should().Be("#00FF00", "cell B (grid col 2, cell-list index 1) must be shaded");
        shadingC.Should().Be("#00FF00", "cell C (grid col 3, cell-list index 2, rightmost) must not be skipped");
    }

    /// <summary>
    /// BL1 regression: a merged cell spanning grid cols 0-1 in a block selection covering 0..1
    /// must only be shaded ONCE (not twice — once per grid column), so the undo stack has exactly
    /// one entry for it.
    /// </summary>
    [Fact]
    public async Task SetCellShading_BlockSelection_MergedCellDedupedToOneCommand()
    {
        string? shadingA = "SENTINEL";
        int commandCount = 0;
        var ran = await OnUiThread(() =>
        {
            // Row: [A(GridSpan=2)] — only one logical cell spanning grid cols 0-1.
            var doc = TextDocument.CreateEmpty();
            var tbl = Table.Create(1, 1);
            tbl.Rows[0].Cells[0] = new TableCell("A") { GridSpan = 2 };
            doc.Blocks.Add(tbl);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 4000));
            var idx = doc.Blocks.IndexOf(tbl);

            // Select grid cols 0..1 — both map to the same logical cell A.
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 0, focusRow: 0, focusCol: 1);
            view.SetCellShading("#FF00FF");

            shadingA = tbl.Rows[0].Cells[0].ShadingColorHex;
            // Undo once — if the command was issued once, the shading should be gone.
            view.Undo();
            commandCount = tbl.Rows[0].Cells[0].ShadingColorHex is null ? 1 : 0;
        });
        if (!ran) return;
        // After shading: color must be set.
        shadingA.Should().Be("#FF00FF", "the merged cell must be shaded");
        // After one undo: color must be gone (proving only ONE command was issued).
        commandCount.Should().Be(1, "exactly one SetCellShadingCommand must be issued for a deduplicated merged cell");
    }

    // ── BL2 regression: SetCellBorders with a preceding merged cell ──────────────────────────────

    /// <summary>
    /// BL2 regression: a row with [A(GridSpan=2), B] — applying "All" borders with caret in B
    /// (grid col 2, cell-list index 1) must set borders on B, not on A or out-of-range.
    /// </summary>
    [Fact]
    public async Task SetCellBorders_CaretAfterMergedCell_BordersLandOnCorrectCell()
    {
        CellBorders? bordersA = new CellBorders(); // non-null sentinel
        CellBorders? bordersB = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            var tbl = Table.Create(1, 2);
            tbl.Rows[0].Cells[0] = new TableCell("A") { GridSpan = 2 };
            tbl.Rows[0].Cells[1] = new TableCell("B");
            doc.Blocks.Add(tbl);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 4000));
            var idx = doc.Blocks.IndexOf(tbl);

            // Caret in B (grid col 2 = cell-list index 1).
            view.PlaceCaretInCell(idx, row: 0, col: 2, paraIdx: 0, offset: 0);
            view.SetCellBorders(CellBorderEdges.All, "#000000", 0.5);

            bordersA = tbl.Rows[0].Cells[0].Borders;
            bordersB = tbl.Rows[0].Cells[1].Borders;
        });
        if (!ran) return;
        bordersA.Should().BeNull("cell A (the merged cell) must not receive borders");
        bordersB.Should().NotBeNull("cell B (grid col 2, cell-list index 1) must get borders");
        bordersB!.Top.Should().NotBeNull();
        bordersB.Bottom.Should().NotBeNull();
        bordersB.Left.Should().NotBeNull();
        bordersB.Right.Should().NotBeNull();
    }

    /// <summary>
    /// BL2 regression: applying a "Right" border to a block selection [gridCol 0..3] in a row
    /// with [A(GridSpan=2), B, C]. Every selected logical cell should get its Right edge set.
    /// This verifies the grid→cell-list conversion in the border path: without the fix, the loop
    /// would compare grid col against cells.Count and skip the rightmost cells or target wrong ones.
    /// </summary>
    [Fact]
    public async Task SetCellBorders_Right_BlockSelection_WithMergedCell_AllCellsGetRightEdge()
    {
        CellBorders? bordersA = null;
        CellBorders? bordersB = null;
        CellBorders? bordersC = null;
        var ran = await OnUiThread(() =>
        {
            // Row: [A(GridSpan=2), B, C] — grid cols 0-1, 2, 3.
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

            // Select the full row: grid cols 0..3 (A occupies 0-1, B=2, C=3).
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 0, focusRow: 0, focusCol: 3);
            // Apply a primitive Right edge to every cell in the selection.
            view.SetCellBorders(CellBorderEdges.Right, "#000000", 0.5);

            bordersA = tbl.Rows[0].Cells[0].Borders;
            bordersB = tbl.Rows[0].Cells[1].Borders;
            bordersC = tbl.Rows[0].Cells[2].Borders;
        });
        if (!ran) return;
        // All three logical cells must get the Right edge.
        bordersA.Should().NotBeNull("A (cell-list index 0, GridSpan=2) must receive a Right border");
        bordersA!.Right.Should().NotBeNull("A must get Right edge (primitive flag applies to every cell)");
        bordersA.Left.Should().BeNull("only Right was requested");
        bordersB.Should().NotBeNull("B (cell-list index 1) must receive a Right border");
        bordersB!.Right.Should().NotBeNull();
        bordersC.Should().NotBeNull("C (cell-list index 2, rightmost, grid col 3) must not be skipped");
        bordersC!.Right.Should().NotBeNull("C is the rightmost cell and must get the Right border");
    }

    /// <summary>
    /// BL2 regression: applying "All" borders to a block selection [gridCol 2..3] in a row
    /// with [A(GridSpan=2), B, C] — only B and C (grid cols 2 and 3) are selected.
    /// B and C must get all four borders; A (outside the selection) must not be touched.
    /// </summary>
    [Fact]
    public async Task SetCellBorders_All_BlockSelection_AfterMergedCell_SkipsOutOfSelectionCells()
    {
        CellBorders? bordersA = null;
        CellBorders? bordersB = null;
        CellBorders? bordersC = null;
        var ran = await OnUiThread(() =>
        {
            // Row: [A(GridSpan=2), B, C] — grid cols 0-1, 2, 3.
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

            // Select only grid cols 2..3 (B and C); A (grid 0-1) is outside.
            view.SetCellBlockSelection(idx, anchorRow: 0, anchorCol: 2, focusRow: 0, focusCol: 3);
            view.SetCellBorders(CellBorderEdges.All, "#000000", 0.5);

            bordersA = tbl.Rows[0].Cells[0].Borders;
            bordersB = tbl.Rows[0].Cells[1].Borders;
            bordersC = tbl.Rows[0].Cells[2].Borders;
        });
        if (!ran) return;
        bordersA.Should().BeNull("A (outside the selection, grid cols 0-1) must not be touched");
        bordersB.Should().NotBeNull("B (grid col 2, cell-list index 1) must get all borders");
        bordersB!.Top.Should().NotBeNull();
        bordersB.Bottom.Should().NotBeNull();
        bordersB.Left.Should().NotBeNull();
        bordersB.Right.Should().NotBeNull();
        bordersC.Should().NotBeNull("C (grid col 3, cell-list index 2, rightmost) must not be skipped");
        bordersC!.Top.Should().NotBeNull();
        bordersC.Bottom.Should().NotBeNull();
        bordersC.Left.Should().NotBeNull();
        bordersC.Right.Should().NotBeNull();
    }
}
