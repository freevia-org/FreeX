namespace FreeW.Core.Model.Tests;

public sealed class TextDocumentStoryTraversalTests
{
    [Fact]
    public void EnumerateParagraphs_UsesBodyHeaderFooterNoteAndCallerSuppliedCommentOrder()
    {
        var document = new TextDocument();
        var body = new Paragraph("body");
        body.SectionBreak = new Section(new PageSettings())
        {
            HeadersFooters = CreateHeadersFooters("section"),
        };
        document.Blocks.Add(body);

        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("cell 1");
        table.Rows[0].Cells[1].Paragraphs[0] = new Paragraph("cell 2");
        document.Blocks.Add(table);
        document.FinalSectionHeadersFooters.Header = new HeaderFooter("final header");
        document.FinalSectionHeadersFooters.Footer = new HeaderFooter("final footer");
        document.Footnotes[9] = new Footnote(9, "footnote");
        document.Endnotes[4] = new Endnote(4, "endnote");
        var comments = new[] { new Paragraph("comment 2"), new Paragraph("comment 1") };

        var paragraphs = TextDocumentStoryTraversal.EnumerateParagraphs(document, comments).ToList();

        paragraphs.Select(paragraph => paragraph.PlainText).Should().Equal(
            "body",
            "cell 1",
            "cell 2",
            "section header",
            "section footer",
            "section even header",
            "section even footer",
            "section first header",
            "section first footer",
            "final header",
            "final footer",
            "footnote",
            "endnote",
            "comment 2",
            "comment 1");
    }

