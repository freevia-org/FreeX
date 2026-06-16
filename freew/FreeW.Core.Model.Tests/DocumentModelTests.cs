namespace FreeW.Core.Model.Tests;

public class DocumentModelTests
{
    [Fact]
    public void CreateEmpty_HasBuiltInStylesAndOneParagraph()
    {
        var doc = TextDocument.CreateEmpty();

        doc.Paragraphs.Should().HaveCount(1);
        doc.Styles.Keys.Should().Contain(new[] { "Normal", "Heading1", "Title" });
        doc.DefaultRun.FontFamily.Should().Be("Calibri");
        doc.DefaultRun.FontSizePt.Should().Be(11);
    }

    [Fact]
    public void PlainText_JoinsParagraphsWithNewlines()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Hello"));
        doc.Blocks.Add(new Paragraph("World"));

        doc.PlainText.Should().Be("Hello\nWorld");
    }

    [Fact]
    public void Paragraph_PlainText_ConcatenatesRuns()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Free"));
        paragraph.Runs.Add(new Run("W"));

        paragraph.PlainText.Should().Be("FreeW");
    }

    [Fact]
    public void Run_CarriesOptionalHyperlinkUrl()
    {
        var plain = new Run("plain");
        var linked = new Run("linked") { HyperlinkUrl = "https://example.com" };

        plain.HyperlinkUrl.Should().BeNull();
        linked.HyperlinkUrl.Should().Be("https://example.com");
        linked.Text.Should().Be("linked");
    }

    [Fact]
    public void Heading1Style_CarriesBoldColouredFormatting()
    {
        var style = TextDocument.CreateEmpty().Styles["Heading1"];

        style.Run.Bold.Should().BeTrue();
        style.Run.ColorHex.Should().Be("#2F5496");
        style.BasedOnStyleId.Should().Be("Normal");
    }
}
