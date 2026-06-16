namespace FreeW.Core.Model.Tests;

public class DocumentCommandBusTests
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
    public void InsertParagraph_Execute_Undo_Redo()
    {
        var (doc, bus) = New();

        bus.Execute(new InsertParagraphCommand(0, new Paragraph("A")));
        doc.PlainText.Should().Be("A");
        bus.CanUndo.Should().BeTrue();

        bus.Undo().Should().BeTrue();
        doc.Blocks.Should().BeEmpty();
        bus.CanRedo.Should().BeTrue();

        bus.Redo().Should().BeTrue();
        doc.PlainText.Should().Be("A");
    }

    [Fact]
    public void NewCommand_InvalidatesRedo()
    {
        var (doc, bus) = New();
        bus.Execute(new InsertParagraphCommand(0, new Paragraph("A")));
        bus.Undo();
        bus.CanRedo.Should().BeTrue();

        bus.Execute(new InsertParagraphCommand(0, new Paragraph("B")));

        bus.CanRedo.Should().BeFalse();
        doc.PlainText.Should().Be("B");
    }

    [Fact]
    public void DeleteParagraph_Undo_RestoresSameInstance()
    {
        var (doc, bus) = New();
        var p = new Paragraph("keep");
        doc.Blocks.Add(p);

        bus.Execute(new DeleteParagraphCommand(0));
        doc.Blocks.Should().BeEmpty();

        bus.Undo();
        doc.Blocks.Should().ContainSingle().Which.Should().BeSameAs(p);
    }

    [Fact]
    public void FormatParagraphRuns_TogglesBold_AndReverts()
    {
        var (doc, bus) = New();
        var p = new Paragraph();
        p.Runs.Add(new Run("x"));
        p.Runs.Add(new Run("y"));
        doc.Blocks.Add(p);

        bus.Execute(new FormatParagraphRunsCommand(0, f => f with { Bold = true }));
        p.Runs.Should().OnlyContain(r => r.Formatting.Bold);

        bus.Undo();
        p.Runs.Should().OnlyContain(r => !r.Formatting.Bold);
    }

    [Fact]
    public void SetParagraphFormatting_Applies_AndReverts()
    {
        var (doc, bus) = New();
        doc.Blocks.Add(new Paragraph("p"));
        var centered = ParagraphFormatting.Default with { Alignment = TextAlignment.Center };

        bus.Execute(new SetParagraphFormattingCommand(0, centered));
        doc.Paragraphs.First().Formatting.Alignment.Should().Be(TextAlignment.Center);

        bus.Undo();
        doc.Paragraphs.First().Formatting.Alignment.Should().Be(TextAlignment.Left);
    }

    [Fact]
    public void InsertBlock_Table_Execute_Undo_Redo()
    {
        var (doc, bus) = New();
        doc.Blocks.Add(new Paragraph("p"));
        var table = Table.Create(2, 2);

        bus.Execute(new InsertBlockCommand(1, table));
        doc.Blocks.Should().HaveCount(2);
        doc.Blocks[1].Should().BeSameAs(table);

        bus.Undo();
        doc.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>();

        bus.Redo();
        doc.Blocks[1].Should().BeSameAs(table);
    }

    [Fact]
    public void Undo_WhenEmpty_ReturnsFalse()
    {
        var (_, bus) = New();
        bus.CanUndo.Should().BeFalse();
        bus.Undo().Should().BeFalse();
    }
}
