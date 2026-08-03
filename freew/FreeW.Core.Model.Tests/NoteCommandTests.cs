namespace FreeW.Core.Model.Tests;

public sealed class NoteCommandTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    [Fact]
    public void ReplaceNoteContent_PreservesRichParagraphs_AndUndoRedo()
    {
        var document = TextDocument.CreateEmpty();
        var original = new Paragraph();
        original.Runs.Add(new Run("original")
        {
            Formatting = RunFormatting.Default with { Italic = true },
        });
        var footnote = new Footnote(7);
        footnote.Content.Add(original);
        document.Footnotes[7] = footnote;
        var bus = new DocumentCommandBus(new Context(document));

        var replacement = new Paragraph { StyleId = "FootnoteText" };
        replacement.Runs.Add(new Run("updated")
        {
            Formatting = RunFormatting.Default with { Bold = true, ColorHex = "#C00000" },
        });
        bus.Execute(new ReplaceNoteContentCommand(7, footnote: true, [replacement, new Paragraph("more")]));

        document.Footnotes[7].PlainText.Should().Be("updated\nmore");
        document.Footnotes[7].Content[0].StyleId.Should().Be("FootnoteText");
        document.Footnotes[7].Content[0].Runs.Single().Formatting.Bold.Should().BeTrue();

        bus.Undo().Should().BeTrue();
        document.Footnotes[7].PlainText.Should().Be("original");
        document.Footnotes[7].Content.Single().Runs.Single().Formatting.Italic.Should().BeTrue();

        bus.Redo().Should().BeTrue();
        document.Footnotes[7].PlainText.Should().Be("updated\nmore");
        document.Footnotes[7].Content[0].Runs.Single().Formatting.ColorHex.Should().Be("#C00000");
    }

    [Fact]
    public void ReplaceNoteContent_EmptyReplacement_LeavesEditableBlankParagraph()
    {
        var document = TextDocument.CreateEmpty();
        document.Endnotes[3] = new Endnote(3, "old");
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new ReplaceNoteContentCommand(3, footnote: false, []));

        document.Endnotes[3].Content.Should().ContainSingle();
        document.Endnotes[3].PlainText.Should().BeEmpty();
        bus.Undo().Should().BeTrue();
        document.Endnotes[3].PlainText.Should().Be("old");
    }

    [Fact]
    public void DeleteNote_RemovesNestedTableMarker_AndUndoRedoRestoresExactRuns()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var body = new Paragraph();
        body.Runs.Add(new Run("body"));
        body.Runs.Add(Run.FootnoteReference(4));
        document.Blocks.Add(body);

        var table = Table.Create(1, 1);
        var cellParagraph = table.Rows[0].Cells[0].Paragraphs[0];
        cellParagraph.Runs.Clear();
        cellParagraph.Runs.Add(new Run("cell-before"));
        cellParagraph.Runs.Add(Run.FootnoteReference(4));
        cellParagraph.Runs.Add(new Run("cell-after"));
        document.Blocks.Add(table);
        document.Footnotes[4] = new Footnote(4, "nested marker note");
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new DeleteNoteCommand(4, footnote: true));

        document.Footnotes.Should().NotContainKey(4);
        body.Runs.Should().NotContain(run => run.FootnoteId == 4);
        cellParagraph.Runs.Should().NotContain(run => run.FootnoteId == 4);

        bus.Undo().Should().BeTrue();
        document.Footnotes[4].PlainText.Should().Be("nested marker note");
        body.Runs.Select(run => (run.Text, run.FootnoteId))
            .Should().Equal(("body", null), ("4", 4));
        cellParagraph.Runs.Select(run => (run.Text, run.FootnoteId))
            .Should().Equal(("cell-before", null), ("4", 4), ("cell-after", null));

        bus.Redo().Should().BeTrue();
        document.Footnotes.Should().NotContainKey(4);
        cellParagraph.Runs.Should().NotContain(run => run.FootnoteId == 4);
    }
}
