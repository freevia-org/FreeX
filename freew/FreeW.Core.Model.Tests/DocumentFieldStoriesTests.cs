namespace FreeW.Core.Model.Tests;

public sealed class DocumentFieldStoriesTests
{
    [Fact]
    public void Enumerate_CoversEveryModelledStoryAndDeduplicatesSharedHeaders()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var body = new Paragraph("body");
        var textBox = new Paragraph("text box");
        body.Runs.Add(new Run(string.Empty)
        {
            Shape = new Shape { TextParagraphs = { textBox } },
        });
        var sharedHeader = new HeaderFooter("header");
        body.SectionBreak = new Section(new PageSettings())
        {
            HeadersFooters = new SectionHeadersFooters { Header = sharedHeader },
        };
        document.Blocks.Add(body);
        document.Header = sharedHeader;
        document.Footer = new HeaderFooter("footer");
        document.Footnotes[2] = new Footnote(2, "footnote");
        document.Endnotes[3] = new Endnote(3, "endnote");
        var comment = new Comment(4, "comment");
        comment.AddReply(5, "reply");
        document.Comments[4] = comment;

        var stories = DocumentFieldStories.Enumerate(document).ToList();

        stories.Select(item => item.Paragraph.PlainText).Should().Equal(
            "body", "text box", "header", "footer", "footnote", "endnote", "comment", "reply");
        stories.Select(item => item.StoryKind).Should().Equal(
            DocumentFieldStoryKind.MainDocument,
            DocumentFieldStoryKind.TextBox,
            DocumentFieldStoryKind.HeaderFooter,
            DocumentFieldStoryKind.HeaderFooter,
            DocumentFieldStoryKind.Footnote,
            DocumentFieldStoryKind.Endnote,
            DocumentFieldStoryKind.Comment,
            DocumentFieldStoryKind.Comment);
        stories[0].BodyBlockIndex.Should().Be(0);
        stories[1].BodyBlockIndex.Should().Be(0);
        stories.Skip(2).Should().OnlyContain(item => item.BodyBlockIndex == -1);
    }

    [Fact]
    public void Enumerate_MainStoryRecursesThroughNestedTablesInSerializedOrder()
    {
        var document = new TextDocument();
        var outer = Table.Create(1, 1);
        var nested = Table.Create(1, 1);
        nested.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("nested");
        outer.Rows[0].Cells[0].NestedTables.Add(nested);
        outer.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("outer");
        document.Blocks.Add(outer);

        var stories = DocumentFieldStories.Enumerate(document).ToList();

        stories.Select(item => item.Paragraph.PlainText).Should().Equal("nested", "outer");
        stories.Should().OnlyContain(item =>
            item.StoryKind == DocumentFieldStoryKind.MainDocument && item.BodyBlockIndex == 0);
    }

    [Fact]
    public void Enumerate_TextBoxAnchoredInsideTableRow_CarriesTheAnchorsRowIndex()
    {
        var document = new TextDocument();
        var table = Table.Create(3, 1);
        var anchor = new Paragraph("anchor row 2");
        var textBox = new Paragraph("box text");
        anchor.Runs.Add(new Run(string.Empty) { Shape = new Shape { TextParagraphs = { textBox } } });
        table.Rows[2].Cells[0].Paragraphs[0] = anchor;
        document.Blocks.Add(table);

        var stories = DocumentFieldStories.Enumerate(document).ToList();

        var anchorStory = stories.Single(item => item.Paragraph.PlainText == "anchor row 2");
        anchorStory.StoryKind.Should().Be(DocumentFieldStoryKind.MainDocument);
        anchorStory.TableRowIndex.Should().Be(2);

        var textBoxStory = stories.Single(item => item.Paragraph.PlainText == "box text");
        textBoxStory.StoryKind.Should().Be(DocumentFieldStoryKind.TextBox);
        textBoxStory.BodyBlockIndex.Should().Be(0);
        textBoxStory.TableRowIndex.Should().Be(2);
    }

    // Sibling no-regression: a text box anchored to a non-table paragraph still carries no row.
    [Fact]
    public void Enumerate_TextBoxAnchoredOutsideAnyTable_HasNoRowIndex()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var anchor = new Paragraph("anchor");
        var textBox = new Paragraph("box text");
        anchor.Runs.Add(new Run(string.Empty) { Shape = new Shape { TextParagraphs = { textBox } } });
        document.Blocks.Add(anchor);

        var stories = DocumentFieldStories.Enumerate(document).ToList();

        stories.Should().OnlyContain(item => item.TableRowIndex == null);
    }

    [Theory]
    [InlineData("DOCPROPERTY", true)]
    [InlineData("REF", true)]
    [InlineData("SEQ", false)]
    [InlineData("STYLEREF", false)]
    [InlineData("CITATION", false)]
    public void CanRecomputeComplexField_NonMainStoryRejectsPositionSensitiveFamilies(
        string keyword,
        bool expected)
    {
        var field = new ComplexField(keyword);

        DocumentFieldStories.CanRecomputeComplexField(DocumentFieldStoryKind.HeaderFooter, field)
            .Should().Be(expected);
        DocumentFieldStories.CanRecomputeComplexField(DocumentFieldStoryKind.MainDocument, field)
            .Should().Be(ComplexFieldEngine.CanRecompute(field));
    }
}
