using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

public sealed class FinalFiveCommandIoTests
{
    [Fact]
    public void FieldAndErasedTableBorder_RoundTripWithoutFlatteningStructure()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" TITLE ", "Parity Report") },
        });
        var table = Table.Create(1, 2);
        document.Blocks.Add(table);
        var bus = new DocumentCommandBus(new Context(document));
        bus.Execute(new MergeCellsHorizontalCommand(1, 0, 0, 1));

        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        var restored = DocxReader.Read(stream);

        var field = restored.Blocks.OfType<Paragraph>().Single().Runs.Single();
        field.ComplexField!.Instruction.Should().Be(" TITLE ");
        field.Text.Should().Be("Parity Report");
        var restoredTable = restored.Blocks.OfType<Table>().Single();
        restoredTable.Rows[0].Cells.Should().ContainSingle();
        restoredTable.Rows[0].Cells[0].GridSpan.Should().Be(2);
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
