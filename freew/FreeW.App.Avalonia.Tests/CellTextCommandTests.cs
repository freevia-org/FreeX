using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public class CellTextCommandTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }

    private static (TextDocument Doc, DocumentCommandBus Bus, Table Table) NewTableDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(2, 2);
        table.Rows[0].Cells[0] = new TableCell("A1");
        table.Rows[0].Cells[1] = new TableCell("B1");
        table.Rows[1].Cells[0] = new TableCell("A2");
        table.Rows[1].Cells[1] = new TableCell("B2");
        doc.Blocks.Add(table);
        return (doc, new DocumentCommandBus(new Context(doc)), table);
    }

    [Fact]
    public void Sets_cell_text()
    {
        var (_, bus, table) = NewTableDocument();
        bus.Execute(new CellTextCommand(0, 1, 0, "edited"));
        table.Rows[1].Cells[0].PlainText.Should().Be("edited");
    }

    [Fact]
    public void Undo_restores_previous_cell_text()
    {
        var (_, bus, table) = NewTableDocument();
        bus.Execute(new CellTextCommand(0, 0, 1, "changed"));
        table.Rows[0].Cells[1].PlainText.Should().Be("changed");

        bus.Undo().Should().BeTrue();
        table.Rows[0].Cells[1].PlainText.Should().Be("B1");

        bus.Redo().Should().BeTrue();
        table.Rows[0].Cells[1].PlainText.Should().Be("changed");
    }

    [Fact]
    public void Out_of_range_indices_are_a_no_op()
    {
        var (_, bus, table) = NewTableDocument();
        bus.Execute(new CellTextCommand(0, 9, 9, "nope"));
        table.Rows[0].Cells[0].PlainText.Should().Be("A1");
    }
}