    [Fact]
    public void EnumerateParagraphs_StorySubsetIncludesOnlyRequestedStoriesInCanonicalOrder()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("body"));
        document.FinalSectionHeadersFooters.Header = new HeaderFooter("header");
        document.FinalSectionHeadersFooters.Footer = new HeaderFooter("footer");
        document.Footnotes[2] = new Footnote(2, "footnote");
        document.Endnotes[3] = new Endnote(3, "endnote");

        var paragraphs = TextDocumentStoryTraversal.EnumerateParagraphs(
                document,
                TextDocumentStorySubset.HeadersFooters | TextDocumentStorySubset.Endnotes,
                TextDocumentStoryTraversalOptions.PreserveDuplicateParagraphs)
            .ToList();

        paragraphs.Select(paragraph => paragraph.PlainText).Should().Equal("header", "footer", "endnote");
    }

    [Fact]
    public void EnumerateParagraphs_TraversalOptionsKeepNestedAndTextBoxReachIndependent()
    {
        var document = new TextDocument();
        var body = new Paragraph("body");
        body.Runs.Add(new Run(string.Empty)
        {
            Shape = new Shape { TextParagraphs = { new Paragraph("shape") } },
        });
        var group = new DrawingGroup();
        group.Children.Add(new Shape { TextParagraphs = { new Paragraph("group") } });
        body.Runs.Add(new Run(string.Empty) { DrawingGroup = group });
        document.Blocks.Add(body);

        var outer = Table.Create(1, 1);
        outer.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("cell");
        var nested = Table.Create(1, 1);
        nested.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("nested");
        outer.Rows[0].Cells[0].NestedTables.Add(nested);
        document.Blocks.Add(outer);

        TextDocumentStoryTraversal.EnumerateParagraphs(
                document,
                TextDocumentStorySubset.Body,
                TextDocumentStoryTraversalOptions.IncludeShapeTextBoxes
                | TextDocumentStoryTraversalOptions.PreserveDuplicateParagraphs)
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("body", "shape", "cell");

        TextDocumentStoryTraversal.EnumerateParagraphs(
                document,
                TextDocumentStorySubset.Body,
                TextDocumentStoryTraversalOptions.IncludeTextBoxes
                | TextDocumentStoryTraversalOptions.IncludeNestedTables
                | TextDocumentStoryTraversalOptions.PreserveDuplicateParagraphs)
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("body", "shape", "group", "cell", "nested");
    }

    [Fact]
    public void EnumerateParagraphs_SubsetTraversalIsDeferredAndStopsBeforeLaterStories()
    {
        var document = new TextDocument();
        var body = new Paragraph("body");
        document.Blocks.Add(body);
        document.Footnotes[1] = null!;

        var traversal = TextDocumentStoryTraversal.EnumerateParagraphs(
            document,
            TextDocumentStorySubset.All,
            TextDocumentStoryTraversalOptions.PreserveDuplicateParagraphs);
        document.Blocks.Insert(0, new Paragraph("inserted"));

        traversal.Take(1).Should().Equal((Paragraph)document.Blocks[0]);
    }

    [Fact]
    public void EnumerateParagraphs_IncludeTextBoxes_UsesWriterExpansionOrder()
    {
        var document = new TextDocument();
        var body = new Paragraph("body");
        var directTextBox = new Paragraph("direct text box");
        var nestedTextBox = new Paragraph("nested text box");
        directTextBox.Runs.Add(new Run(string.Empty)
        {
            Shape = new Shape { TextParagraphs = { nestedTextBox } },
        });
        body.Runs.Add(new Run(string.Empty)
        {
            Shape = new Shape { TextParagraphs = { directTextBox } },
        });

        var groupedTextBox = new Paragraph("grouped text box");
        var group = new DrawingGroup();
        group.Children.Add(new Shape { TextParagraphs = { groupedTextBox } });
        body.Runs.Add(new Run(string.Empty) { DrawingGroup = group });
        document.Blocks.Add(body);

        var paragraphs = TextDocumentStoryTraversal.EnumerateParagraphs(
                document,
                [],
                TextDocumentStoryTraversalOptions.IncludeTextBoxes)
            .ToList();

        paragraphs.Select(paragraph => paragraph.PlainText).Should().Equal(
            "body", "direct text box", "nested text box", "grouped text box");
    }

    [Fact]
    public void EnumerateParagraphs_PreservesOrRemovesDuplicateReferencesAsRequested()
    {
        var document = new TextDocument();
        var shared = new Paragraph("shared");
        document.Blocks.Add(shared);
        var header = new HeaderFooter();
        header.Paragraphs.Add(shared);
        document.Header = header;

        TextDocumentStoryTraversal.EnumerateParagraphs(document, [shared])
            .Should().Equal(shared);
        TextDocumentStoryTraversal.EnumerateParagraphs(
                document,
                [shared],
                TextDocumentStoryTraversalOptions.PreserveDuplicateParagraphs)
            .Should().Equal(shared, shared, shared);
    }

    [Fact]
    public void EnumerateParagraphs_ReturnsMutableModelReferences()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph("before");
        document.Blocks.Add(paragraph);

        var returned = TextDocumentStoryTraversal.EnumerateParagraphs(document, []).Single();
        returned.Runs[0].Text = "after";

        paragraph.PlainText.Should().Be("after");
    }

    [Fact]
    public void EnumerateParagraphs_EmptyDocumentIsEmpty()
    {
        TextDocumentStoryTraversal.EnumerateParagraphs(new TextDocument(), [])
            .Should().BeEmpty();
    }

    [Fact]
    public void EnumerateParagraphs_NullInputsThrowWhenEnumerated()
    {
        var nullDocument = () => TextDocumentStoryTraversal
            .EnumerateParagraphs(null!, [])
            .ToList();
        var nullComments = () => TextDocumentStoryTraversal
            .EnumerateParagraphs(new TextDocument(), null!)
            .ToList();

        nullDocument.Should().Throw<ArgumentNullException>();
        nullComments.Should().Throw<ArgumentNullException>();
    }

    private static SectionHeadersFooters CreateHeadersFooters(string prefix) => new()
    {
        Header = new HeaderFooter($"{prefix} header"),
        Footer = new HeaderFooter($"{prefix} footer"),
        EvenHeader = new HeaderFooter($"{prefix} even header"),
        EvenFooter = new HeaderFooter($"{prefix} even footer"),
        FirstHeader = new HeaderFooter($"{prefix} first header"),
        FirstFooter = new HeaderFooter($"{prefix} first footer"),
    };
}
