namespace FreeW.Core.Model.Tests;

/// <summary>
/// AV-TBL4: Tests for <see cref="SetCellShadingCommand"/> and <see cref="SetCellBordersCommand"/>.
/// Covers: set/clear shading; set/clear per-edge borders; undo/redo; out-of-range no-op;
/// ApplyEdges helper for All/Outside/Inside composite selectors.
/// </summary>
public sealed class SetCellShadingBordersCommandTests
{
    [Fact]
    public void CellBorderPayloadAndTextDirection_AreUndoableAndRedoable()
    {
        var (_, bus, table) = Make2x2();
        var cell = table.Rows[0].Cells[0];
        var borders = new CellBorders
        {
            Top = new CellBorderEdge(BorderLineStyle.Double, "#123456", 1.5),
        };

        bus.Execute(new SetCellBorderPayloadCommand(0, 0, 0, borders));
        cell.Borders.Should().BeSameAs(borders);
        bus.Undo().Should().BeTrue();
        cell.Borders.Should().BeNull();
        bus.Redo().Should().BeTrue();
        cell.Borders.Should().BeSameAs(borders);

        bus.Execute(new SetCellTextDirectionCommand(0, 0, 0, CellTextDirection.Rotate270));
        cell.TextDirection.Should().Be(CellTextDirection.Rotate270);
        bus.Undo().Should().BeTrue();
        cell.TextDirection.Should().Be(CellTextDirection.Horizontal);
        bus.Redo().Should().BeTrue();
        cell.TextDirection.Should().Be(CellTextDirection.Rotate270);
    }

