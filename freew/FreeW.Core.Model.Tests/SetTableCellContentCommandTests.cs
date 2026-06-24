using System.Collections.Generic;
using System.Linq;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// Tests for the <see cref="SetTableCellContentCommand"/> table-cell content-write API:
/// <list type="bullet">
///   <item>Writing content into cell (b,r,c) sets exactly that cell's paragraphs.</item>
///   <item>Undo restores the original paragraphs exactly (same instances).</item>
///   <item>Redo re-applies the replacement.</item>
///   <item>Table structure (other cells, rows, widths, merge state) is preserved.</item>
///   <item>Out-of-range coordinates are silent no-ops.</item>
/// </list>
/// DOCX round-trip coverage is in FreeW.Core.IO.Tests/TableCellContentRoundTripTests.cs.
/// </summary>
public class SetTableCellContentCommandTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private static (TextDocument doc, DocumentCommandBus bus) New()
    {
        var doc = new TextDocument();
        return (doc, new DocumentCommandBus(new Context(doc)));
    }

    // ── core correctness ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SetCellContent_WritesToExactCell()
    {
        var (doc, bus) = New();
        var table = Table.Create(2, 3);
        doc.Blocks.Add(table);

        var newParas = new List<Paragraph> { new Paragraph("Row1Col2 content") };

        bus.Execute(new SetTableCellContentCommand(0, 1, 2, newParas));

        // Targeted cell must have the new content.
        table.Rows[1].Cells[2].PlainText.Should().Be("Row1Col2 content");

        // Adjacent cells must be unchanged (default empty paragraph from Table.Create).
        table.Rows[0].Cells[0].Paragraphs.Should().ContainSingle();
        table.Rows[1].Cells[0].Paragraphs.Should().ContainSingle();
        table.Rows[0].Cells[2].Paragraphs.Should().ContainSingle();
    }

    [Fact]
    public void SetCellContent_IsUndoable()
    {
        var (doc, bus) = New();
        var table = Table.Create(1, 2);
        // Seed cell (0,0) with known content.
        var originalPara = new Paragraph("Original");
        table.Rows[0].Cells[0].Paragraphs.Clear();
        table.Rows[0].Cells[0].Paragraphs.Add(originalPara);
        doc.Blocks.Add(table);

        var replacement = new List<Paragraph> { new Paragraph("Replacement") };
        bus.Execute(new SetTableCellContentCommand(0, 0, 0, replacement));
        table.Rows[0].Cells[0].PlainText.Should().Be("Replacement");

        bus.Undo();

        // After undo the original paragraph instance must be restored.
        table.Rows[0].Cells[0].Paragraphs.Should().ContainSingle()
            .Which.Should().BeSameAs(originalPara,
                "undo must restore the exact original paragraph instances, not clones");
        table.Rows[0].Cells[0].PlainText.Should().Be("Original");
    }

    [Fact]
    public void SetCellContent_IsRedoable()
    {
        var (doc, bus) = New();
        var table = Table.Create(1, 1);
        doc.Blocks.Add(table);

        var replacement = new List<Paragraph> { new Paragraph("Redo target") };
        bus.Execute(new SetTableCellContentCommand(0, 0, 0, replacement));
        bus.Undo();
        bus.Redo();

        table.Rows[0].Cells[0].PlainText.Should().Be("Redo target");
    }

    [Fact]
    public void SetCellContent_PreservesTableStructure()
    {
        var (doc, bus) = New();
        var table = Table.Create(2, 2);
        table.Rows[0].Cells[0].GridSpan = 2;
        table.Rows[1].Cells[1].ShadingColorHex = "FF0000";
        table.ColumnWidthsPt.AddRange([100.0, 120.0]);
        doc.Blocks.Add(table);

        bus.Execute(new SetTableCellContentCommand(0, 0, 0, [new Paragraph("new")]));

        // Structure preserved.
        table.Rows.Should().HaveCount(2, "row count must not change");
        table.Rows[0].Cells.Should().HaveCount(2, "column count must not change");
        table.Rows[0].Cells[0].GridSpan.Should().Be(2, "GridSpan on other cells must survive");
        table.Rows[1].Cells[1].ShadingColorHex.Should().Be("FF0000", "ShadingColorHex must survive");
        table.ColumnWidthsPt.Should().Equal([100.0, 120.0], "ColumnWidthsPt must survive");
    }

    [Fact]
    public void SetCellContent_MultiParagraph_AllParagraphsWritten()
    {
        var (doc, bus) = New();
        var table = Table.Create(1, 1);
        doc.Blocks.Add(table);

        var paras = new List<Paragraph>
        {
            new Paragraph("Line 1"),
            new Paragraph("Line 2"),
            new Paragraph("Line 3"),
        };
        bus.Execute(new SetTableCellContentCommand(0, 0, 0, paras));

        var cell = table.Rows[0].Cells[0];
        cell.Paragraphs.Should().HaveCount(3);
        cell.Paragraphs[0].PlainText.Should().Be("Line 1");
        cell.Paragraphs[1].PlainText.Should().Be("Line 2");
        cell.Paragraphs[2].PlainText.Should().Be("Line 3");
    }

    [Fact]
    public void SetCellContent_EmptyReplacement_LeavesOneEmptyParagraph()
    {
        var (doc, bus) = New();
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("some text"));
        doc.Blocks.Add(table);

        // Pass an empty replacement list — the command must ensure at least one paragraph.
        bus.Execute(new SetTableCellContentCommand(0, 0, 0, []));

        table.Rows[0].Cells[0].Paragraphs.Should().ContainSingle(
            "a cell must always have at least one paragraph after a set");
        table.Rows[0].Cells[0].PlainText.Should().Be(string.Empty);
    }

    [Fact]
    public void SetCellContent_OutOfRangeRow_IsNoOp()
    {
        var (doc, bus) = New();
        var table = Table.Create(1, 1);
        doc.Blocks.Add(table);

        // Row index 99 does not exist — must not throw, must not change anything.
        bus.Execute(new SetTableCellContentCommand(0, 99, 0, [new Paragraph("oob")]));

        table.Rows[0].Cells[0].Paragraphs.Should().ContainSingle();
    }

    [Fact]
    public void SetCellContent_OutOfRangeColumn_IsNoOp()
    {
        var (doc, bus) = New();
        var table = Table.Create(1, 1);
        doc.Blocks.Add(table);

        bus.Execute(new SetTableCellContentCommand(0, 0, 99, [new Paragraph("oob")]));

        table.Rows[0].Cells[0].Paragraphs.Should().ContainSingle();
    }

}
