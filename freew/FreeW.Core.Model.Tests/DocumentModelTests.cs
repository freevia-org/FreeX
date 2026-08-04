namespace FreeW.Core.Model.Tests;

public class DocumentModelTests
{
    [Fact]
    public void CreateEmpty_HasBuiltInStylesAndOneParagraph()
    {
        var doc = TextDocument.CreateEmpty();

        doc.Paragraphs.Should().HaveCount(1);
        doc.Styles.Keys.Should().Contain(new[]
        {
            "Normal", "Heading1", "Heading2", "Heading3", "Title", "Subtitle", "Quote"
        });
        doc.DefaultRun.FontFamily.Should().Be("Calibri");
        doc.DefaultRun.FontSizePt.Should().Be(11);
    }

    [Fact]
    public void BuiltInStyles_HaveSensibleFormattingBasedOnNormal()
    {
        var styles = TextDocument.CreateEmpty().Styles;

        styles["Heading2"].Name.Should().Be("Heading 2");
        styles["Heading2"].Run.Bold.Should().BeTrue();
        styles["Heading2"].BasedOnStyleId.Should().Be("Normal");

        styles["Heading3"].Name.Should().Be("Heading 3");
        styles["Heading3"].Run.Bold.Should().BeTrue();
        styles["Heading3"].BasedOnStyleId.Should().Be("Normal");

        styles["Subtitle"].Name.Should().Be("Subtitle");
        styles["Subtitle"].Run.Italic.Should().BeTrue();
        styles["Subtitle"].BasedOnStyleId.Should().Be("Normal");

        styles["Quote"].Name.Should().Be("Quote");
        styles["Quote"].Run.Italic.Should().BeTrue();
        styles["Quote"].Paragraph.IndentLeftPt.Should().BeGreaterThan(0);
        styles["Quote"].BasedOnStyleId.Should().Be("Normal");
    }

    [Fact]
    public void SetParagraphStyleCommand_SetsStyleId_AndUndoRestoresPrevious()
    {
        var doc = TextDocument.CreateEmpty();
        var paragraph = new Paragraph("Heading text") { StyleId = "Normal" };
        doc.Blocks.Add(paragraph);
        var index = doc.Blocks.IndexOf(paragraph);
        var bus = new DocumentCommandBus(new DocContext(doc));

        bus.Execute(new SetParagraphStyleCommand(index, "Heading2"));
        paragraph.StyleId.Should().Be("Heading2");

        bus.Undo();
        paragraph.StyleId.Should().Be("Normal");

        bus.Redo();
        paragraph.StyleId.Should().Be("Heading2");
    }

    [Fact]
    public void DesignCatalogCommand_Restores_defaults_theme_and_affected_styles()
    {
        var document = TextDocument.CreateEmpty();
        var context = new DocContext(document);
        var bus = new DocumentCommandBus(context);
        var defaultRun = document.DefaultRun;
        var defaultParagraph = document.DefaultParagraph;
        var theme = document.Theme;
        var styleSnapshots = document.Styles.ToDictionary(
            entry => entry.Key,
            entry => (entry.Value.Run, entry.Value.Paragraph));

        bus.Execute(new DesignCatalogCommand(
            "Elegant Style Set",
            doc => DocumentStyleSet.Apply(doc, DocumentStyleSet.FindByName("Elegant")!)));
        document.DefaultRun.FontFamily.Should().Be("Georgia");
        document.Styles["Heading1"].Run.FontFamily.Should().Be("Cambria");

        bus.Undo();
        document.DefaultRun.Should().Be(defaultRun);
        document.DefaultParagraph.Should().Be(defaultParagraph);
        document.Theme.Should().Be(theme);
        foreach (var (styleId, snapshot) in styleSnapshots)
        {
            document.Styles[styleId].Run.Should().Be(snapshot.Run);
            document.Styles[styleId].Paragraph.Should().Be(snapshot.Paragraph);
        }

        bus.Redo();
        document.DefaultRun.FontFamily.Should().Be("Georgia");
        document.Styles["Heading1"].Run.FontFamily.Should().Be("Cambria");
    }

    private sealed class DocContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
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
