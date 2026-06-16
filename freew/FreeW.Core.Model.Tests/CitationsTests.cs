namespace FreeW.Core.Model.Tests;

public class CitationsTests
{
    [Fact]
    public void FormatInText_AuthorAndYear_ProducesAuthorCommaYear()
    {
        var source = new Source { Author = "Knuth", Year = "1997" };

        Citations.FormatInText(source).Should().Be("(Knuth, 1997)");
    }

    [Fact]
    public void FormatInText_MissingYear_OmitsYearGracefully()
    {
        var source = new Source { Author = "Knuth" };

        Citations.FormatInText(source).Should().Be("(Knuth)");
    }

    [Fact]
    public void FormatInText_MissingAuthor_FallsBackToYear()
    {
        var source = new Source { Year = "1997" };

        Citations.FormatInText(source).Should().Be("(1997)");
    }

    [Fact]
    public void FormatInText_NoAuthorOrYear_FallsBackToTagThenUnknown()
    {
        Citations.FormatInText(new Source { Tag = "Anon42" }).Should().Be("(Anon42)");
        Citations.FormatInText(new Source()).Should().Be("(Unknown)");
    }

    [Fact]
    public void FormatBibliographyEntry_AllFields_ProducesAuthorYearTitlePublisher()
    {
        var source = new Source
        {
            Author = "Knuth, D.",
            Year = "1997",
            Title = "The Art of Computer Programming",
            Publisher = "Addison-Wesley"
        };

        Citations.FormatBibliographyEntry(source)
            .Should().Be("Knuth, D. (1997). The Art of Computer Programming. Addison-Wesley.");
    }

    [Fact]
    public void FormatBibliographyEntry_MissingPublisher_OmitsThatSegment()
    {
        var source = new Source { Author = "Knuth, D.", Year = "1997", Title = "TAOCP" };

        Citations.FormatBibliographyEntry(source).Should().Be("Knuth, D. (1997). TAOCP.");
    }

    [Fact]
    public void FormatBibliographyEntry_OnlyTitle_ProducesJustTitle()
    {
        var source = new Source { Title = "Untitled Manuscript" };

        Citations.FormatBibliographyEntry(source).Should().Be("Untitled Manuscript.");
    }

    [Fact]
    public void FormatBibliographyEntry_NoFields_ProducesEmptyString()
    {
        Citations.FormatBibliographyEntry(new Source()).Should().BeEmpty();
    }

    [Fact]
    public void BuildBibliography_NoSources_YieldsOnlyTheHeadingParagraph()
    {
        var doc = new TextDocument();

        var bibliography = Citations.BuildBibliography(doc);

        bibliography.Should().ContainSingle();
        bibliography[0].PlainText.Should().Be(Citations.HeadingText);
        bibliography[0].StyleId.Should().Be(Citations.HeadingStyleId);
    }

    [Fact]
    public void BuildBibliography_SortsEntriesByAuthorAndStylesThem()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));
        doc.Sources.Add(new Source { Tag = "Z", Author = "Zimmerman", Year = "2001", Title = "Zed" });
        doc.Sources.Add(new Source { Tag = "A", Author = "Adams", Year = "1979", Title = "Guide" });
        doc.Sources.Add(new Source { Tag = "M", Author = "Knuth", Year = "1997", Title = "TAOCP" });

        var bibliography = Citations.BuildBibliography(doc);

        // Heading first, then one entry per source sorted by author.
        bibliography.Select(p => p.PlainText).Should().Equal(
            Citations.HeadingText,
            "Adams. (1979). Guide.",
            "Knuth. (1997). TAOCP.",
            "Zimmerman. (2001). Zed.");

        bibliography[0].StyleId.Should().Be(Citations.HeadingStyleId);
        bibliography.Skip(1).Should().OnlyContain(p => p.StyleId == Citations.EntryStyleId);
    }

    [Fact]
    public void BuildBibliography_HandlesSourcesWithMissingFieldsGracefully()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph());
        doc.Sources.Add(new Source { Author = "Brown", Title = "No Year Work" });
        doc.Sources.Add(new Source { Title = "Anonymous Work" }); // no author -> sorts first (empty author)

        var bibliography = Citations.BuildBibliography(doc);

        bibliography.Select(p => p.PlainText).Should().Equal(
            Citations.HeadingText,
            "Anonymous Work.",
            "Brown. No Year Work.");
    }

    [Fact]
    public void BuildBibliography_DoesNotMutateTheDocument()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Sources.Add(new Source { Author = "Adams", Year = "1979", Title = "Guide" });

        Citations.BuildBibliography(doc);

        doc.Blocks.Should().ContainSingle();
        doc.Sources.Should().ContainSingle();
    }

    [Fact]
    public void IsBibliographyParagraph_RecognisesOnlyBibliographyStyledParagraphs()
    {
        var doc = new TextDocument();
        doc.Sources.Add(new Source { Author = "Adams", Year = "1979", Title = "Guide" });
        var bibliography = Citations.BuildBibliography(doc);

        Citations.IsBibliographyParagraph(bibliography[0]).Should().BeTrue();
        Citations.IsBibliographyParagraph(bibliography[1]).Should().BeTrue();
        Citations.IsBibliographyParagraph(new Paragraph("Body")).Should().BeFalse();
    }

    [Fact]
    public void EnsureStyles_RegistersBibliographyStylesIdempotently()
    {
        var doc = new TextDocument();

        Citations.EnsureStyles(doc);
        Citations.EnsureStyles(doc); // second call must not throw or duplicate

        doc.Styles.Should().ContainKey(Citations.HeadingStyleId);
        doc.Styles.Should().ContainKey(Citations.EntryStyleId);
    }
}
