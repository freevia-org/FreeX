namespace FreeW.Core.Model.Tests;

public sealed class CitationStyleCommandTests
{
    [Fact]
    public void ApplyCitationStyle_RefreshesCitationStoriesAndExistingBibliography_WithUndoRedo()
    {
        var source = new Source
        {
            Tag = "Sm24",
            Author = "Smith",
            Title = "A Work",
            Year = "2024",
            Publisher = "Press"
        };
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Sources.Add(source);
        var bodyCitation = Run.ComplexFieldRun(" CITATION Sm24 ", "(Smith, 2024)");
        document.Blocks.Add(new Paragraph { Runs = { bodyCitation } });
        var headerCitation = Run.ComplexFieldRun(" CITATION Sm24 ", "(Smith, 2024)");
        document.Header = new HeaderFooter { Paragraphs = { new Paragraph { Runs = { headerCitation } } } };
        var bibliographyControl = BlockContentControl.BibliographyRegion();
        foreach (var paragraph in Citations.BuildBibliography(document, CitationStyle.Apa))
        {
            paragraph.BlockContentControl = bibliographyControl;
            document.Blocks.Add(paragraph);
        }
        var originalBibliography = document.Blocks.Where(Citations.IsBibliographyParagraph).ToArray();
        var bus = new DocumentCommandBus(new TestContext(document));

        bus.Execute(new ApplyCitationStyleCommand(CitationStyle.Ieee));

        document.BibliographyStyle.Should().Be(CitationStyle.Ieee);
        bodyCitation.Text.Should().Be("[1]");
        headerCitation.Text.Should().Be("[1]");
        document.Blocks.Where(Citations.IsBibliographyParagraph)
            .Select(block => ((Paragraph)block).PlainText)
            .Should().Equal("References", "[1] Smith, \"A Work,\" Press, 2024.");

        bus.Undo().Should().BeTrue();

        document.BibliographyStyle.Should().Be(CitationStyle.Apa);
        bodyCitation.Text.Should().Be("(Smith, 2024)");
        headerCitation.Text.Should().Be("(Smith, 2024)");
        document.Blocks.Where(Citations.IsBibliographyParagraph)
            .Should().Equal(originalBibliography);

        bus.Redo().Should().BeTrue();
        document.BibliographyStyle.Should().Be(CitationStyle.Ieee);
        bodyCitation.Text.Should().Be("[1]");
        headerCitation.Text.Should().Be("[1]");
    }

    [Fact]
    public void ApplyCitationStyle_DoesNotInsertMissingBibliography()
    {
        var document = TextDocument.CreateEmpty();
        var bus = new DocumentCommandBus(new TestContext(document));

        bus.Execute(new ApplyCitationStyleCommand(CitationStyle.Mla));

        document.BibliographyStyle.Should().Be(CitationStyle.Mla);
        document.Blocks.Any(Citations.IsBibliographyParagraph).Should().BeFalse();
    }

    private sealed class TestContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
