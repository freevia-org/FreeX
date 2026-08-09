using System.Collections.Generic;
using System.IO;
using System.Linq;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// DOCX round-trip tests for <see cref="SetTableCellContentCommand"/>:
/// verifies that cell content written via the command survives DocxWriter -> DocxReader.
/// </summary>
public class TableCellContentRoundTripTests
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

    [Fact]
    public void SetCellContent_DocxRoundTrip_ContentPreserved()
    {
        var (doc, bus) = New();
        var table = Table.Create(2, 2);
        doc.Blocks.Add(table);

        bus.Execute(new SetTableCellContentCommand(0, 0, 1, [new Paragraph("Merged R0C1")]));
        bus.Execute(new SetTableCellContentCommand(0, 1, 0, [new Paragraph("Merged R1C0")]));

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        var read = DocxReader.Read(stream);

        read.Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<Table>("the single body block must be a table");
        var readTable = (Table)read.Blocks[0];

        readTable.Rows[0].Cells[1].PlainText.Should().Be("Merged R0C1",
            "cell (0,1) content must survive DOCX round-trip");
        readTable.Rows[1].Cells[0].PlainText.Should().Be("Merged R1C0",
            "cell (1,0) content must survive DOCX round-trip");
    }

    [Fact]
    public void SetCellContent_MultiParagraph_DocxRoundTrip()
    {
        var (doc, bus) = New();
        var table = Table.Create(1, 1);
        doc.Blocks.Add(table);

        bus.Execute(new SetTableCellContentCommand(0, 0, 0, [
            new Paragraph("Line 1"),
            new Paragraph("Line 2"),
        ]));

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        var read = DocxReader.Read(stream);

        var cell = ((Table)read.Blocks[0]).Rows[0].Cells[0];
        cell.Paragraphs.Should().HaveCount(2, "two paragraphs must survive DOCX round-trip");
        cell.Paragraphs[0].PlainText.Should().Be("Line 1");
        cell.Paragraphs[1].PlainText.Should().Be("Line 2");
    }

    [Fact]
    public void NestedTableInsideCell_DocxRoundTrip_TableSurvivesNotJustItsParagraphs()
    {
        // A cell can contain a whole sub-table (tc/w:tbl), not just paragraphs. Build one directly via
        // the model, write it, and read it back: the nested table itself (not merely some paragraph text
        // scraped out of it) must come back with its own distinct row/column content intact.
        var doc = new TextDocument();
        var outerTable = Table.Create(1, 1);
        var nestedTable = Table.Create(2, 2);
        nestedTable.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("Inner R0C0");
        nestedTable.Rows[0].Cells[1].Paragraphs[0] = new Paragraph("Inner R0C1");
        nestedTable.Rows[1].Cells[0].Paragraphs[0] = new Paragraph("Inner R1C0");
        nestedTable.Rows[1].Cells[1].Paragraphs[0] = new Paragraph("Inner R1C1");
        outerTable.Rows[0].Cells[0].NestedTables.Add(nestedTable);
        doc.Blocks.Add(outerTable);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        var read = DocxReader.Read(stream);

        var readOuterTable = read.Blocks.OfType<Table>().Should().ContainSingle(
            "the outer table must survive the round-trip").Which;
        var readCell = readOuterTable.Rows[0].Cells[0];
        readCell.NestedTables.Should().ContainSingle(
            "the nested table must be preserved, not dropped on the floor during import");
        var readNestedTable = readCell.NestedTables[0];
        readNestedTable.Rows.Should().HaveCount(2);
        readNestedTable.Rows[0].Cells.Select(c => c.PlainText).Should().Equal("Inner R0C0", "Inner R0C1");
        readNestedTable.Rows[1].Cells.Select(c => c.PlainText).Should().Equal("Inner R1C0", "Inner R1C1");
    }

    [Fact]
    public void CellWithNestedTableAndOwnParagraph_DocxRoundTrip_BothSurvive()
    {
        // Sibling/no-regression coverage: a cell holding BOTH a nested table AND its own real paragraph
        // text (the ordinary, already-working path) must keep both after the round trip — the nested-
        // table fix must not swallow or duplicate the cell's own paragraph content.
        var doc = new TextDocument();
        var outerTable = Table.Create(1, 1);
        var cell = outerTable.Rows[0].Cells[0];
        cell.Paragraphs[0] = new Paragraph("Caption above sub-table");
        var nestedTable = Table.Create(1, 1);
        nestedTable.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("Sole inner cell");
        cell.NestedTables.Add(nestedTable);
        doc.Blocks.Add(outerTable);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        var read = DocxReader.Read(stream);

        var readCell = ((Table)read.Blocks.OfType<Table>().Single()).Rows[0].Cells[0];
        readCell.Paragraphs.Should().ContainSingle().Which.PlainText.Should().Be("Caption above sub-table");
        readCell.NestedTables.Should().ContainSingle()
            .Which.Rows[0].Cells[0].PlainText.Should().Be("Sole inner cell");
    }
}
