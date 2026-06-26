using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Unit tests for table-edit commands (Wave 9A):
///   SetTableCellTextCommand, InsertTableRowCommand, DeleteTableRowCommand,
///   InsertTableColumnCommand, DeleteTableColumnCommand,
///   MergeTableCellsCommand, SplitTableCellCommand.
///
/// Also covers EditingSession table API (active-cell, SetTableCellText, InsertRow/Col, etc.)
/// and the framework-free TableCellHitTester helper.
/// </summary>
public sealed class TableEditCommandTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a presentation with one slide containing a (rows x cols) table.</summary>
    private static (Presentation p, PresentationCommandBus bus, SlideShape tableShape)
        MakeTable(int rows = 3, int cols = 3)
    {
        var p = new Presentation();
        p.Slides.Add(new Slide());
        var bus = new PresentationCommandBus(p);

        var table = new TableShape();
        for (int c = 0; c < cols; c++)
            table.ColumnWidthsEmu.Add(914400L); // 1 inch each

        for (int r = 0; r < rows; r++)
        {
            var row = new TableRow { HeightEmu = 457200L }; // 0.5 inch each
            for (int c = 0; c < cols; c++)
                row.Cells.Add(new TableCell());
            table.Rows.Add(row);
        }

        var shape = new SlideShape
        {
            Id          = 1,
            Kind        = SlideShapeKind.Table,
            OffsetXEmu  = 0,
            OffsetYEmu  = 0,
            ExtentCxEmu = 914400L * cols,
            ExtentCyEmu = 457200L * rows,
            Table       = table,
        };
        p.Slides[0].Shapes.Add(shape);
        return (p, bus, shape);
    }

    private static (Presentation p, PresentationCommandBus bus, SlideShape tableShape)
        MakeTableWithText(int rows = 3, int cols = 3)
    {
        var (p, bus, shape) = MakeTable(rows, cols);
        // Populate cells with text "R{r}C{c}"
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var body = new TextBody();
                var para = new Paragraph();
                para.Runs.Add(new Run { Text = $"R{r}C{c}" });
                body.Paragraphs.Add(para);
                shape.Table!.Rows[r].Cells[c].TextBody = body;
            }
        return (p, bus, shape);
    }

    private static string CellText(SlideShape shape, int r, int c)
    {
        var cell = shape.Table!.Rows[r].Cells[c];
        if (cell.TextBody is null) return string.Empty;
        return string.Join("", cell.TextBody.Paragraphs.SelectMany(p => p.Runs).Select(run => run.Text));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // SetTableCellTextCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SetTableCellText_Apply_ChangesCellText()
    {
        var (p, bus, shape) = MakeTable();
        var body = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Hello" });
        body.Paragraphs.Add(para);

        bus.Execute(new SetTableCellTextCommand(0, 1, 1, 1, body));

        CellText(shape, 1, 1).Should().Be("Hello");
    }

    [Fact]
    public void SetTableCellText_Revert_RestoresPreviousText()
    {
        var (p, bus, shape) = MakeTableWithText();
        var oldText = CellText(shape, 0, 0); // "R0C0"

        var newBody = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Changed" });
        newBody.Paragraphs.Add(para);

        bus.Execute(new SetTableCellTextCommand(0, 1, 0, 0, newBody));
        bus.Undo();

        CellText(shape, 0, 0).Should().Be(oldText);
    }

    [Fact]
    public void SetTableCellText_UndoRedo_Works()
    {
        var (p, bus, shape) = MakeTable();
        var body = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Redo" });
        body.Paragraphs.Add(para);

        bus.Execute(new SetTableCellTextCommand(0, 1, 2, 2, body));
        bus.Undo();
        bus.Redo();

        CellText(shape, 2, 2).Should().Be("Redo");
    }

    [Fact]
    public void SetTableCellText_OtherCellsUnchanged()
    {
        var (p, bus, shape) = MakeTableWithText();
        var body = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "X" });
        body.Paragraphs.Add(para);

        bus.Execute(new SetTableCellTextCommand(0, 1, 1, 1, body));

        // Surrounding cells should be unchanged
        CellText(shape, 0, 0).Should().Be("R0C0");
        CellText(shape, 2, 2).Should().Be("R2C2");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // InsertTableRowCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertRow_Apply_AddsRowAtIndex()
    {
        var (p, bus, shape) = MakeTable(3, 2);
        bus.Execute(new InsertTableRowCommand(0, 1, 1));
        shape.Table!.Rows.Should().HaveCount(4);
        // New row at index 1 should have correct cell count.
        shape.Table.Rows[1].Cells.Should().HaveCount(2);
    }

    [Fact]
    public void InsertRow_Apply_PreservesExistingCellContent()
    {
        var (p, bus, shape) = MakeTableWithText(2, 2);
        // Row 0: R0C0, R0C1. Row 1: R1C0, R1C1.
        bus.Execute(new InsertTableRowCommand(0, 1, 1)); // insert between rows 0 and 1
        // After: row 0 = original row 0, row 1 = new blank, row 2 = original row 1.
        CellText(shape, 0, 0).Should().Be("R0C0");
        CellText(shape, 2, 0).Should().Be("R1C0");
        CellText(shape, 1, 0).Should().Be(string.Empty); // new row is blank
    }

    [Fact]
    public void InsertRow_Revert_RestoresOriginalRowCount()
    {
        var (p, bus, shape) = MakeTable(3, 3);
        bus.Execute(new InsertTableRowCommand(0, 1, 0));
        bus.Undo();
        shape.Table!.Rows.Should().HaveCount(3);
    }

    [Fact]
    public void InsertRow_Revert_RestoresCellContent()
    {
        var (p, bus, shape) = MakeTableWithText(2, 2);
        bus.Execute(new InsertTableRowCommand(0, 1, 1));
        bus.Undo();
        CellText(shape, 0, 0).Should().Be("R0C0");
        CellText(shape, 1, 0).Should().Be("R1C0");
    }

    [Fact]
    public void InsertRow_AtEnd_AppendRow()
    {
        var (p, bus, shape) = MakeTable(2, 2);
        bus.Execute(new InsertTableRowCommand(0, 1, 2)); // insert at end
        shape.Table!.Rows.Should().HaveCount(3);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // DeleteTableRowCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteRow_Apply_RemovesRowAtIndex()
    {
        var (p, bus, shape) = MakeTableWithText(3, 2);
        bus.Execute(new DeleteTableRowCommand(0, 1, 1)); // delete middle row
        shape.Table!.Rows.Should().HaveCount(2);
        CellText(shape, 0, 0).Should().Be("R0C0");
        CellText(shape, 1, 0).Should().Be("R2C0");
    }

    [Fact]
    public void DeleteRow_Revert_RestoresAllRows()
    {
        var (p, bus, shape) = MakeTableWithText(3, 2);
        bus.Execute(new DeleteTableRowCommand(0, 1, 1));
        bus.Undo();
        shape.Table!.Rows.Should().HaveCount(3);
        CellText(shape, 1, 0).Should().Be("R1C0");
    }

    [Fact]
    public void DeleteRow_NoOp_WhenSingleRow()
    {
        var (p, bus, shape) = MakeTable(1, 2);
        bus.Execute(new DeleteTableRowCommand(0, 1, 0));
        shape.Table!.Rows.Should().HaveCount(1); // still one row
    }

    // ════════════════════════════════════════════════════════════════════════════
    // InsertTableColumnCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertColumn_Apply_AddsColumnAtIndex()
    {
        var (p, bus, shape) = MakeTable(2, 3);
        bus.Execute(new InsertTableColumnCommand(0, 1, 1));
        shape.Table!.ColumnWidthsEmu.Should().HaveCount(4);
        foreach (var row in shape.Table.Rows)
            row.Cells.Should().HaveCount(4);
    }

    [Fact]
    public void InsertColumn_Apply_PreservesExistingCellContent()
    {
        var (p, bus, shape) = MakeTableWithText(2, 2);
        bus.Execute(new InsertTableColumnCommand(0, 1, 1)); // insert between cols 0 and 1
        // After: col 0 = R0C0/R1C0, col 1 = blank, col 2 = R0C1/R1C1
        CellText(shape, 0, 0).Should().Be("R0C0");
        CellText(shape, 0, 1).Should().Be(string.Empty); // new col
        CellText(shape, 0, 2).Should().Be("R0C1");
    }

    [Fact]
    public void InsertColumn_Revert_RestoresOriginalColumnCount()
    {
        var (p, bus, shape) = MakeTable(2, 3);
        bus.Execute(new InsertTableColumnCommand(0, 1, 0));
        bus.Undo();
        shape.Table!.ColumnWidthsEmu.Should().HaveCount(3);
        foreach (var row in shape.Table.Rows)
            row.Cells.Should().HaveCount(3);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // DeleteTableColumnCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteColumn_Apply_RemovesColumnAtIndex()
    {
        var (p, bus, shape) = MakeTableWithText(2, 3);
        bus.Execute(new DeleteTableColumnCommand(0, 1, 1)); // delete middle col
        shape.Table!.ColumnWidthsEmu.Should().HaveCount(2);
        foreach (var row in shape.Table.Rows)
            row.Cells.Should().HaveCount(2);
        CellText(shape, 0, 0).Should().Be("R0C0");
        CellText(shape, 0, 1).Should().Be("R0C2");
    }

    [Fact]
    public void DeleteColumn_Revert_RestoresAllColumns()
    {
        var (p, bus, shape) = MakeTableWithText(2, 3);
        bus.Execute(new DeleteTableColumnCommand(0, 1, 1));
        bus.Undo();
        shape.Table!.ColumnWidthsEmu.Should().HaveCount(3);
        CellText(shape, 0, 1).Should().Be("R0C1");
    }

    [Fact]
    public void DeleteColumn_NoOp_WhenSingleColumn()
    {
        var (p, bus, shape) = MakeTable(2, 1);
        bus.Execute(new DeleteTableColumnCommand(0, 1, 0));
        shape.Table!.ColumnWidthsEmu.Should().HaveCount(1);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // MergeTableCellsCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MergeCells_Apply_SetsAnchorGridSpanAndRowSpan()
    {
        var (p, bus, shape) = MakeTable(3, 3);
        bus.Execute(new MergeTableCellsCommand(0, 1, 0, 0, 1, 1)); // merge 2x2 at top-left
        var anchor = shape.Table!.Rows[0].Cells[0];
        anchor.GridSpan.Should().Be(2);
        anchor.RowSpan.Should().Be(2);
        anchor.HMerge.Should().BeFalse();
        anchor.VMerge.Should().BeFalse();
    }

    [Fact]
    public void MergeCells_Apply_SetsCoveredCellsHMergeVMerge()
    {
        var (p, bus, shape) = MakeTable(3, 3);
        bus.Execute(new MergeTableCellsCommand(0, 1, 0, 0, 1, 1)); // merge rows 0-1, cols 0-1
        // Row 0, col 1: same row as anchor → HMerge.
        shape.Table!.Rows[0].Cells[1].HMerge.Should().BeTrue();
        shape.Table.Rows[0].Cells[1].VMerge.Should().BeFalse();
        // Row 1, col 0: below anchor → VMerge.
        shape.Table.Rows[1].Cells[0].VMerge.Should().BeTrue();
        shape.Table.Rows[1].Cells[0].HMerge.Should().BeFalse();
        // Row 1, col 1: below and to the right → VMerge (second row, not anchor's column).
        shape.Table.Rows[1].Cells[1].VMerge.Should().BeTrue();
    }

    [Fact]
    public void MergeCells_Apply_ConcatenatesText()
    {
        var (p, bus, shape) = MakeTableWithText(2, 2);
        bus.Execute(new MergeTableCellsCommand(0, 1, 0, 0, 0, 1)); // merge row 0, cols 0-1
        // Anchor text should contain both "R0C0" and "R0C1"
        var anchorText = CellText(shape, 0, 0);
        anchorText.Should().Contain("R0C0");
        anchorText.Should().Contain("R0C1");
    }

    [Fact]
    public void MergeCells_Apply_CoversParameterOrderInvariant()
    {
        // r1 > r2 and c1 > c2 should be normalised internally.
        var (p, bus, shape) = MakeTable(3, 3);
        bus.Execute(new MergeTableCellsCommand(0, 1, 2, 2, 0, 0)); // reversed corners
        var anchor = shape.Table!.Rows[0].Cells[0];
        anchor.GridSpan.Should().Be(3);
        anchor.RowSpan.Should().Be(3);
    }

    [Fact]
    public void MergeCells_Revert_RestoresAllCells()
    {
        var (p, bus, shape) = MakeTableWithText(2, 2);
        bus.Execute(new MergeTableCellsCommand(0, 1, 0, 0, 1, 1));
        bus.Undo();
        // All cells should revert to GridSpan=1, RowSpan=1, original text.
        foreach (var row in shape.Table!.Rows)
            foreach (var cell in row.Cells)
            {
                cell.GridSpan.Should().Be(1);
                cell.RowSpan.Should().Be(1);
                cell.HMerge.Should().BeFalse();
                cell.VMerge.Should().BeFalse();
            }
        CellText(shape, 0, 0).Should().Be("R0C0");
        CellText(shape, 1, 1).Should().Be("R1C1");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // SplitTableCellCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SplitCell_Apply_ClearsAnchorMerge()
    {
        var (p, bus, shape) = MakeTableWithText(2, 2);
        bus.Execute(new MergeTableCellsCommand(0, 1, 0, 0, 0, 1));
        bus.Execute(new SplitTableCellCommand(0, 1, 0, 0));

        var anchor = shape.Table!.Rows[0].Cells[0];
        anchor.GridSpan.Should().Be(1);
        anchor.RowSpan.Should().Be(1);
    }

    [Fact]
    public void SplitCell_Apply_ClearsHMergeOnCoveredCells()
    {
        var (p, bus, shape) = MakeTable(2, 3);
        bus.Execute(new MergeTableCellsCommand(0, 1, 0, 0, 0, 2)); // merge row 0 cols 0-2
        bus.Execute(new SplitTableCellCommand(0, 1, 0, 0));

        shape.Table!.Rows[0].Cells[1].HMerge.Should().BeFalse();
        shape.Table.Rows[0].Cells[2].HMerge.Should().BeFalse();
    }

    [Fact]
    public void SplitCell_NoOp_WhenCellIsNotMerged()
    {
        var (p, bus, shape) = MakeTable(2, 2);
        // No merge — apply split should be a no-op (no exception, no undo entry recorded).
        bus.Execute(new SplitTableCellCommand(0, 1, 0, 0));
        bus.CanUndo.Should().BeFalse("no-op should not push undo entry");
    }

    [Fact]
    public void SplitCell_Revert_ReappliesMerge()
    {
        var (p, bus, shape) = MakeTable(2, 2);
        bus.Execute(new MergeTableCellsCommand(0, 1, 0, 0, 1, 1));
        bus.Execute(new SplitTableCellCommand(0, 1, 0, 0));
        bus.Undo(); // undo the split → merge should be restored
        shape.Table!.Rows[0].Cells[0].GridSpan.Should().Be(2);
        shape.Table.Rows[0].Cells[0].RowSpan.Should().Be(2);
        shape.Table.Rows[0].Cells[1].HMerge.Should().BeTrue();
    }

    // ════════════════════════════════════════════════════════════════════════════
    // EditingSession table API
    // ════════════════════════════════════════════════════════════════════════════

    private static EditingSession MakeSession(out SlideShape tableShape, int rows = 3, int cols = 3)
    {
        var p = new Presentation();
        p.Slides.Add(new Slide());
        var bus = new PresentationCommandBus(p);

        var table = new TableShape();
        for (int c = 0; c < cols; c++)
            table.ColumnWidthsEmu.Add(914400L);
        for (int r = 0; r < rows; r++)
        {
            var row = new TableRow { HeightEmu = 457200L };
            for (int c = 0; c < cols; c++)
                row.Cells.Add(new TableCell());
            table.Rows.Add(row);
        }

        tableShape = new SlideShape
        {
            Id          = 1,
            Kind        = SlideShapeKind.Table,
            OffsetXEmu  = 0,
            OffsetYEmu  = 0,
            ExtentCxEmu = 914400L * cols,
            ExtentCyEmu = 457200L * rows,
            Table       = table,
        };
        p.Slides[0].Shapes.Add(tableShape);

        var sess = new EditingSession(p, bus);
        sess.Select(1); // select the table shape
        return sess;
    }

    [Fact]
    public void EditingSession_SetActiveTableCell_SetsAndClamps()
    {
        var sess = MakeSession(out _);
        sess.SetActiveTableCell(1, 2);
        sess.ActiveTableCell.Should().Be((1, 2));
    }

    [Fact]
    public void EditingSession_SetActiveTableCell_ClampsToValidRange()
    {
        var sess = MakeSession(out _, 3, 3);
        sess.SetActiveTableCell(99, 99);
        sess.ActiveTableCell.Should().Be((2, 2)); // clamped to last valid
    }

    [Fact]
    public void EditingSession_ClearActiveTableCell_SetsNull()
    {
        var sess = MakeSession(out _);
        sess.SetActiveTableCell(0, 0);
        sess.ClearActiveTableCell();
        sess.ActiveTableCell.Should().BeNull();
    }

    [Fact]
    public void EditingSession_ActiveTableCellChanged_Fires()
    {
        var sess = MakeSession(out _);
        int fired = 0;
        sess.ActiveTableCellChanged += (_, _) => fired++;
        sess.SetActiveTableCell(1, 1);
        fired.Should().Be(1);
    }

    [Fact]
    public void EditingSession_SetTableCellText_UpdatesCell()
    {
        var sess = MakeSession(out var shape);
        sess.SetTableCellText(0, 0, "Hello");
        CellText(shape, 0, 0).Should().Be("Hello");
    }

    [Fact]
    public void EditingSession_SetTableCellText_IsUndoable()
    {
        var sess = MakeSession(out var shape);
        sess.SetTableCellText(0, 0, "Hello");
        sess.Undo();
        CellText(shape, 0, 0).Should().BeEmpty();
    }

    [Fact]
    public void EditingSession_InsertRowBelow_GrowsGrid()
    {
        var sess = MakeSession(out var shape, 2, 2);
        sess.SetActiveTableCell(0, 0);
        sess.InsertRowBelow();
        shape.Table!.Rows.Should().HaveCount(3);
    }

    [Fact]
    public void EditingSession_InsertRowAbove_ShiftsActiveCell()
    {
        var sess = MakeSession(out var shape, 3, 2);
        sess.SetActiveTableCell(1, 0);
        sess.InsertRowAbove();
        // Active cell should have shifted down to row 2.
        sess.ActiveTableCell!.Value.Row.Should().Be(2);
    }

    [Fact]
    public void EditingSession_DeleteRow_ShrinkGrid()
    {
        var sess = MakeSession(out var shape, 3, 2);
        sess.SetActiveTableCell(1, 0);
        sess.DeleteRow();
        shape.Table!.Rows.Should().HaveCount(2);
    }

    [Fact]
    public void EditingSession_InsertColumnRight_GrowsGrid()
    {
        var sess = MakeSession(out var shape, 2, 2);
        sess.SetActiveTableCell(0, 0);
        sess.InsertColumnRight();
        shape.Table!.ColumnWidthsEmu.Should().HaveCount(3);
        foreach (var row in shape.Table.Rows)
            row.Cells.Should().HaveCount(3);
    }

    [Fact]
    public void EditingSession_DeleteColumn_ShrinkGrid()
    {
        var sess = MakeSession(out var shape, 2, 3);
        sess.SetActiveTableCell(0, 1);
        sess.DeleteColumn();
        shape.Table!.ColumnWidthsEmu.Should().HaveCount(2);
    }

    [Fact]
    public void EditingSession_SplitSelectedCell_Works()
    {
        var sess = MakeSession(out var shape, 2, 2);
        // First merge, then split via session API.
        sess.MergeTableCells(0, 0, 0, 1);
        sess.SetActiveTableCell(0, 0);
        sess.SplitSelectedCell();
        shape.Table!.Rows[0].Cells[0].GridSpan.Should().Be(1);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // GetSelectedTable
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EditingSession_GetSelectedTable_ReturnsTableWhenTableSelected()
    {
        var sess = MakeSession(out var shape);
        sess.GetSelectedTable().Should().NotBeNull();
    }

    [Fact]
    public void EditingSession_GetSelectedTable_ReturnsNullWhenNonTableSelected()
    {
        var p = new Presentation();
        p.Slides.Add(new Slide());
        var nonTable = new SlideShape
        {
            Id   = 10,
            Kind = SlideShapeKind.AutoShape,
        };
        p.Slides[0].Shapes.Add(nonTable);
        var bus  = new PresentationCommandBus(p);
        var sess = new EditingSession(p, bus);
        sess.Select(10);
        sess.GetSelectedTable().Should().BeNull();
    }

    // ════════════════════════════════════════════════════════════════════════════
    // TableCellHitTester  (framework-free)
    // ════════════════════════════════════════════════════════════════════════════

    private static SlideShape MakeTableShape(int rows, int cols,
        long colWidthEmu = 914400L, long rowHeightEmu = 457200L,
        long offsetX = 0, long offsetY = 0)
    {
        var table = new TableShape();
        for (int c = 0; c < cols; c++)
            table.ColumnWidthsEmu.Add(colWidthEmu);
        for (int r = 0; r < rows; r++)
        {
            var row = new TableRow { HeightEmu = rowHeightEmu };
            for (int c = 0; c < cols; c++)
                row.Cells.Add(new TableCell());
            table.Rows.Add(row);
        }
        return new SlideShape
        {
            Id          = 1,
            Kind        = SlideShapeKind.Table,
            OffsetXEmu  = offsetX,
            OffsetYEmu  = offsetY,
            ExtentCxEmu = colWidthEmu * cols,
            ExtentCyEmu = rowHeightEmu * rows,
            Table       = table,
        };
    }

    // 1 EMU = 1/9525 DIP
    private const double Dip = 1.0 / 9525.0;

    [Fact]
    public void TableCellHitTester_HitTest_ReturnsNullOutsideFrame()
    {
        var shape = MakeTableShape(2, 2);
        // Point far outside.
        var result = FreeP.App.Compositor.TableCellHitTester.HitTest(shape, 1e6, 1e6);
        result.Should().BeNull();
    }

    [Fact]
    public void TableCellHitTester_HitTest_TopLeftCell()
    {
        var shape = MakeTableShape(3, 3, colWidthEmu: 914400L, rowHeightEmu: 457200L);
        // Click at centre of first cell (DIP).
        double x = 914400L / 9525.0 * 0.5;
        double y = 457200L / 9525.0 * 0.5;
        var result = FreeP.App.Compositor.TableCellHitTester.HitTest(shape, x, y);
        result.Should().Be((0, 0));
    }

    [Fact]
    public void TableCellHitTester_HitTest_BottomRightCell()
    {
        var shape = MakeTableShape(3, 3, colWidthEmu: 914400L, rowHeightEmu: 457200L);
        double colW = 914400L / 9525.0;
        double rowH = 457200L / 9525.0;
        double x = colW * 2 + colW * 0.5; // centre of column 2
        double y = rowH * 2 + rowH * 0.5; // centre of row 2
        var result = FreeP.App.Compositor.TableCellHitTester.HitTest(shape, x, y);
        result.Should().Be((2, 2));
    }

    [Fact]
    public void TableCellHitTester_HitTest_MiddleCell()
    {
        var shape = MakeTableShape(3, 3, colWidthEmu: 914400L, rowHeightEmu: 457200L);
        double colW = 914400L / 9525.0;
        double rowH = 457200L / 9525.0;
        double x = colW * 1 + colW * 0.5;
        double y = rowH * 1 + rowH * 0.5;
        var result = FreeP.App.Compositor.TableCellHitTester.HitTest(shape, x, y);
        result.Should().Be((1, 1));
    }

    [Fact]
    public void TableCellHitTester_HitTest_WithTableOffset()
    {
        // Table starts at (1 inch, 1 inch) = (914400 EMU, 914400 EMU).
        var shape = MakeTableShape(2, 2, offsetX: 914400L, offsetY: 914400L);
        double off = 914400L / 9525.0;
        double colW = 914400L / 9525.0;
        double rowH = 457200L / 9525.0;
        // Click on centre of cell (0,1).
        double x = off + colW + colW * 0.5;
        double y = off + rowH * 0.5;
        var result = FreeP.App.Compositor.TableCellHitTester.HitTest(shape, x, y);
        result.Should().Be((0, 1));
    }

    [Fact]
    public void TableCellHitTester_GetCellRect_ReturnsCorrectBoundsForFirstCell()
    {
        var shape = MakeTableShape(2, 3, colWidthEmu: 914400L, rowHeightEmu: 457200L);
        var rect = FreeP.App.Compositor.TableCellHitTester.GetCellRect(shape, 0, 0);
        rect.Should().NotBeNull();
        rect!.Value.X.Should().BeApproximately(0, 0.001);
        rect.Value.Y.Should().BeApproximately(0, 0.001);
        rect.Value.Width.Should().BeApproximately(914400L / 9525.0, 0.001);
        rect.Value.Height.Should().BeApproximately(457200L / 9525.0, 0.001);
    }

    [Fact]
    public void TableCellHitTester_GetCellRect_ReturnsNullForOutOfBoundsRow()
    {
        var shape = MakeTableShape(2, 2);
        var rect = FreeP.App.Compositor.TableCellHitTester.GetCellRect(shape, 99, 0);
        rect.Should().BeNull();
    }

    [Fact]
    public void TableCellHitTester_HitTest_HMergeReturnsAnchor()
    {
        var shape = MakeTableShape(2, 3, colWidthEmu: 914400L, rowHeightEmu: 457200L);
        // Manually set up a merge: anchor at (0,0) with GridSpan=2, cells (0,1) as HMerge.
        shape.Table!.Rows[0].Cells[0].GridSpan = 2;
        shape.Table.Rows[0].Cells[1].HMerge = true;

        // Click in the area that is "owned" by cell (0,1) but it is HMerge.
        double colW = 914400L / 9525.0;
        double rowH = 457200L / 9525.0;
        double x = colW + colW * 0.5; // centre of slot (0,1)
        double y = rowH * 0.5;
        var result = FreeP.App.Compositor.TableCellHitTester.HitTest(shape, x, y);
        // Should resolve to anchor (0,0).
        result.Should().Be((0, 0));
    }
}
