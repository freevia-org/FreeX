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