    private sealed class Ctx(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private static (TextDocument doc, DocumentCommandBus bus, Table tbl) Make2x2()
    {
        var doc = new TextDocument();
        var tbl = Table.Create(2, 2);
        doc.Blocks.Add(tbl);
        var bus = new DocumentCommandBus(new Ctx(doc));
        return (doc, bus, tbl);
    }

    // ── SetCellShadingCommand ────────────────────────────────────────────────────────────────────

    [Fact]
    public void SetCellShading_AppliesColorToTargetCell()
    {
        var (_, bus, tbl) = Make2x2();
        bus.Execute(new SetCellShadingCommand(0, 0, 0, "#FFFF00"));
        tbl.Rows[0].Cells[0].ShadingColorHex.Should().Be("#FFFF00");
        // Other cells untouched.
        tbl.Rows[0].Cells[1].ShadingColorHex.Should().BeNull();
        tbl.Rows[1].Cells[0].ShadingColorHex.Should().BeNull();
    }

    [Fact]
    public void SetCellShading_ClearsWithNull()
    {
        var (_, bus, tbl) = Make2x2();
        tbl.Rows[0].Cells[0].ShadingColorHex = "#FF0000";
        bus.Execute(new SetCellShadingCommand(0, 0, 0, null));
        tbl.Rows[0].Cells[0].ShadingColorHex.Should().BeNull();
    }

    [Fact]
    public void SetCellShading_ClearsWithEmpty()
    {
        var (_, bus, tbl) = Make2x2();
        tbl.Rows[0].Cells[0].ShadingColorHex = "#FF0000";
        bus.Execute(new SetCellShadingCommand(0, 0, 0, string.Empty));
        tbl.Rows[0].Cells[0].ShadingColorHex.Should().BeNull();
    }

    [Fact]
    public void SetCellShading_IsUndoable()
    {
        var (_, bus, tbl) = Make2x2();
        tbl.Rows[1].Cells[1].ShadingColorHex = "#AABBCC";
        bus.Execute(new SetCellShadingCommand(0, 1, 1, "#FFFF00"));
        tbl.Rows[1].Cells[1].ShadingColorHex.Should().Be("#FFFF00");
        bus.Undo();
        tbl.Rows[1].Cells[1].ShadingColorHex.Should().Be("#AABBCC", "undo should restore previous shading");
    }

    [Fact]
    public void SetCellShading_IsRedoable()
    {
        var (_, bus, tbl) = Make2x2();
        bus.Execute(new SetCellShadingCommand(0, 0, 1, "#123456"));
        bus.Undo();
        bus.Redo();
        tbl.Rows[0].Cells[1].ShadingColorHex.Should().Be("#123456", "redo should re-apply shading");
    }

    [Fact]
    public void SetCellShading_OutOfRangeIsNoOp()
    {
        var (_, bus, tbl) = Make2x2();
        // Row out of range.
        bus.Execute(new SetCellShadingCommand(0, 99, 0, "#FFFF00"));
        // Col out of range.
        bus.Execute(new SetCellShadingCommand(0, 0, 99, "#FFFF00"));
        // Block out of range.
        bus.Execute(new SetCellShadingCommand(99, 0, 0, "#FFFF00"));
        // Nothing should have changed.
        foreach (var row in tbl.Rows)
            foreach (var cell in row.Cells)
                cell.ShadingColorHex.Should().BeNull("out-of-range commands must be no-ops");
    }

    // ── SetCellBordersCommand ────────────────────────────────────────────────────────────────────

    [Fact]
    public void SetCellBorders_All_SetsAllFourEdges()
    {
        var (_, bus, tbl) = Make2x2();
        bus.Execute(new SetCellBordersCommand(0, 0, 0, CellBorderEdges.All,
            BorderLineStyle.Single, "#000000", 1.0));
        var borders = tbl.Rows[0].Cells[0].Borders;
        borders.Should().NotBeNull();
        borders!.Top.Should().NotBeNull();
        borders.Bottom.Should().NotBeNull();
        borders.Left.Should().NotBeNull();
        borders.Right.Should().NotBeNull();
    }

    [Fact]
    public void SetCellBorders_Top_SetsOnlyTopEdge()
    {
        var (_, bus, tbl) = Make2x2();
        bus.Execute(new SetCellBordersCommand(0, 0, 0, CellBorderEdges.Top,
            BorderLineStyle.Dashed, "#FF0000", 2.0));
        var borders = tbl.Rows[0].Cells[0].Borders;
        borders.Should().NotBeNull();
        borders!.Top.Should().NotBeNull();
        borders.Top!.Style.Should().Be(BorderLineStyle.Dashed);
        borders.Top.ColorHex.Should().Be("#FF0000");
        borders.Top.WidthPt.Should().Be(2.0);
        borders.Bottom.Should().BeNull("only Top was requested");
        borders.Left.Should().BeNull();
        borders.Right.Should().BeNull();
    }

    [Fact]
    public void SetCellBorders_PreservesExistingEdgesNotSelected()
    {
        var (_, bus, tbl) = Make2x2();
        // Set all four first.
        bus.Execute(new SetCellBordersCommand(0, 0, 0, CellBorderEdges.All,
            BorderLineStyle.Single, "#000000", 0.5));
        // Now only update Top.
        bus.Execute(new SetCellBordersCommand(0, 0, 0, CellBorderEdges.Top,
            BorderLineStyle.Dashed, "#FF0000", 1.5));
        var borders = tbl.Rows[0].Cells[0].Borders!;
        borders.Top!.Style.Should().Be(BorderLineStyle.Dashed, "Top should have the new dashed style");
        borders.Bottom!.Style.Should().Be(BorderLineStyle.Single, "Bottom should be unchanged");
        borders.Left!.Style.Should().Be(BorderLineStyle.Single, "Left should be unchanged");
        borders.Right!.Style.Should().Be(BorderLineStyle.Single, "Right should be unchanged");
    }

    [Fact]
    public void SetCellBorders_Clear_RemovesEdges()
    {
        var (_, bus, tbl) = Make2x2();
        bus.Execute(new SetCellBordersCommand(0, 0, 0, CellBorderEdges.All,
            BorderLineStyle.Single, "#000000", 0.5));
        // Clear all.
        bus.Execute(new SetCellBordersCommand(0, 0, 0, CellBorderEdges.All,
            BorderLineStyle.Single, "#000000", 0.5, clearEdges: true));
        tbl.Rows[0].Cells[0].Borders.Should().BeNull("all edges cleared → Borders collapses to null");
    }

    [Fact]
    public void SetCellBorders_IsUndoable()
    {
        var (_, bus, tbl) = Make2x2();
        bus.Execute(new SetCellBordersCommand(0, 0, 0, CellBorderEdges.All,
            BorderLineStyle.Single, "#000000", 0.5));
        bus.Undo();
        tbl.Rows[0].Cells[0].Borders.Should().BeNull("undo should restore null borders");
    }

    [Fact]
    public void SetCellBorders_OutOfRangeIsNoOp()
    {
        var (_, bus, tbl) = Make2x2();
        bus.Execute(new SetCellBordersCommand(0, 99, 0, CellBorderEdges.All,
            BorderLineStyle.Single, "#000000", 0.5));
        bus.Execute(new SetCellBordersCommand(0, 0, 99, CellBorderEdges.All,
            BorderLineStyle.Single, "#000000", 0.5));
        foreach (var row in tbl.Rows)
            foreach (var cell in row.Cells)
                cell.Borders.Should().BeNull("out-of-range commands must be no-ops");
    }

    // ── Edge accumulation via bus: set + partial-clear ───────────────────────────────────────────

    [Fact]
    public void SetCellBorders_SequentialCommands_AccumulateEdgesCorrectly()
    {
        // Set All, then clear only Top — the other three should survive.
        var (_, bus, tbl) = Make2x2();
        bus.Execute(new SetCellBordersCommand(0, 0, 0, CellBorderEdges.All,
            BorderLineStyle.Single, "#000000", 1.0));
        bus.Execute(new SetCellBordersCommand(0, 0, 0, CellBorderEdges.Top,
            BorderLineStyle.Single, "#000000", 1.0, clearEdges: true));
        var b = tbl.Rows[0].Cells[0].Borders;
        b.Should().NotBeNull();
        b!.Top.Should().BeNull("Top was cleared");
        b.Bottom.Should().NotBeNull("Bottom should survive");
        b.Left.Should().NotBeNull("Left should survive");
        b.Right.Should().NotBeNull("Right should survive");
    }

    [Fact]
    public void SetCellBorders_ClearAll_LeavesNullBorders()
    {
        var (_, bus, tbl) = Make2x2();
        bus.Execute(new SetCellBordersCommand(0, 0, 0, CellBorderEdges.All,
            BorderLineStyle.Single, "#000000", 1.0));
        bus.Execute(new SetCellBordersCommand(0, 0, 0, CellBorderEdges.All,
            BorderLineStyle.Single, "#000000", 1.0, clearEdges: true));
        tbl.Rows[0].Cells[0].Borders.Should().BeNull("clearing all four edges collapses to null");
    }
}
