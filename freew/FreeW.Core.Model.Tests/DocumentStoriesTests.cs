namespace FreeW.Core.Model.Tests;

public class DocumentStoriesTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }

    private static TextDocument BuildDocument()
    {
        var document = new TextDocument();

        // Section 1 ends at the first body paragraph and owns its own header and first-page footer.
        var firstSection = new Section(new PageSettings());
        firstSection.HeadersFooters.Header = new HeaderFooter("S1 header");
        firstSection.HeadersFooters.FirstFooter = new HeaderFooter("S1 first footer");
        document.Blocks.Add(new Paragraph("Body one") { SectionBreak = firstSection });

        var host = new Paragraph("Body two ");
        host.Runs.Add(Run.FromShape(Shape.TextBoxWith("Box text", 100, 40)));
        document.Blocks.Add(host);

        document.Header = new HeaderFooter("Final header");
        document.Footer = new HeaderFooter("Final footer");
        document.FinalSectionHeadersFooters.EvenHeader = new HeaderFooter("Final even header");

        document.Footnotes[2] = new Footnote(2, "Footnote two");
        document.Footnotes[1] = new Footnote(1, "Footnote one");
        document.Footnotes[0] = new Footnote(0, "separator");
        document.Endnotes[1] = new Endnote(1, "Endnote one");

        var comment = new Comment(5, "Comment five", "Ann");
        comment.AddReply(6, "Reply six", "Bob");
        document.Comments[5] = comment;
        return document;
    }

    [Fact]
    public void Enumerate_WalksEveryNonBodyStoryInCanonicalOrder()
    {
        var stories = DocumentStories.Enumerate(BuildDocument()).ToList();

        stories.Select(story => (story.Kind, story.Address.Ordinal, story.StoryNumber, story.Paragraph.PlainText))
            .Should().Equal(
                (DocumentStoryKind.HeaderFooter, 0, 1, "S1 header"),
                (DocumentStoryKind.HeaderFooter, 1, 1, "S1 first footer"),
                (DocumentStoryKind.HeaderFooter, 2, 2, "Final header"),
                (DocumentStoryKind.HeaderFooter, 3, 2, "Final footer"),
                (DocumentStoryKind.HeaderFooter, 4, 2, "Final even header"),
                (DocumentStoryKind.Footnote, 0, 1, "Footnote one"),
                (DocumentStoryKind.Footnote, 1, 2, "Footnote two"),
                (DocumentStoryKind.Endnote, 0, 1, "Endnote one"),
                (DocumentStoryKind.Comment, 0, 5, "Comment five"),
                (DocumentStoryKind.Comment, 1, 6, "Reply six"),
                (DocumentStoryKind.TextBox, 0, 1, "Box text"));

        stories[1].Slot.Should().Be(StoryHeaderFooterSlot.FirstFooter);
        stories[4].Slot.Should().Be(StoryHeaderFooterSlot.EvenHeader);
        stories[^1].TextBoxHostRun.Should().NotBeNull();
    }

    [Fact]
    public void Enumerate_FindsTextBoxesAnchoredInTablesHeadersAndGroups()
    {
        var document = new TextDocument();
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        var cellParagraph = new Paragraph();
        cellParagraph.Runs.Add(Run.FromShape(Shape.TextBoxWith("In cell", 10, 10)));
        cell.Paragraphs.Add(cellParagraph);
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Blocks.Add(table);

        var group = new DrawingGroup();
        group.Children.Add(Shape.TextBoxWith("In group", 10, 10));
        group.Children.Add(Shape.Preset(ShapeKind.Rectangle, 10, 10));
        var groupHost = new Paragraph();
        groupHost.Runs.Add(Run.FromDrawingGroup(group));
        document.Blocks.Add(groupHost);

        var header = new HeaderFooter();
        var headerParagraph = new Paragraph("H ");
        headerParagraph.Runs.Add(Run.FromShape(Shape.TextBoxWith("In header", 10, 10)));
        header.Paragraphs.Add(headerParagraph);
        document.Header = header;

        DocumentStories.Enumerate(document)
            .Where(story => story.Kind == DocumentStoryKind.TextBox)
            .Select(story => story.Paragraph.PlainText)
            .Should().Equal("In cell", "In group", "In header");
    }

    [Fact]
    public void Resolve_ReturnsTheAddressedParagraph_OrNull()
    {
        var document = BuildDocument();

        DocumentStories.Resolve(document, new StoryParagraphAddress(DocumentStoryKind.Footnote, 1))!
            .Paragraph.PlainText.Should().Be("Footnote two");
        DocumentStories.Resolve(document, new StoryParagraphAddress(DocumentStoryKind.Endnote, 9)).Should().BeNull();
    }

    [Fact]
    public void EditStoryParagraphCommand_AppliesAndFullyRevertsAnInPlaceRunEdit()
    {
        var document = BuildDocument();
        var context = new Context(document);
        var address = new StoryParagraphAddress(DocumentStoryKind.Comment, 1);
        var command = new EditStoryParagraphCommand(
            address,
            paragraph =>
            {
                RevisionEditPlanner.TryReplacePlainTextRange(paragraph, 0, 5, "Answer");
                paragraph.StyleId = "Quote";
            });

        command.Apply(context);
        document.Comments[5].Replies[0].PlainText.Should().Be("Answer six");
        command.MutationKind.Should().Be(DocumentCommandMutationKind.Comment);

        command.Revert(context);
        document.Comments[5].Replies[0].PlainText.Should().Be("Reply six");
        document.Comments[5].Replies[0].Content[0].StyleId.Should().BeNull();

        command.Apply(context);
        document.Comments[5].Replies[0].PlainText.Should().Be("Answer six");
    }

    [Fact]
    public void EditStoryParagraphCommand_KeepsATextBoxHostRunMirrorCurrent()
    {
        var document = BuildDocument();
        var context = new Context(document);
        var hostRun = ((Paragraph)document.Blocks[1]).Runs[1];
        var command = new EditStoryParagraphCommand(
            new StoryParagraphAddress(DocumentStoryKind.TextBox, 0),
            paragraph => RevisionEditPlanner.TryReplacePlainTextRange(paragraph, 0, 3, "Crate"));

        command.Apply(context);
        hostRun.Shape!.PlainText.Should().Be("Crate text");
        hostRun.Text.Should().Be("Crate text");

        command.Revert(context);
        hostRun.Text.Should().Be("Box text");
    }

    [Fact]
    public void EditStoryParagraphCommand_OnAMissingAddress_DoesNothing()
    {
        var document = BuildDocument();
        var context = new Context(document);
        var called = false;
        var command = new EditStoryParagraphCommand(
            new StoryParagraphAddress(DocumentStoryKind.Footnote, 40),
            _ => called = true);

        command.HasEffect(context).Should().BeFalse("the bus must not push an undo entry for it");
        command.Apply(context);
        command.Revert(context);

        called.Should().BeFalse();
        new EditStoryParagraphCommand(new StoryParagraphAddress(DocumentStoryKind.Footnote, 0), _ => { })
            .HasEffect(context).Should().BeTrue();
    }
}
