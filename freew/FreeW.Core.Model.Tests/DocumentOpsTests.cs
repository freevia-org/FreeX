namespace FreeW.Core.Model.Tests;

public class DocumentOpsTests
{
    [Fact]
    public void InsertCoverPage_PrependsTitleAndSubtitle_FromProperties()
    {
        var doc = new TextDocument();
        doc.Properties.Title = "Annual Report";
        doc.Properties.Author = "Ada Lovelace";
        doc.Blocks.Add(new Paragraph("Body"));

        DocumentOps.InsertCoverPage(doc);

        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        paragraphs[0].StyleId.Should().Be("Title");
        paragraphs[0].PlainText.Should().Be("Annual Report");
        paragraphs[1].StyleId.Should().Be("Subtitle");
        paragraphs[1].PlainText.Should().Be("Ada Lovelace");
        // The spacer, then the original body, follow.
        paragraphs[2].PlainText.Should().BeEmpty();
        paragraphs.Last().PlainText.Should().Be("Body");
    }

    [Fact]
    public void BuildCoverPage_UsesPlaceholderTitle_AndOmitsSubtitle_WhenPropertiesUnset()
    {
        var doc = new TextDocument();

        var blocks = DocumentOps.BuildCoverPage(doc).OfType<Paragraph>().ToList();

        // No author -> no subtitle paragraph: just the placeholder Title and a spacer.
        blocks.Should().HaveCount(2);
        blocks[0].StyleId.Should().Be("Title");
        blocks[0].PlainText.Should().Be(DocumentOps.DefaultCoverTitle);
        blocks[1].PlainText.Should().BeEmpty();
        blocks.Any(p => p.StyleId == "Subtitle").Should().BeFalse();
    }

    [Fact]
    public void CreatePageBreak_SetsPageBreakBeforeFlag()
    {
        var paragraph = DocumentOps.CreatePageBreak();

        paragraph.Formatting.PageBreakBefore.Should().BeTrue();
        paragraph.PlainText.Should().BeEmpty();
    }

    [Fact]
    public void BuildBlankPage_CreatesTwoPageBreakParagraphs()
    {
        var blocks = DocumentOps.BuildBlankPage().OfType<Paragraph>().ToList();

        blocks.Should().HaveCount(2);
        blocks.Should().OnlyContain(p => p.Formatting.PageBreakBefore);
        blocks.Should().OnlyContain(p => p.PlainText.Length == 0);
    }

    [Fact]
    public void CreateHorizontalRule_SetsBottomOnlyBorder()
    {
        var paragraph = DocumentOps.CreateHorizontalRule();

        paragraph.Formatting.Border.Should().NotBeNull();
        paragraph.Formatting.Border!.BottomOnly.Should().BeTrue();
        paragraph.PlainText.Should().BeEmpty();
    }

    [Fact]
    public void ParagraphBorder_DefaultsToFullBox_NonBreakingForExistingCallers()
    {
        // Existing callers that omit BottomOnly keep a full box (the historical behaviour).
        var border = new ParagraphBorder("#FF0000", 1.5);

        border.BottomOnly.Should().BeFalse();
    }
}
