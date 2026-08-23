namespace FreeW.Core.Model.Tests;

public sealed class TextDocumentStoryTraversalAdoptionTests
{
    [Fact]
    public void DocumentInspector_PreservesItsBodyOnlyShapeExpansion()
    {
        var bodyShapeParagraph = CommentParagraph(1);
        var headerShapeParagraph = CommentParagraph(2);
        var directHeaderParagraph = CommentParagraph(3);
        directHeaderParagraph.Runs.Add(new Run(string.Empty)
        {
            Shape = new Shape { TextParagraphs = { headerShapeParagraph } },
        });

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(ShapeHost(bodyShapeParagraph));
        document.Header = new HeaderFooter();
        document.Header.Paragraphs.Add(directHeaderParagraph);
        document.Comments[1] = new Comment(1, "body shape", "A", "A");
        document.Comments[2] = new Comment(2, "header shape", "A", "A");
        document.Comments[3] = new Comment(3, "header", "A", "A");

        DocumentInspector.RemoveComments(document);

        bodyShapeParagraph.Runs.Should().OnlyContain(run => run.CommentId == null && !run.IsCommentReference);
        directHeaderParagraph.Runs.Should().OnlyContain(run => run.CommentId == null && !run.IsCommentReference);
        headerShapeParagraph.Runs.Should().Contain(run => run.CommentId == 2);
    }

    [Fact]
    public void DeleteComment_PreservesNestedTableReachWithoutExpandingTextBoxes()
    {
        var nestedParagraph = CommentParagraph(7);
        var shapeParagraph = CommentParagraph(7);
        var outer = Table.Create(1, 1);
        var nested = Table.Create(1, 1);
        nested.Rows[0].Cells[0].Paragraphs[0] = nestedParagraph;
        outer.Rows[0].Cells[0].NestedTables.Add(nested);

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(outer);
        document.Blocks.Add(ShapeHost(shapeParagraph));
        document.Comments[7] = new Comment(7, "comment", "A", "A");

        new DocumentCommandBus(new Context(document)).Execute(new DeleteCommentCommand(7));

        nestedParagraph.Runs.Should().OnlyContain(run => run.CommentId == null && !run.IsCommentReference);
        shapeParagraph.Runs.Should().Contain(run => run.CommentId == 7);
    }

    [Fact]
    public void DeleteNote_ExpandsShapeTextBoxesButExcludesNoteContentStories()
    {
        var bodyShapeParagraph = NoteParagraph(4);
        var headerShapeParagraph = NoteParagraph(4);
        var endnoteParagraph = NoteParagraph(4);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(ShapeHost(bodyShapeParagraph));
        document.Header = new HeaderFooter();
        document.Header.Paragraphs.Add(ShapeHost(headerShapeParagraph));
        document.Footnotes[4] = new Footnote(4, "deleted");
        var endnote = new Endnote(9);
        endnote.Content.Add(endnoteParagraph);
        document.Endnotes[9] = endnote;

        new DocumentCommandBus(new Context(document)).Execute(new DeleteNoteCommand(4, footnote: true));

        bodyShapeParagraph.Runs.Should().NotContain(run => run.FootnoteId == 4);
        headerShapeParagraph.Runs.Should().NotContain(run => run.FootnoteId == 4);
        endnoteParagraph.Runs.Should().Contain(run => run.FootnoteId == 4);
    }

    private static Paragraph ShapeHost(Paragraph content)
    {
        var host = new Paragraph();
        host.Runs.Add(new Run(string.Empty)
        {
            Shape = new Shape { TextParagraphs = { content } },
        });
        return host;
    }

    private static Paragraph CommentParagraph(int id)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("text") { CommentId = id });
        paragraph.Runs.Add(Run.CommentReference(id));
        return paragraph;
    }

    private static Paragraph NoteParagraph(int id)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("text"));
        paragraph.Runs.Add(Run.FootnoteReference(id));
        return paragraph;
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
