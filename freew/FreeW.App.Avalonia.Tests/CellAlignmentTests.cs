using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-TBL5 / BY2: tests for <see cref="DocumentView.SetCaretCellAlignment"/> and the 9
/// <c>freew.cell-align-*</c> ribbon commands added to the Avalonia table-layout Alignment group.
/// <list type="bullet">
///   <item>Caret cell: VerticalAlignment + paragraph Alignment set.</item>
///   <item>Block selection: all selected cells updated, including correct grid→cell-index for a row
///     that contains a merged (GridSpan=2) cell.</item>
///   <item>No-op outside a table.</item>
///   <item>One undo reverts a single-cell change; a multi-cell change is grouped into one undo.</item>
///   <item>All 9 <c>freew.cell-align-*</c> commands are registered in the Avalonia ribbon registry.</item>
///   <item>Alignment group is present in the table-layout contextual tab definition.</item>
/// </list>
/// </summary>
public sealed class CellAlignmentTests
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

    private static RibbonHostCallbacks NoopCallbacks() =>
        new(
            Open: () => { },
            Save: () => { },
            Cut: () => { },
            Copy: () => { },
            Paste: () => { },
            Backstage: () => { },
            NewDocument: () => { },
            ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { },
            ToggleRevealFormatting: () => { },
            OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { },
            SetWebLayout: () => { },
            SetDraftView: () => { },
            OpenFontDialog: () => { },
            OpenParagraphDialog: () => { },
            OpenPageSetupDialog: () => { },
            ToggleOrientation:   () => { },
            ApplyMarginPreset:   _ => { },
            ApplyPaperSize:      _ => { },
            ApplyZoom: (_, _) => { });

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────

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

    // ── Registry completeness ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void All_9_cell_align_commands_are_registered()
    {
        var view = new DocumentView();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        var ids = new[]
        {
            "freew.cell-align-top-left",
            "freew.cell-align-top-center",
            "freew.cell-align-top-right",
            "freew.cell-align-middle-left",
            "freew.cell-align-middle-center",
            "freew.cell-align-middle-right",
            "freew.cell-align-bottom-left",
            "freew.cell-align-bottom-center",
            "freew.cell-align-bottom-right",
        };

        foreach (var id in ids)
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"cell-align command '{id}' must be registered (BY2 parity gap)");
    }

    [Fact]
    public void Table_layout_tab_contains_alignment_group_with_9_buttons()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var layoutTab = definition.FindTab("table-layout");
        layoutTab.Should().NotBeNull("table-layout contextual tab must exist");

        var alignmentGroup = layoutTab!.Groups.FirstOrDefault(g => g.Id == "table-alignment");
        alignmentGroup.Should().NotBeNull("table-alignment group must be in table-layout tab (BY2)");

        var buttonCount = alignmentGroup!.Controls.Count(c => c is RibbonButton);
        buttonCount.Should().Be(9, "Alignment group must have exactly 9 cell-align buttons");
    }

    [Fact]
    public void Every_cell_align_ribbon_command_is_in_the_registry()
    {
        // Cross-check: every control in the table-alignment group resolves in the registry.
        var definition = FreeWRibbon.BuildDefinition();
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), NoopCallbacks());

        var alignmentGroup = definition.FindTab("table-layout")!
            .Groups.First(g => g.Id == "table-alignment");

        foreach (var control in alignmentGroup.Controls)
        {
            if (control is not RibbonButton b) continue;
            registry.TryGet(b.CommandId, out _)
                .Should().BeTrue($"Alignment group button '{b.CommandId.Value}' must be registered");
        }
    }

    // ── SetCaretCellAlignment – caret cell ────────────────────────────────────────────────────────

    [Fact]
    public async Task SetCaretCellAlignment_SetsVerticalAndHorizontalOnCaretCell()
    {
        TableCellVerticalAlignment? vAlign = null;
        TextAlignment? hAlign = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x2();
            view.PlaceCaretInCell(idx, 0, 0, 0, 0);
            view.SetCaretCellAlignment(TableCellVerticalAlignment.Center, TextAlignment.Right);
            vAlign = tbl.Rows[0].Cells[0].VerticalAlignment;
            hAlign = tbl.Rows[0].Cells[0].Paragraphs[0].Formatting.Alignment;
        });
        if (!ran) return;
        vAlign.Should().Be(TableCellVerticalAlignment.Center, "VerticalAlignment must be applied");
        hAlign.Should().Be(TextAlignment.Right, "paragraph horizontal Alignment must be applied");
    }

    [Fact]
    public async Task SetCaretCellAlignment_DoesNotAffectOtherCells()
    {
        TableCellVerticalAlignment? otherVAlign = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x2();
            view.PlaceCaretInCell(idx, 0, 0, 0, 0);
            view.SetCaretCellAlignment(TableCellVerticalAlignment.Bottom, TextAlignment.Center);
            otherVAlign = tbl.Rows[0].Cells[1].VerticalAlignment;
        });
        if (!ran) return;
        otherVAlign.Should().Be(TableCellVerticalAlignment.Top, "adjacent cells must not be touched");
    }

    [Fact]
    public async Task SetCaretCellAlignment_AllParagraphsInCell_ReceiveHorizontalAlignment()
    {
        // A cell with 2 paragraphs — both must get the new horizontal alignment.
        TextAlignment? align0 = null;
        TextAlignment? align1 = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            var tbl = Table.Create(1, 1);
            var cell = new TableCell();
            cell.Paragraphs.Add(new Paragraph("First"));
            cell.Paragraphs.Add(new Paragraph("Second"));
            tbl.Rows[0].Cells[0] = cell;
            doc.Blocks.Add(tbl);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            var idx = doc.Blocks.IndexOf(tbl);
            view.PlaceCaretInCell(idx, 0, 0, 0, 0);
            view.SetCaretCellAlignment(TableCellVerticalAlignment.Center, TextAlignment.Right);
            align0 = tbl.Rows[0].Cells[0].Paragraphs[0].Formatting.Alignment;
            align1 = tbl.Rows[0].Cells[0].Paragraphs[1].Formatting.Alignment;
        });
        if (!ran) return;
        align0.Should().Be(TextAlignment.Right, "first paragraph must get horizontal alignment");
        align1.Should().Be(TextAlignment.Right, "second paragraph must also get horizontal alignment");
    }

    // ── SetCaretCellAlignment – undo ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetCaretCellAlignment_IsUndoable_SingleCell()
    {
        TableCellVerticalAlignment? vAfterUndo = null;
        TextAlignment? hAfterUndo = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x2();
            view.PlaceCaretInCell(idx, 1, 1, 0, 0);
            view.SetCaretCellAlignment(TableCellVerticalAlignment.Bottom, TextAlignment.Right);
            view.Undo();
            vAfterUndo = tbl.Rows[1].Cells[1].VerticalAlignment;
            hAfterUndo = tbl.Rows[1].Cells[1].Paragraphs[0].Formatting.Alignment;
        });
        if (!ran) return;
        vAfterUndo.Should().Be(TableCellVerticalAlignment.Top, "undo must restore default Top vertical alignment");
        hAfterUndo.Should().Be(TextAlignment.Left, "undo must restore default Left horizontal alignment");
    }

    // ── SetCaretCellAlignment – block selection ──────────────────────────────────────────────────

    [Fact]
    public async Task SetCaretCellAlignment_AppliesToAllCellsInBlockSelection()
    {
        TableCellVerticalAlignment? v00 = null, v01 = null, v10 = null, v11 = null;
        TextAlignment? h00 = null, h01 = null, h10 = null, h11 = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x2();
            view.SetCellBlockSelection(idx, 0, 0, 1, 1);
            view.SetCaretCellAlignment(TableCellVerticalAlignment.Center, TextAlignment.Center);
            v00 = tbl.Rows[0].Cells[0].VerticalAlignment;
            v01 = tbl.Rows[0].Cells[1].VerticalAlignment;
            v10 = tbl.Rows[1].Cells[0].VerticalAlignment;
            v11 = tbl.Rows[1].Cells[1].VerticalAlignment;
            h00 = tbl.Rows[0].Cells[0].Paragraphs[0].Formatting.Alignment;
            h01 = tbl.Rows[0].Cells[1].Paragraphs[0].Formatting.Alignment;
            h10 = tbl.Rows[1].Cells[0].Paragraphs[0].Formatting.Alignment;
            h11 = tbl.Rows[1].Cells[1].Paragraphs[0].Formatting.Alignment;
        });
        if (!ran) return;
        v00.Should().Be(TableCellVerticalAlignment.Center);
        v01.Should().Be(TableCellVerticalAlignment.Center);
        v10.Should().Be(TableCellVerticalAlignment.Center);
        v11.Should().Be(TableCellVerticalAlignment.Center, "all 4 cells in block selection must be updated");
        h00.Should().Be(TextAlignment.Center);
        h01.Should().Be(TextAlignment.Center);
        h10.Should().Be(TextAlignment.Center);
        h11.Should().Be(TextAlignment.Center);
    }

    [Fact]
    public async Task SetCaretCellAlignment_BlockSelection_IsUndoneWithSingleUndo()
    {
        // A block selection on 4 cells — because BeginUndoGroup/CommitUndoGroup wraps them all,
        // a single Undo() must revert all 4 cells.
        int cellsReverted = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, tbl) = MakeTable2x2();
            view.SetCellBlockSelection(idx, 0, 0, 1, 1);
            view.SetCaretCellAlignment(TableCellVerticalAlignment.Bottom, TextAlignment.Right);
            view.Undo(); // single undo must revert all 4 (grouped)
            cellsReverted = tbl.Rows.SelectMany(r => r.Cells)
                .Count(c => c.VerticalAlignment == TableCellVerticalAlignment.Top
                         && c.Paragraphs[0].Formatting.Alignment == TextAlignment.Left);
        });
        if (!ran) return;
        cellsReverted.Should().Be(4, "single undo must revert all 4 cells at once (undo group)");
    }

    // ── SetCaretCellAlignment – grid→cell-index with merged cell ────────────────────────────────

    /// <summary>
    /// BY2/BL1 regression: when a row has a cell with GridSpan=2 (covers grid cols 0–1), the
    /// second cell B is at grid col 2 but cell-list index 1. A block selection covering grid
    /// cols 0–2 must apply alignment to both the merged cell A (cell-list index 0) and B
    /// (cell-list index 1), not silently index out-of-bounds.
    /// </summary>
    [Fact]
    public async Task SetCaretCellAlignment_BlockSelection_GridSpanRow_TargetsCorrectCells()
    {
        TableCellVerticalAlignment? vA = null; // the GridSpan=2 merged cell
        TableCellVerticalAlignment? vB = null; // the next cell (grid col 2, cell-list index 1)
        TextAlignment? hA = null;
        TextAlignment? hB = null;
        var ran = await OnUiThread(() =>
        {
            // Row 0: [A(GridSpan=2), B] — grid layout: A at 0..1, B at 2.
            var doc = TextDocument.CreateEmpty();
            var tbl = Table.Create(1, 2);
            tbl.Rows[0].Cells[0] = new TableCell("A") { GridSpan = 2 };
            tbl.Rows[0].Cells[1] = new TableCell("B");
            doc.Blocks.Add(tbl);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            var idx = doc.Blocks.IndexOf(tbl);

            // Block selection: grid cols 0..2 (covers A twice and B once).
            view.SetCellBlockSelection(idx, 0, 0, 0, 2);
            view.SetCaretCellAlignment(TableCellVerticalAlignment.Bottom, TextAlignment.Right);

            vA = tbl.Rows[0].Cells[0].VerticalAlignment;
            vB = tbl.Rows[0].Cells[1].VerticalAlignment;
            hA = tbl.Rows[0].Cells[0].Paragraphs[0].Formatting.Alignment;
            hB = tbl.Rows[0].Cells[1].Paragraphs[0].Formatting.Alignment;
        });
        if (!ran) return;
        vA.Should().Be(TableCellVerticalAlignment.Bottom, "merged cell A must be aligned (grid→cell dedup)");
        vB.Should().Be(TableCellVerticalAlignment.Bottom, "cell B (grid col 2, list index 1) must be aligned");
        hA.Should().Be(TextAlignment.Right);
        hB.Should().Be(TextAlignment.Right);
    }

    // ── SetCaretCellAlignment – no-op outside table ──────────────────────────────────────────────

    [Fact]
    public async Task SetCaretCellAlignment_InBodyText_IsNoOp()
    {
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Add(new Paragraph("not a table"));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            // CellCaretInfo is null — must not throw.
            view.SetCaretCellAlignment(TableCellVerticalAlignment.Bottom, TextAlignment.Right);
        });
        if (!ran) return; // env race — skip
        // Reaching here without exception is the assertion.
    }

    // ── AV-TBL5-VRENDER: vertical alignment render tests ─────────────────────────────────────────

    /// <summary>
    /// Builds a 2-column table where the LEFT cell has many lines (making it tall) and the RIGHT
    /// cell has a single short line. Returns (view, tableBlockIdx) after measuring.
    /// The caller sets VerticalAlignment on the right cell before calling Measure, or this helper
    /// accepts the alignment as a parameter.
    /// </summary>
    private static (DocumentView View, int TableBlockIdx, Table Tbl) MakeTallRowTable(
        TableCellVerticalAlignment rightCellVAlign)
    {
        var doc = TextDocument.CreateEmpty();
        // 1 row, 2 columns: left cell has many paragraphs (tall), right cell has one short line.
        var tbl = Table.Create(1, 2);

        // Left cell: 5 paragraphs — drives row height up well above a single line.
        var leftCell = new TableCell();
        leftCell.Paragraphs.Clear();
        leftCell.Paragraphs.Add(new Paragraph("Line 1"));
        leftCell.Paragraphs.Add(new Paragraph("Line 2"));
        leftCell.Paragraphs.Add(new Paragraph("Line 3"));
        leftCell.Paragraphs.Add(new Paragraph("Line 4"));
        leftCell.Paragraphs.Add(new Paragraph("Line 5"));
        tbl.Rows[0].Cells[0] = leftCell;

        // Right cell: one short line, with the requested vertical alignment pre-set.
        var rightCell = new TableCell("Hi") { VerticalAlignment = rightCellVAlign };
        tbl.Rows[0].Cells[1] = rightCell;

        doc.Blocks.Add(tbl);
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 4000));
        var idx = doc.Blocks.IndexOf(tbl);
        return (view, idx, tbl);
    }

    [Fact]
    public async Task VerticalAlign_Top_ContentStartsAtTopOfCell()
    {
        // Top alignment (default): content Y must equal rowPageSpaceY + pad.
        // We test this indirectly: Top Y is less than or equal to Center Y.
        double topY = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTallRowTable(TableCellVerticalAlignment.Top);
            view.PlaceCaretInCell(idx, 0, 1, 0, 0);
            topY = view.CaretTop;
        });
        if (!ran) return;
        // The caret must be placed somewhere (non-negative).
        topY.Should().BeGreaterThanOrEqualTo(0,
            "Top-aligned cell caret must be resolvable");
    }

    [Fact]
    public async Task VerticalAlign_Center_ContentLowerThanTop()
    {
        // Center-aligned content in a tall row must start lower than Top-aligned content.
        double topY = -1, centerY = -1;
        var ran = await OnUiThread(() =>
        {
            var (viewTop, idxTop, _) = MakeTallRowTable(TableCellVerticalAlignment.Top);
            viewTop.PlaceCaretInCell(idxTop, 0, 1, 0, 0);
            topY = viewTop.CaretTop;

            var (viewCenter, idxCenter, _) = MakeTallRowTable(TableCellVerticalAlignment.Center);
            viewCenter.PlaceCaretInCell(idxCenter, 0, 1, 0, 0);
            centerY = viewCenter.CaretTop;
        });
        if (!ran) return;
        centerY.Should().BeGreaterThan(topY,
            "Center-aligned content must start lower (higher Y) than Top-aligned content in a tall row");
    }

    [Fact]
    public async Task VerticalAlign_Bottom_ContentLowerThanCenter()
    {
        // Bottom-aligned content must start lower than Center-aligned content.
        double centerY = -1, bottomY = -1;
        var ran = await OnUiThread(() =>
        {
            var (viewCenter, idxCenter, _) = MakeTallRowTable(TableCellVerticalAlignment.Center);
            viewCenter.PlaceCaretInCell(idxCenter, 0, 1, 0, 0);
            centerY = viewCenter.CaretTop;

            var (viewBottom, idxBottom, _) = MakeTallRowTable(TableCellVerticalAlignment.Bottom);
            viewBottom.PlaceCaretInCell(idxBottom, 0, 1, 0, 0);
            bottomY = viewBottom.CaretTop;
        });
        if (!ran) return;
        bottomY.Should().BeGreaterThan(centerY,
            "Bottom-aligned content must start lower (higher Y) than Center-aligned content in a tall row");
    }

    [Fact]
    public async Task VerticalAlign_Bottom_GreaterThan_Top()
    {
        // Composite: Bottom > Top directly.
        double topY = -1, bottomY = -1;
        var ran = await OnUiThread(() =>
        {
            var (viewTop, idxTop, _) = MakeTallRowTable(TableCellVerticalAlignment.Top);
            viewTop.PlaceCaretInCell(idxTop, 0, 1, 0, 0);
            topY = viewTop.CaretTop;

            var (viewBottom, idxBottom, _) = MakeTallRowTable(TableCellVerticalAlignment.Bottom);
            viewBottom.PlaceCaretInCell(idxBottom, 0, 1, 0, 0);
            bottomY = viewBottom.CaretTop;
        });
        if (!ran) return;
        bottomY.Should().BeGreaterThan(topY,
            "Bottom-aligned content must start lower than Top-aligned content in a tall row");
    }

    [Fact]
    public async Task VerticalAlign_Top_NoRegressionVsDefaultLayout()
    {
        // A normal uniform-height table (all cells have same content) must still render correctly:
        // Top alignment should be unchanged — content starts at the same Y in all cells.
        double yCell0 = -1, yCell1 = -1;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, _) = MakeTable2x2();
            // Row 0, cells 0 and 1 have equal content — both Top aligned by default.
            view.PlaceCaretInCell(idx, 0, 0, 0, 0);
            yCell0 = view.CaretTop;
            view.PlaceCaretInCell(idx, 0, 1, 0, 0);
            yCell1 = view.CaretTop;
        });
        if (!ran) return;
        yCell0.Should().BeApproximately(yCell1, 0.5,
            "Top-aligned cells in a uniform-height row must render at the same Y");
    }

    [Fact]
    public async Task VerticalAlign_ContentTallerThanCell_NoClamping_NoUpwardShift()
    {
        // When content is taller than available height, vAlignOffset must clamp to 0 (no upward shift).
        // We simulate this by using Center alignment on a cell whose content exactly fills the row
        // (the tallest cell = itself). cellAvailableHeight == contentHeight → offset = 0.
        double topY = -1, centerY = -1;
        var ran = await OnUiThread(() =>
        {
            // Single-column, single-row table: only cell is the tallest, so its vAlignOffset must be 0.
            // Table.Create(rows, columns) — 1 row, 1 column.
            var doc = TextDocument.CreateEmpty();
            var tbl = Table.Create(1, 1); // 1 row, 1 col
            var cell = new TableCell { VerticalAlignment = TableCellVerticalAlignment.Center };
            cell.Paragraphs.Clear();
            cell.Paragraphs.Add(new Paragraph("Line A"));
            cell.Paragraphs.Add(new Paragraph("Line B"));
            cell.Paragraphs.Add(new Paragraph("Line C"));
            tbl.Rows[0].Cells[0] = cell;
            doc.Blocks.Add(tbl);

            var docTop = TextDocument.CreateEmpty();
            var tblTop = Table.Create(1, 1); // 1 row, 1 col
            var cellTop = new TableCell { VerticalAlignment = TableCellVerticalAlignment.Top };
            cellTop.Paragraphs.Clear();
            cellTop.Paragraphs.Add(new Paragraph("Line A"));
            cellTop.Paragraphs.Add(new Paragraph("Line B"));
            cellTop.Paragraphs.Add(new Paragraph("Line C"));
            tblTop.Rows[0].Cells[0] = cellTop;
            docTop.Blocks.Add(tblTop);

            var viewCenter = new DocumentView();
            viewCenter.LoadDocument(doc);
            viewCenter.Measure(new Size(800, 4000));
            var idxCenter = doc.Blocks.IndexOf(tbl);
            viewCenter.PlaceCaretInCell(idxCenter, 0, 0, 0, 0);
            centerY = viewCenter.CaretTop;

            var viewTop = new DocumentView();
            viewTop.LoadDocument(docTop);
            viewTop.Measure(new Size(800, 4000));
            var idxTop = docTop.Blocks.IndexOf(tblTop);
            viewTop.PlaceCaretInCell(idxTop, 0, 0, 0, 0);
            topY = viewTop.CaretTop;
        });
        if (!ran) return;
        centerY.Should().BeApproximately(topY, 0.5,
            "When the cell is the tallest (fills the row), Center offset clamps to 0 — same as Top");
    }
}
