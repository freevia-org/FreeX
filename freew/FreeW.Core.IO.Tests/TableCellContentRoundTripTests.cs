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
}
