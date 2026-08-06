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

    [Theory]
    [InlineData(CitationStyle.Apa, "(Smith, 2020)")]
    [InlineData(CitationStyle.Harvard, "(Smith, 2020)")]
    [InlineData(CitationStyle.Gost, "(Smith, 2020)")]
    [InlineData(CitationStyle.Iso690, "(Smith, 2020)")]
    public void FormatInText_LastFirstPersonalAuthor_UsesFamilyNameForAuthorDateStyles(
        CitationStyle style,
        string expected)
    {
        var source = new Source { Author = "Smith, John", Year = "2020" };

        Citations.FormatInText(source, style).Should().Be(expected);
    }

    [Theory]
    [InlineData(CitationStyle.Chicago)]
    [InlineData(CitationStyle.Turabian)]
    public void FormatInText_LastFirstPersonalAuthor_UsesFamilyNameForChicagoLikeStyles(CitationStyle style)
    {
        var source = new Source { Author = "Smith, John", Year = "2020" };

        Citations.FormatInText(source, style).Should().Be("(Smith 2020)");
    }

    [Fact]
    public void FormatInText_FirstMiddleLastPersonalAuthor_UsesFamilyName()
    {
        var source = new Source { Author = "Jane Q. Doe", Year = "2020" };

        Citations.FormatInText(source, CitationStyle.Apa).Should().Be("(Doe, 2020)");
    }

    [Fact]
    public void FormatInText_TwoPersonalAuthors_UsesFamilyNamesJoinedByAmpersand()
    {
        var source = new Source { Author = "Jane Q. Doe; Alex Smith", Year = "2020" };

        Citations.FormatInText(source, CitationStyle.Apa).Should().Be("(Doe & Smith, 2020)");
        Citations.FormatInText(source, CitationStyle.Mla).Should().Be("(Doe & Smith)");
    }

    [Fact]
    public void FormatInText_StructuredPersonalAuthors_KeepFlatAuthorCompatibility()
    {
        var source = new Source
        {
            Author = "Jane Q. Doe; Alex Smith",
            PersonalAuthors =
            [
                SourceAuthorPerson.Create("Jane", "Q.", "Doe"),
                SourceAuthorPerson.Create("Alex", string.Empty, "Smith")
            ],
            Year = "2020"
        };

        Citations.FormatInText(source, CitationStyle.Apa).Should().Be("(Doe & Smith, 2020)");
    }

    [Fact]
    public void FormatInText_ThreePersonalAuthors_UsesFirstFamilyNameEtAl()
    {
        var source = new Source { Author = "Jane Q. Doe; Alex Smith; Priya Patel", Year = "2020" };

        Citations.FormatInText(source, CitationStyle.Apa).Should().Be("(Doe et al., 2020)");
        Citations.FormatInText(source, CitationStyle.Mla).Should().Be("(Doe et al.)");
    }

    [Theory]
    [InlineData(CitationStyle.Ieee)]
    [InlineData(CitationStyle.Vancouver)]
    public void FormatInText_NumericStylesWithoutPosition_BracketDisplayAuthor(CitationStyle style)
    {
        var source = new Source { Author = "Jane Q. Doe", Year = "2020" };

        Citations.FormatInText(source, style).Should().Be("[Doe]");
    }

    [Fact]
    public void FormatInText_CorporateAuthor_RemainsUnchanged()
    {
        var source = new Source { Author = "World Health Organization", Year = "2020" };

        Citations.FormatInText(source, CitationStyle.Apa).Should().Be("(World Health Organization, 2020)");
    }

    [Fact]
    public void FormatInText_AmbiguousAcronymAuthors_RemainUnchanged()
    {
        var source = new Source { Author = "NASA; ESA", Year = "2020" };

        Citations.FormatInText(source, CitationStyle.Apa).Should().Be("(NASA; ESA, 2020)");
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
    public void BuildBibliography_NoSources_YieldsNativeEmptyResultAfterHeading()
    {
        var doc = new TextDocument();

        var bibliography = Citations.BuildBibliography(doc);

        bibliography.Should().HaveCount(2);
        bibliography[0].PlainText.Should().Be(Citations.HeadingText);
        bibliography[0].StyleId.Should().Be(Citations.HeadingStyleId);
        bibliography[0].SpanningFieldOwner.Should().BeNull();
        bibliography[1].PlainText.Should().Be(Citations.EmptyResultText);
        bibliography[1].StyleId.Should().Be(Citations.EntryStyleId);
        bibliography[1].Runs.Should().ContainSingle();
        bibliography[1].Runs[0].ComplexField!.Instruction.Should().Be(Citations.NativeFieldInstruction);
        bibliography[1].SpanningFieldStart.Should().BeNull();
        bibliography[1].EndsSpanningField.Should().BeFalse();
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
        bibliography[0].SpanningFieldOwner.Should().BeNull();
        bibliography.Skip(1).Should().OnlyContain(paragraph =>
            ReferenceEquals(paragraph.SpanningFieldOwner, bibliography[1].SpanningFieldStart));
        bibliography[1].SpanningFieldStart!.Instruction.Should().Be(Citations.NativeFieldInstruction);
        bibliography[1].EndsSpanningField.Should().BeFalse();
        bibliography[^1].EndsSpanningField.Should().BeTrue();
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

    // --- Style-aware formatting (APA / MLA / Chicago) -------------------------------------------------

    [Fact]
    public void FormatInText_DefaultOverload_MatchesApaBehaviour()
    {
        var source = new Source { Author = "Knuth", Year = "1997" };

        // The no-style overload must keep producing the original APA author–year form.
        Citations.FormatInText(source).Should().Be("(Knuth, 1997)");
        Citations.FormatInText(source).Should().Be(Citations.FormatInText(source, CitationStyle.Apa));
    }

    [Fact]
    public void FormatInText_PerStyle_ProducesDistinctDocumentedForms()
    {
        var source = new Source { Author = "Knuth", Year = "1997" };

        // APA: (Author, Year) — comma between author and year.
        Citations.FormatInText(source, CitationStyle.Apa).Should().Be("(Knuth, 1997)");
        // MLA: author–page, but FreeW has no page, so (Author) only.
        Citations.FormatInText(source, CitationStyle.Mla).Should().Be("(Knuth)");
        // Chicago (author–date): (Author Year) — space between author and year.
        Citations.FormatInText(source, CitationStyle.Chicago).Should().Be("(Knuth 1997)");
    }

    [Fact]
    public void FormatInText_Mla_NoAuthor_FallsBackToYearThenTag()
    {
        Citations.FormatInText(new Source { Year = "1997" }, CitationStyle.Mla).Should().Be("(1997)");
        Citations.FormatInText(new Source { Tag = "Anon42" }, CitationStyle.Mla).Should().Be("(Anon42)");
        Citations.FormatInText(new Source(), CitationStyle.Mla).Should().Be("(Unknown)");
    }

    [Fact]
    public void FormatBibliographyEntry_DefaultOverload_MatchesApaBehaviour()
    {
        var source = new Source
        {
            Author = "Knuth, D.",
            Year = "1997",
            Title = "The Art of Computer Programming",
            Publisher = "Addison-Wesley"
        };

        Citations.FormatBibliographyEntry(source)
            .Should().Be(Citations.FormatBibliographyEntry(source, CitationStyle.Apa));
    }

    [Fact]
    public void FormatBibliographyEntry_PerStyle_ProducesDistinctDocumentedForms()
    {
        var source = new Source
        {
            Author = "Knuth, D.",
            Year = "1997",
            Title = "The Art of Computer Programming",
            Publisher = "Addison-Wesley"
        };

        // APA: Author. (Year). Title. Publisher.
        Citations.FormatBibliographyEntry(source, CitationStyle.Apa)
            .Should().Be("Knuth, D. (1997). The Art of Computer Programming. Addison-Wesley.");
        // MLA: Author. Title. Publisher, Year.
        Citations.FormatBibliographyEntry(source, CitationStyle.Mla)
            .Should().Be("Knuth, D. The Art of Computer Programming. Addison-Wesley, 1997.");
        // Chicago: Author. Title. Publisher, Year. (same ordering as MLA).
        Citations.FormatBibliographyEntry(source, CitationStyle.Chicago)
            .Should().Be("Knuth, D. The Art of Computer Programming. Addison-Wesley, 1997.");
    }

    [Fact]
    public void FormatBibliographyEntry_AuthorTitlePublisherYear_OmitsEmptySegmentsCleanly()
    {
        // Year present, publisher missing -> "... Year." (no stray comma).
        Citations.FormatBibliographyEntry(
                new Source { Author = "Brown", Title = "Work", Year = "2001" }, CitationStyle.Mla)
            .Should().Be("Brown. Work. 2001.");

        // Publisher present, year missing -> "... Publisher."
        Citations.FormatBibliographyEntry(
                new Source { Author = "Brown", Title = "Work", Publisher = "Acme" }, CitationStyle.Chicago)
            .Should().Be("Brown. Work. Acme.");

        // No populated fields -> empty string, in every style.
        Citations.FormatBibliographyEntry(new Source(), CitationStyle.Mla).Should().BeEmpty();
        Citations.FormatBibliographyEntry(new Source(), CitationStyle.Chicago).Should().BeEmpty();
    }

    [Fact]
    public void HeadingTextFor_IsStyleSpecific()
    {
        Citations.HeadingTextFor(CitationStyle.Apa).Should().Be("References");
        Citations.HeadingTextFor(CitationStyle.Mla).Should().Be("Works Cited");
        Citations.HeadingTextFor(CitationStyle.Chicago).Should().Be("Bibliography");

        // The default heading constant tracks the APA (default) style.
        Citations.HeadingText.Should().Be("References");
    }

    [Fact]
    public void BuildBibliography_DefaultOverload_MatchesApa()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Sources.Add(new Source { Author = "Adams", Year = "1979", Title = "Guide" });

        var byDefault = Citations.BuildBibliography(doc).Select(p => p.PlainText);
        var apa = Citations.BuildBibliography(doc, CitationStyle.Apa).Select(p => p.PlainText);

        byDefault.Should().Equal(apa);
    }

    [Fact]
    public void BuildBibliography_PerStyle_UsesStyleHeadingAndEntries()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Sources.Add(new Source { Author = "Adams", Year = "1979", Title = "Guide", Publisher = "Pan" });

        Citations.BuildBibliography(doc, CitationStyle.Apa).Select(p => p.PlainText).Should().Equal(
            "References",
            "Adams. (1979). Guide. Pan.");

        Citations.BuildBibliography(doc, CitationStyle.Mla).Select(p => p.PlainText).Should().Equal(
            "Works Cited",
            "Adams. Guide. Pan, 1979.");

        Citations.BuildBibliography(doc, CitationStyle.Chicago).Select(p => p.PlainText).Should().Equal(
            "Bibliography",
            "Adams. Guide. Pan, 1979.");

        Citations.BuildBibliography(doc, CitationStyle.Ieee).Select(p => p.PlainText).Should().Equal(
            "References",
            "[1] Adams, \"Guide,\" Pan, 1979.");
    }

    // --- IEEE in-text (numeric) -----------------------------------------------------------------------

    [Fact]
    public void FormatInText_Ieee_Source_BracketsTheTagOrAuthor()
    {
        Citations.FormatInText(new Source { Author = "Knuth", Year = "1997" }, CitationStyle.Ieee)
            .Should().Be("[Knuth]");
        Citations.FormatInText(new Source { Tag = "Knuth1997" }, CitationStyle.Ieee).Should().Be("[Knuth1997]");
        Citations.FormatInText(new Source(), CitationStyle.Ieee).Should().Be("[Unknown]");
    }

    [Fact]
    public void FormatInText_Numbered_NumericStylesProduceBracketedNumber()
    {
        Citations.FormatInText(3, CitationStyle.Ieee).Should().Be("[3]");
        Citations.FormatInText(3, CitationStyle.Vancouver).Should().Be("[3]");
        // Author–date styles do not number their in-text citations -> empty so callers fall back.
        Citations.FormatInText(3, CitationStyle.Apa).Should().BeEmpty();
        Citations.FormatInText(3, CitationStyle.Mla).Should().BeEmpty();
        Citations.FormatInText(3, CitationStyle.Chicago).Should().BeEmpty();
    }

    [Theory]
    [InlineData(CitationStyle.Ieee)]
    [InlineData(CitationStyle.Vancouver)]
    public void FormatInText_DocumentAwareNumericStyles_UseSourceOrderNumber(CitationStyle style)
    {
        var first = new Source { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" };
        var second = new Source { Tag = "Tur1936", Author = "Alan Turing", Title = "Computable Numbers", Year = "1936" };
        var doc = new TextDocument();
        doc.Sources.Add(first);
        doc.Sources.Add(second);

        Citations.FormatInText(doc, first, style).Should().Be("[1]");
        Citations.FormatInText(doc, second, style).Should().Be("[2]");
    }

    [Fact]
    public void FormatInText_DocumentAwareNumericStyle_ReusesTaggedSourceNumber()
    {
        var first = new Source { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" };
        var second = new Source { Tag = "Tur1936", Author = "Alan Turing", Title = "Computable Numbers", Year = "1936" };
        var repeated = new Source { Tag = "Tur1936", Author = "Alan M. Turing", Title = "Computable Numbers" };
        var doc = new TextDocument();
        doc.Sources.Add(first);
        doc.Sources.Add(second);

        Citations.FormatInText(doc, second, CitationStyle.Ieee).Should().Be("[2]");
        Citations.FormatInText(doc, repeated, CitationStyle.Ieee).Should().Be("[2]");
    }

    [Fact]
    public void FormatInText_DocumentAwareNumericStyle_UntaggedEqualityIncludesWordFieldDepth()
    {
        var source = new Source
        {
            Author = "Ada Lovelace",
            Title = "Notes",
            BookTitle = "Collected Notes",
            Institution = "Analytical Society",
            City = "London",
            Edition = "Annotated",
            StandardNumber = "ISBN-1",
            ChapterNumber = "4",
            ShortTitle = "Notes",
            Comments = "Original note"
        };
        var clone = new Source
        {
            Author = "Ada Lovelace",
            Title = "Notes",
            BookTitle = "Collected Notes",
            Institution = "Analytical Society",
            City = "London",
            Edition = "Annotated",
            StandardNumber = "ISBN-1",
            ChapterNumber = "4",
            ShortTitle = "Notes",
            Comments = "Original note"
        };
        var differentComment = new Source
        {
            Author = "Ada Lovelace",
            Title = "Notes",
            BookTitle = "Collected Notes",
            Institution = "Analytical Society",
            City = "London",
            Edition = "Annotated",
            StandardNumber = "ISBN-1",
            ChapterNumber = "4",
            ShortTitle = "Notes",
            Comments = "Different note"
        };
        var differentBookTitle = new Source
        {
            Author = "Ada Lovelace",
            Title = "Notes",
            BookTitle = "Different Collection",
            Institution = "Analytical Society",
            City = "London",
            Edition = "Annotated",
            StandardNumber = "ISBN-1",
            ChapterNumber = "4",
            ShortTitle = "Notes",
            Comments = "Original note"
        };
        var differentChapterNumber = new Source
        {
            Author = "Ada Lovelace",
            Title = "Notes",
            BookTitle = "Collected Notes",
            Institution = "Analytical Society",
            City = "London",
            Edition = "Annotated",
            StandardNumber = "ISBN-1",
            ChapterNumber = "5",
            ShortTitle = "Notes",
            Comments = "Original note"
        };
        var differentInstitution = new Source
        {
            Author = "Ada Lovelace",
            Title = "Notes",
            BookTitle = "Collected Notes",
            Institution = "Different Institute",
            City = "London",
            Edition = "Annotated",
            StandardNumber = "ISBN-1",
            ChapterNumber = "4",
            ShortTitle = "Notes",
            Comments = "Original note"
        };
        var doc = new TextDocument();
        doc.Sources.Add(source);

        Citations.FormatInText(doc, clone, CitationStyle.Ieee).Should().Be("[1]");
        Citations.FormatInText(doc, differentComment, CitationStyle.Ieee).Should().Be("[Lovelace]");
        Citations.FormatInText(doc, differentBookTitle, CitationStyle.Ieee).Should().Be("[Lovelace]");
        Citations.FormatInText(doc, differentChapterNumber, CitationStyle.Ieee).Should().Be("[Lovelace]");
        Citations.FormatInText(doc, differentInstitution, CitationStyle.Ieee).Should().Be("[Lovelace]");
    }

    [Fact]
    public void FormatInText_DocumentAwareNumericStyle_MissingSourceFallsBackToPlaceholder()
    {
        var doc = new TextDocument();
        doc.Sources.Add(new Source { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" });

        var missing = new Source { Tag = "Tur1936", Author = "Alan Turing", Title = "Computable Numbers", Year = "1936" };

        Citations.FormatInText(doc, missing, CitationStyle.Ieee).Should().Be("[Turing]");
    }

    [Fact]
    public void TryCreateCitationFieldRun_TaggedSource_BuildsWordLikeCitationField()
    {
        var source = new Source { Tag = "Tur1936", Author = "Alan Turing", Title = "Computable Numbers", Year = "1936" };
        var doc = new TextDocument { BibliographyStyle = CitationStyle.Ieee };
        doc.Sources.Add(new Source { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" });
        doc.Sources.Add(source);

        Citations.TryCreateCitationFieldRun(doc, source, doc.BibliographyStyle, out var run).Should().BeTrue();

        run.Text.Should().Be("[2]");
        run.ComplexField.Should().NotBeNull();
        run.ComplexField!.Instruction.Should().Be(" CITATION Tur1936 ");
        run.ComplexField.Keyword.Should().Be("CITATION");
    }

    [Fact]
    public void TryCreateCitationFieldRun_TagWithSpacesAndQuotes_UsesEscapedWordFieldArgument()
    {
        var source = new Source
        {
            Tag = "Tur \"AI\" 1936",
            Author = "Alan Turing",
            Title = "Computable Numbers",
            Year = "1936"
        };
        var doc = new TextDocument { BibliographyStyle = CitationStyle.Ieee };
        doc.Sources.Add(new Source { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" });
        doc.Sources.Add(source);

        Citations.TryCreateCitationFieldRun(doc, source, doc.BibliographyStyle, out var run).Should().BeTrue();

        run.Text.Should().Be("[2]");
        run.ComplexField.Should().NotBeNull();
        run.ComplexField!.Instruction.Should().Be(" CITATION \"Tur \\\"AI\\\" 1936\" ");
        Citations.ResolveCitationField(doc, run.ComplexField, cached: "stale").Should().Be("[2]");
    }

    [Fact]
    public void TryCreateCitationFieldRun_UntaggedSource_ReturnsFalseForPlainTextFallback()
    {
        var source = new Source { Author = "Jane Q. Doe", Year = "2024" };
        var doc = new TextDocument();

        Citations.TryCreateCitationFieldRun(doc, source, CitationStyle.Apa, out var run).Should().BeFalse();

        run.ComplexField.Should().BeNull();
        run.Text.Should().BeEmpty();
    }

    [Fact]
    public void ResolveCitationField_DeletedSource_PreservesCachedText()
    {
        var doc = new TextDocument { BibliographyStyle = CitationStyle.Ieee };
        doc.Sources.Add(new Source { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" });
        var field = new ComplexField(" CITATION Missing1936 ");

        Citations.ResolveCitationField(doc, field, cached: "[2]").Should().Be("[2]");
    }

    [Fact]
    public void BuildBibliography_NumericStyles_UseSourceOrderAndNumberedEntries()
    {
        var doc = new TextDocument();
        doc.Sources.Add(new Source { Tag = "Z", Author = "Zimmerman", Year = "2001", Title = "Zed" });
        doc.Sources.Add(new Source { Tag = "A", Author = "Adams", Year = "1979", Title = "Guide" });
        doc.Sources.Add(new Source { Tag = "M", Author = "Knuth", Year = "1997", Title = "TAOCP" });

        Citations.BuildBibliography(doc, CitationStyle.Ieee).Select(p => p.PlainText).Should().Equal(
            "References",
            "[1] Zimmerman, \"Zed,\" 2001.",
            "[2] Adams, \"Guide,\" 1979.",
            "[3] Knuth, \"TAOCP,\" 1997.");

        Citations.BuildBibliography(doc, CitationStyle.Vancouver).Select(p => p.PlainText).Should().Equal(
            "References",
            "1. Zimmerman. Zed. 2001.",
            "2. Adams. Guide. 1979.",
            "3. Knuth. TAOCP. 1997.");
    }

    [Fact]
    public void HeadingTextFor_Ieee_IsReferences()
    {
        Citations.HeadingTextFor(CitationStyle.Ieee).Should().Be("References");
    }

    // --- Style name <-> CitationStyle round-trip ------------------------------------------------------

    [Theory]
    [InlineData(CitationStyle.Apa, "APA")]
    [InlineData(CitationStyle.Mla, "MLA")]
    [InlineData(CitationStyle.Chicago, "Chicago")]
    [InlineData(CitationStyle.Ieee, "IEEE")]
    public void StyleName_And_ParseStyle_RoundTrip(CitationStyle style, string name)
    {
        Citations.StyleName(style).Should().Be(name);
        Citations.ParseStyle(name).Should().Be(style);
        // Parsing is case-insensitive and trims.
        Citations.ParseStyle($"  {name.ToLowerInvariant()}  ").Should().Be(style);
    }

    [Fact]
    public void ParseStyle_UnknownOrBlank_FallsBackToProvidedDefault()
    {
        Citations.ParseStyle(null).Should().Be(CitationStyle.Apa);
        Citations.ParseStyle("").Should().Be(CitationStyle.Apa);
        // "Oxford" is not a recognised style name — must fall back to the default.
        Citations.ParseStyle("Oxford").Should().Be(CitationStyle.Apa);
        Citations.ParseStyle("Oxford", CitationStyle.Chicago).Should().Be(CitationStyle.Chicago);
        // Harvard is now a first-class style; ParseStyle must resolve it correctly.
        Citations.ParseStyle("Harvard").Should().Be(CitationStyle.Harvard);
    }

    // --- Type-aware bibliography entries (Book / JournalArticle / WebSite) x each style ----------------

    [Fact]
    public void FormatBibliographyEntry_JournalArticle_PerStyle()
    {
        var article = new Source
        {
            Type = SourceType.JournalArticle,
            Author = "Shannon, C.",
            Title = "A Mathematical Theory of Communication",
            Year = "1948",
            Journal = "Bell System Technical Journal",
            Volume = "27",
            Issue = "3",
            Pages = "379-423"
        };

        // APA: Author. (Year). Title. Journal, vol. V, no. I, pp. P.
        Citations.FormatBibliographyEntry(article, CitationStyle.Apa).Should().Be(
            "Shannon, C. (1948). A Mathematical Theory of Communication. " +
            "Bell System Technical Journal, vol. 27, no. 3, pp. 379-423.");

        // MLA / Chicago: Author. Title. Journal, vol. V, no. I, pp. P, Year.
        Citations.FormatBibliographyEntry(article, CitationStyle.Mla).Should().Be(
            "Shannon, C. A Mathematical Theory of Communication. " +
            "Bell System Technical Journal, vol. 27, no. 3, pp. 379-423, 1948.");
        Citations.FormatBibliographyEntry(article, CitationStyle.Chicago).Should().Be(
            "Shannon, C. A Mathematical Theory of Communication. " +
            "Bell System Technical Journal, vol. 27, no. 3, pp. 379-423, 1948.");

        // IEEE: Author, "Title," Journal, vol. V, no. I, pp. P, Year.
        Citations.FormatBibliographyEntry(article, CitationStyle.Ieee).Should().Be(
            "Shannon, C., \"A Mathematical Theory of Communication,\" " +
            "Bell System Technical Journal, vol. 27, no. 3, pp. 379-423, 1948.");
    }

    [Fact]
    public void FormatBibliographyEntry_WebSite_PerStyle()
    {
        var site = new Source
        {
            Type = SourceType.WebSite,
            Author = "Mozilla",
            Title = "CSS Grid Layout",
            Year = "2023",
            Publisher = "MDN Web Docs",
            Url = "https://developer.mozilla.org/grid",
            Accessed = "3 May 2024"
        };

        // APA: Author. (Year). Title. Publisher, URL, accessed Accessed.
        Citations.FormatBibliographyEntry(site, CitationStyle.Apa).Should().Be(
            "Mozilla. (2023). CSS Grid Layout. " +
            "MDN Web Docs, https://developer.mozilla.org/grid, accessed 3 May 2024.");

        // MLA / Chicago: Author. Title. Publisher, URL, accessed Accessed, Year.
        Citations.FormatBibliographyEntry(site, CitationStyle.Mla).Should().Be(
            "Mozilla. CSS Grid Layout. " +
            "MDN Web Docs, https://developer.mozilla.org/grid, accessed 3 May 2024, 2023.");

        // IEEE: Author, "Title," Publisher, URL, accessed Accessed, Year.
        Citations.FormatBibliographyEntry(site, CitationStyle.Ieee).Should().Be(
            "Mozilla, \"CSS Grid Layout,\" " +
            "MDN Web Docs, https://developer.mozilla.org/grid, accessed 3 May 2024, 2023.");
    }

    [Fact]
    public void FormatBibliographyEntry_WebSite_UsesStructuredAccessedDateBeforeLegacyFallback()
    {
        var site = new Source
        {
            Type = SourceType.WebSite,
            Author = "Mozilla",
            Title = "CSS Grid Layout",
            Year = "2023",
            Url = "https://developer.mozilla.org/grid",
            Accessed = "legacy fallback",
            AccessedDay = "3",
            AccessedMonth = "May",
            AccessedYear = "2024"
        };

        Citations.FormatBibliographyEntry(site, CitationStyle.Apa).Should().Be(
            "Mozilla. (2023). CSS Grid Layout. " +
            "https://developer.mozilla.org/grid, accessed 3 May 2024.");
    }

    [Fact]
    public void FormatBibliographyEntry_Report_UsesInstitutionCityPublisherDetail()
    {
        var report = new Source
        {
            Type = SourceType.Report,
            Author = "Doe, J.",
            Title = "Annual Measurements",
            Year = "2026",
            Institution = "National Bureau of Standards",
            City = "Washington",
            Publisher = "Government Printing Office"
        };

        Citations.FormatBibliographyEntry(report, CitationStyle.Apa).Should().Be(
            "Doe, J. (2026). Annual Measurements. " +
            "National Bureau of Standards, Washington, Government Printing Office.");
        Citations.FormatBibliographyEntry(report, CitationStyle.Mla).Should().Be(
            "Doe, J. Annual Measurements. " +
            "National Bureau of Standards, Washington, Government Printing Office, 2026.");
        Citations.FormatBibliographyEntry(report, CitationStyle.Ieee).Should().Be(
            "Doe, J., \"Annual Measurements,\" " +
            "National Bureau of Standards, Washington, Government Printing Office, 2026.");
    }

    [Fact]
    public void FormatBibliographyEntry_BookSection_UsesChapterTitleContainingBookAndPages()
    {
        var bookSection = new Source
        {
            Type = SourceType.BookSection,
            Author = "Doe, J.",
            Title = "Chapter Title",
            BookTitle = "Containing Book",
            Year = "2026",
            ChapterNumber = "3",
            Pages = "12-20",
            City = "London",
            Publisher = "Test Press"
        };

        Citations.FormatBibliographyEntry(bookSection, CitationStyle.Apa).Should().Be(
            "Doe, J. (2026). Chapter Title. " +
            "Containing Book, chap. 3, pp. 12-20, London: Test Press.");
        Citations.FormatBibliographyEntry(bookSection, CitationStyle.Mla).Should().Be(
            "Doe, J. Chapter Title. " +
            "Containing Book, chap. 3, pp. 12-20, London: Test Press, 2026.");
        Citations.FormatBibliographyEntry(bookSection, CitationStyle.Ieee).Should().Be(
            "Doe, J., \"Chapter Title,\" " +
            "Containing Book, chap. 3, pp. 12-20, London: Test Press, 2026.");
    }

    [Fact]
    public void FormatBibliographyEntry_ConferenceProceedings_UsesConferenceNameAndPages()
    {
        var paper = new Source
        {
            Type = SourceType.ConferenceProceedings,
            Author = "Doe, J.",
            Title = "Proceedings Paper",
            ConferenceName = "Proceedings of the Example Conference",
            Year = "2026",
            Pages = "101-109",
            City = "Berlin",
            Publisher = "ACM"
        };

        Citations.FormatBibliographyEntry(paper, CitationStyle.Apa).Should().Be(
            "Doe, J. (2026). Proceedings Paper. " +
            "Proceedings of the Example Conference, pp. 101-109, Berlin: ACM.");
        Citations.FormatBibliographyEntry(paper, CitationStyle.Mla).Should().Be(
            "Doe, J. Proceedings Paper. " +
            "Proceedings of the Example Conference, pp. 101-109, Berlin: ACM, 2026.");
        Citations.FormatBibliographyEntry(paper, CitationStyle.Ieee).Should().Be(
            "Doe, J., \"Proceedings Paper,\" " +
            "Proceedings of the Example Conference, pp. 101-109, Berlin: ACM, 2026.");
    }

    [Fact]
    public void FormatBibliographyEntry_Book_AllStyles_UsePublisherAsDetail()
    {
        var book = new Source
        {
            Type = SourceType.Book,
            Author = "Knuth, D.",
            Title = "The Art of Computer Programming",
            Year = "1997",
            Publisher = "Addison-Wesley"
        };

        Citations.FormatBibliographyEntry(book, CitationStyle.Apa)
            .Should().Be("Knuth, D. (1997). The Art of Computer Programming. Addison-Wesley.");
        Citations.FormatBibliographyEntry(book, CitationStyle.Mla)
            .Should().Be("Knuth, D. The Art of Computer Programming. Addison-Wesley, 1997.");
        Citations.FormatBibliographyEntry(book, CitationStyle.Ieee)
            .Should().Be("Knuth, D., \"The Art of Computer Programming,\" Addison-Wesley, 1997.");
    }

    [Fact]
    public void FormatBibliographyEntry_Book_IncludesStructuredEditorAndTranslatorRoles()
    {
        var book = new Source
        {
            Type = SourceType.Book,
            Author = "Ramos, L.",
            Title = "Designing Shared Documents",
            Year = "2026",
            City = "Boston",
            Publisher = "Contoso Press",
            Editors =
            [
                SourceAuthorPerson.Create("Eve", string.Empty, "Carter"),
                SourceAuthorPerson.Create("Max", "O.", "Lane")
            ],
            Translators =
            [
                SourceAuthorPerson.Create("Nina", string.Empty, "Patel")
            ]
        };

        Citations.FormatBibliographyEntry(book, CitationStyle.Apa).Should().Be(
            "Ramos, L. (2026). Designing Shared Documents. " +
            "Ed. Eve Carter; Max O. Lane, Trans. Nina Patel, Contoso Press.");
        Citations.FormatBibliographyEntry(book, CitationStyle.Vancouver).Should().Be(
            "Ramos, L. Designing Shared Documents. " +
            "Ed. Eve Carter; Max O. Lane. Trans. Nina Patel. Boston: Contoso Press; 2026.");
        Citations.FormatInText(book, CitationStyle.Apa).Should().Be("(Ramos, 2026)");
    }

    [Fact]
    public void FormatBibliographyEntry_BookSection_IncludesStructuredEditorAndTranslatorRoles()
    {
        var bookSection = new Source
        {
            Type = SourceType.BookSection,
            Author = "Lee, S.",
            Title = "Citation Tools",
            BookTitle = "The Word Processor Handbook",
            Year = "2025",
            ChapterNumber = "7",
            Pages = "101-118",
            City = "Seattle",
            Publisher = "Northwest Press",
            Editors =
            [
                SourceAuthorPerson.Create("Helen", string.Empty, "Ortiz")
            ],
            Translators =
            [
                SourceAuthorPerson.Create("Marco", string.Empty, "Silva"),
                SourceAuthorPerson.Create("Irene", "N.", "Cho")
            ]
        };

        Citations.FormatBibliographyEntry(bookSection, CitationStyle.Apa).Should().Be(
            "Lee, S. (2025). Citation Tools. The Word Processor Handbook, " +
            "Ed. Helen Ortiz, Trans. Marco Silva; Irene N. Cho, chap. 7, pp. 101-118, Seattle: Northwest Press.");
        Citations.FormatBibliographyEntry(bookSection, CitationStyle.Chicago).Should().Be(
            "Lee, S. Citation Tools. The Word Processor Handbook, " +
            "Ed. Helen Ortiz, Trans. Marco Silva; Irene N. Cho, chap. 7, pp. 101-118, Seattle: Northwest Press, 2025.");
        Citations.FormatInText(bookSection, CitationStyle.Chicago).Should().Be("(Lee 2025)");
    }

    [Fact]
    public void FormatBibliographyEntry_Ieee_NoFields_IsEmpty_AndOmitsMissingSegments()
    {
        Citations.FormatBibliographyEntry(new Source { Type = SourceType.JournalArticle }, CitationStyle.Ieee)
            .Should().BeEmpty();

        // Only title + year present -> "Title," + Year, no stray journal/volume segments.
        Citations.FormatBibliographyEntry(
                new Source { Type = SourceType.JournalArticle, Title = "Untitled", Year = "2000" },
                CitationStyle.Ieee)
            .Should().Be("\"Untitled,\" 2000.");
    }

    [Fact]
    public void FormatBibliographyEntry_JournalArticle_PartialDetail_OmitsCleanly()
    {
        // Journal + volume only (no issue/pages): "Journal, vol. V" — no empty "no."/"pp." segments.
        Citations.FormatBibliographyEntry(
                new Source
                {
                    Type = SourceType.JournalArticle,
                    Author = "Doe",
                    Title = "Study",
                    Year = "2010",
                    Journal = "Nature",
                    Volume = "5"
                },
                CitationStyle.Apa)
            .Should().Be("Doe. (2010). Study. Nature, vol. 5.");
    }

    [Fact]
    public void SourceType_NewWordBreadthValues_AreAppendedAfterExistingValues()
    {
        ((int)SourceType.Book).Should().Be(0);
        ((int)SourceType.JournalArticle).Should().Be(1);
        ((int)SourceType.WebSite).Should().Be(2);
        ((int)SourceType.Report).Should().Be(3);
        ((int)SourceType.BookSection).Should().Be(4);
        ((int)SourceType.ConferenceProceedings).Should().Be(5);
        ((int)SourceType.ArticleInPeriodical).Should().Be(6);
        ((int)SourceType.ElectronicSource).Should().Be(7);
        ((int)SourceType.Patent).Should().Be(8);
        ((int)SourceType.Interview).Should().Be(9);
        ((int)SourceType.Misc).Should().Be(10);
        ((int)SourceType.Film).Should().Be(11);
        ((int)SourceType.SoundRecording).Should().Be(12);
        ((int)SourceType.Art).Should().Be(13);
        ((int)SourceType.InternetSite).Should().Be(14);
        ((int)SourceType.Performance).Should().Be(15);
        ((int)SourceType.Case).Should().Be(16);
    }

    [Fact]
    public void FormatBibliographyEntry_SourceManagerBreadthTypes_UseModeledFields()
    {
        var patent = new Source
        {
            Type = SourceType.Patent,
            Inventor = "Lovelace, Ada",
            Title = "Analytical Engine Control",
            Year = "1843",
            Month = "July",
            Day = "4",
            PatentNumber = "GB-1843-1",
            CountryRegion = "United Kingdom",
            StateProvince = "London"
        };
        var interview = new Source
        {
            Type = SourceType.Interview,
            Interviewee = "Hopper, Grace",
            Interviewer = "Mauchly, Jean",
            Title = "Compiler Notes",
            Year = "1968",
            Medium = "Recorded interview"
        };
        var misc = new Source
        {
            Type = SourceType.Misc,
            Author = "Example Archive",
            Title = "Loose note",
            Year = "2026",
            SourceKind = "Manuscript",
            Medium = "Scan"
        };
        var caseSource = new Source
        {
            Type = SourceType.Case,
            Author = "Brown",
            Title = "Brown v. Board of Education",
            Year = "1954",
            Month = "May",
            Day = "17",
            CaseNumber = "1",
            Court = "U.S. Supreme Court",
            Reporter = "347 U.S. 483",
            CountryRegion = "United States",
            StateProvince = "District of Columbia"
        };

        Citations.FormatInText(patent, CitationStyle.Apa).Should().Be("(Lovelace, 1843)");
        Citations.FormatBibliographyEntry(patent, CitationStyle.Apa)
            .Should().Be("Lovelace, Ada. (1843). Analytical Engine Control. patent GB-1843-1, United Kingdom, London, 4 July 1843.");
        Citations.FormatBibliographyEntry(interview, CitationStyle.Chicago)
            .Should().Be("Hopper, Grace. Compiler Notes. interview by Mauchly, Jean, Recorded interview, 1968.");
        Citations.FormatBibliographyEntry(misc, CitationStyle.Ieee)
            .Should().Be("Example Archive, \"Loose note,\" Manuscript, Scan, 2026.");
        Citations.FormatBibliographyEntry(caseSource, CitationStyle.Apa)
            .Should().Be("Brown. (1954). Brown v. Board of Education. case no. 1, U.S. Supreme Court, 347 U.S. 483, United States, District of Columbia, 17 May 1954.");
    }

    [Fact]
    public void FormatBibliographyEntry_MediaBreadthTypes_UseModeledFields()
    {
        var film = new Source
        {
            Type = SourceType.Film,
            Director = "Kubrick, Stanley",
            ProducerName = "MGM",
            Writer = "Clarke, Arthur C.",
            Performer = "Dullea, Keir",
            Title = "2001: A Space Odyssey",
            Year = "1968",
            ProductionCompany = "Metro-Goldwyn-Mayer",
            Medium = "Film"
        };
        var recording = new Source
        {
            Type = SourceType.SoundRecording,
            Artist = "Holiday, Billie",
            Composer = "Strange, Lewis Allan",
            Conductor = "Jones, Quincy",
            Title = "Strange Fruit",
            AlbumTitle = "Lady Sings",
            Year = "1956",
            RecordingNumber = "RS-1",
            Medium = "LP"
        };
        var art = new Source
        {
            Type = SourceType.Art,
            Artist = "Kahlo, Frida",
            Title = "The Broken Column",
            Year = "1944",
            Medium = "Oil on masonite",
            Institution = "Museo Dolores Olmedo",
            City = "Mexico City"
        };
        var internetSite = new Source
        {
            Type = SourceType.InternetSite,
            Author = "Example Archive",
            Title = "Example Home",
            Year = "2026",
            Publisher = "Example Site",
            Url = "https://example.test",
            AccessedDay = "7",
            AccessedMonth = "July",
            AccessedYear = "2026"
        };
        var performance = new Source
        {
            Type = SourceType.Performance,
            Performer = "Royal Shakespeare Company",
            Conductor = "Doe, Jane",
            Title = "Hamlet",
            Year = "2026",
            Month = "May",
            Day = "8",
            Theater = "Globe Theatre",
            City = "London",
            Medium = "Stage performance"
        };

        Citations.FormatInText(film, CitationStyle.Apa).Should().Be("(Kubrick, 1968)");
        Citations.FormatBibliographyEntry(film, CitationStyle.Apa)
            .Should().Be("Kubrick, Stanley. (1968). 2001: A Space Odyssey. produced by MGM, written by Clarke, Arthur C., performed by Dullea, Keir, Metro-Goldwyn-Mayer, Film.");
        Citations.FormatBibliographyEntry(recording, CitationStyle.Chicago)
            .Should().Be("Holiday, Billie. Strange Fruit. Lady Sings, composed by Strange, Lewis Allan, conducted by Jones, Quincy, recording RS-1, LP, 1956.");
        Citations.FormatBibliographyEntry(art, CitationStyle.Ieee)
            .Should().Be("Kahlo, Frida, \"The Broken Column,\" Oil on masonite, Museo Dolores Olmedo, Mexico City, 1944.");
        Citations.FormatBibliographyEntry(internetSite, CitationStyle.Apa)
            .Should().Be("Example Archive. (2026). Example Home. Example Site, https://example.test, accessed 7 July 2026.");
        Citations.FormatBibliographyEntry(performance, CitationStyle.Chicago)
            .Should().Be("Royal Shakespeare Company. Hamlet. conducted by Doe, Jane, Globe Theatre, London, Stage performance, 8 May 2026, 2026.");
    }

    [Fact]
    public void FormatBibliographyEntry_ArticleInPeriodical_PerStyle()
    {
        var article = new Source
        {
            Type = SourceType.ArticleInPeriodical,
            Author = "Roe",
            Title = "City Desk",
            Year = "2026",
            Journal = "Daily Planet",
            Volume = "12",
            Issue = "4",
            Pages = "5-7"
        };

        Citations.FormatBibliographyEntry(article, CitationStyle.Apa).Should().Be(
            "Roe. (2026). City Desk. Daily Planet, vol. 12, no. 4, pp. 5-7.");
        Citations.FormatBibliographyEntry(article, CitationStyle.Mla).Should().Be(
            "Roe. City Desk. Daily Planet, vol. 12, no. 4, pp. 5-7, 2026.");
        Citations.FormatBibliographyEntry(article, CitationStyle.Chicago).Should().Be(
            "Roe. City Desk. Daily Planet, vol. 12, no. 4, pp. 5-7, 2026.");
        Citations.FormatBibliographyEntry(article, CitationStyle.Ieee).Should().Be(
            "Roe, \"City Desk,\" Daily Planet, vol. 12, no. 4, pp. 5-7, 2026.");
        Citations.FormatBibliographyEntry(article, CitationStyle.Vancouver).Should().Be(
            "Roe. City Desk. Daily Planet. 2026;12(4):5-7.");
        Citations.FormatBibliographyEntry(article, CitationStyle.Harvard).Should().Be(
            "Roe 2026, City Desk. Daily Planet, vol. 12, no. 4, pp. 5-7.");
        Citations.FormatBibliographyEntry(article, CitationStyle.Gost).Should().Be(
            "Roe. City Desk. Daily Planet 2026. Vol. 12. No. 4. Pp. 5-7.");
        Citations.FormatBibliographyEntry(article, CitationStyle.Iso690).Should().Be(
            "ROE, 2026. City Desk. Daily Planet, 12(4), 5-7.");
    }

    [Fact]
    public void FormatBibliographyEntry_ElectronicSource_PerStyle()
    {
        var electronic = new Source
        {
            Type = SourceType.ElectronicSource,
            Author = "Ada",
            Title = "Online Notes",
            Year = "2026",
            Publisher = "Example Archive",
            Url = "https://example.test/notes",
            AccessedDay = "4",
            AccessedMonth = "July",
            AccessedYear = "2026"
        };

        Citations.FormatBibliographyEntry(electronic, CitationStyle.Apa).Should().Be(
            "Ada. (2026). Online Notes. Example Archive, https://example.test/notes, accessed 4 July 2026.");
        Citations.FormatBibliographyEntry(electronic, CitationStyle.Mla).Should().Be(
            "Ada. Online Notes. Example Archive, https://example.test/notes, accessed 4 July 2026, 2026.");
        Citations.FormatBibliographyEntry(electronic, CitationStyle.Chicago).Should().Be(
            "Ada. Online Notes. Example Archive, https://example.test/notes, accessed 4 July 2026, 2026.");
        Citations.FormatBibliographyEntry(electronic, CitationStyle.Ieee).Should().Be(
            "Ada, \"Online Notes,\" Example Archive, https://example.test/notes, accessed 4 July 2026, 2026.");
        Citations.FormatBibliographyEntry(electronic, CitationStyle.Vancouver).Should().Be(
            "Ada. Online Notes. Example Archive; https://example.test/notes; accessed 4 July 2026; 2026.");
        Citations.FormatBibliographyEntry(electronic, CitationStyle.Harvard).Should().Be(
            "Ada 2026, Online Notes. Example Archive, https://example.test/notes, accessed 4 July 2026.");
        Citations.FormatBibliographyEntry(electronic, CitationStyle.Gost).Should().Be(
            "Ada. Online Notes. Example Archive, https://example.test/notes, accessed 4 July 2026. 2026.");
        Citations.FormatBibliographyEntry(electronic, CitationStyle.Iso690).Should().Be(
            "ADA, 2026. Online Notes. Example Archive, https://example.test/notes, accessed 4 July 2026.");
    }

    // --- Turabian -------------------------------------------------------------------------------

    [Fact]
    public void FormatInText_Turabian_AuthorYear_ProducesSameAsChicago()
    {
        // Turabian author–date in-text is identical to Chicago: (Author Year).
        var source = new Source { Author = "Knuth", Year = "1997" };
        Citations.FormatInText(source, CitationStyle.Turabian).Should().Be("(Knuth 1997)");
    }

    [Fact]
    public void FormatBibliographyEntry_Turabian_MatchesChicagoOrdering()
    {
        // Turabian bibliography order is the same as Chicago: Author. Title. Publisher, Year.
        var source = new Source { Author = "Knuth, D.", Year = "1997", Title = "TAOCP", Publisher = "AW" };
        Citations.FormatBibliographyEntry(source, CitationStyle.Turabian).Should().Be("Knuth, D. TAOCP. AW, 1997.");
    }

    [Fact]
    public void HeadingTextFor_Turabian_IsBibliography()
    {
        Citations.HeadingTextFor(CitationStyle.Turabian).Should().Be("Bibliography");
    }

    // --- Harvard --------------------------------------------------------------------------------

    [Fact]
    public void FormatInText_Harvard_AuthorYear_ProducesSameAsApa()
    {
        // Harvard author–date in-text: (Author, Year).
        var source = new Source { Author = "Knuth", Year = "1997" };
        Citations.FormatInText(source, CitationStyle.Harvard).Should().Be("(Knuth, 1997)");
    }

    [Fact]
    public void FormatBibliographyEntry_Harvard_YearFollowsAuthorDirectly()
    {
        // Harvard: Author Year, Title. Publisher.
        var source = new Source { Author = "Knuth, D.", Year = "1997", Title = "TAOCP", Publisher = "Addison-Wesley" };
        var entry = Citations.FormatBibliographyEntry(source, CitationStyle.Harvard);
        entry.Should().StartWith("Knuth, D. 1997,");
        entry.Should().Contain("TAOCP.");
    }

    [Fact]
    public void FormatBibliographyEntry_Harvard_NoYear_OmitsYearGracefully()
    {
        var source = new Source { Author = "Brown", Title = "Work", Publisher = "Press" };
        var entry = Citations.FormatBibliographyEntry(source, CitationStyle.Harvard);
        entry.Should().StartWith("Brown.");
        entry.Should().Contain("Work.");
        entry.Should().Contain("Press.");
    }

    // --- Vancouver ------------------------------------------------------------------------------

    [Fact]
    public void FormatInText_Vancouver_BracketsAuthor()
    {
        // Vancouver numeric: [Author] as placeholder when no reference number is known.
        var source = new Source { Author = "Doe", Year = "2000" };
        Citations.FormatInText(source, CitationStyle.Vancouver).Should().Be("[Doe]");
    }

    [Fact]
    public void FormatInText_Vancouver_Numbered_IsSquareBracketed()
    {
        Citations.FormatInText(5, CitationStyle.Vancouver).Should().Be("[5]");
    }

    [Fact]
    public void FormatBibliographyEntry_Vancouver_JournalUsesCondensedForm()
    {
        var article = new Source
        {
            Type = SourceType.JournalArticle,
            Author = "Doe J.",
            Title = "A Study",
            Year = "2000",
            Journal = "N Engl J Med",
            Volume = "342",
            Issue = "1",
            Pages = "1-10"
        };
        var entry = Citations.FormatBibliographyEntry(article, CitationStyle.Vancouver);
        // Vancouver form: Author. Title. Journal. Year;Vol(Issue):Pages.
        entry.Should().Contain("Doe J.");
        entry.Should().Contain("A Study.");
        entry.Should().Contain("N Engl J Med.");
        entry.Should().Contain("2000;342(1):1-10");
    }

    [Fact]
    public void FormatBibliographyEntry_Vancouver_BookUsesPublisherSemicolonYear()
    {
        var book = new Source { Author = "Smith", Title = "Book", Publisher = "Wiley", Year = "2020" };
        var entry = Citations.FormatBibliographyEntry(book, CitationStyle.Vancouver);
        entry.Should().Contain("Smith.");
        entry.Should().Contain("Book.");
        entry.Should().Contain("Wiley; 2020.");
    }

    [Fact]
    public void FormatBibliographyEntry_Vancouver_BookUsesCityPublisherWhenPresent()
    {
        var book = new Source
        {
            Type = SourceType.Book,
            Author = "Smith",
            Title = "Book",
            City = "London",
            Publisher = "Wiley",
            Year = "2020"
        };

        Citations.FormatBibliographyEntry(book, CitationStyle.Vancouver)
            .Should().Contain("London: Wiley; 2020.");
    }

    // --- GOST -----------------------------------------------------------------------------------

    [Fact]
    public void FormatBibliographyEntry_Gost_AuthorTitlePublisherYear()
    {
        var book = new Source { Author = "Иванов И.И.", Title = "Книга", Publisher = "Наука", Year = "2010" };
        var entry = Citations.FormatBibliographyEntry(book, CitationStyle.Gost);
        // GOST: Author. Title. Publisher, Year.
        entry.Should().Contain("Иванов И.И.");
        entry.Should().Contain("Книга.");
        entry.Should().Contain("Наука, 2010.");
    }

    [Fact]
    public void FormatBibliographyEntry_Gost_BookUsesCityPublisherWhenPresent()
    {
        var book = new Source { Author = "Ivanov I.", Title = "Book", City = "Moscow", Publisher = "Nauka", Year = "2010" };

        Citations.FormatBibliographyEntry(book, CitationStyle.Gost)
            .Should().Contain("Moscow: Nauka, 2010.");
    }

    [Fact]
    public void FormatBibliographyEntry_Gost_JournalArticleUsesVolumeAndNo()
    {
        var article = new Source
        {
            Type = SourceType.JournalArticle,
            Author = "Petrov P.",
            Title = "Analysis",
            Year = "2005",
            Journal = "Russian Journal",
            Volume = "3",
            Issue = "2",
            Pages = "11-20"
        };
        var entry = Citations.FormatBibliographyEntry(article, CitationStyle.Gost);
        entry.Should().Contain("Petrov P.");
        entry.Should().Contain("Analysis.");
        entry.Should().Contain("Russian Journal");
        entry.Should().Contain("Vol. 3.");
        entry.Should().Contain("No. 2.");
        entry.Should().Contain("Pp. 11-20.");
    }

    // --- ISO-690 --------------------------------------------------------------------------------

    [Fact]
    public void FormatBibliographyEntry_Iso690_AuthorIsUpperCase()
    {
        var book = new Source { Author = "Knuth, D.", Year = "1997", Title = "TAOCP", Publisher = "AW" };
        var entry = Citations.FormatBibliographyEntry(book, CitationStyle.Iso690);
        // ISO 690: AUTHOR, Year. Title. Publisher.
        entry.Should().StartWith("KNUTH, D., 1997.");
        entry.Should().Contain("TAOCP.");
        entry.Should().Contain("AW.");
    }

    [Fact]
    public void FormatBibliographyEntry_Iso690_BookUsesCityPublisherWhenPresent()
    {
        var book = new Source { Author = "Knuth, D.", Year = "1997", Title = "TAOCP", City = "Reading", Publisher = "AW" };

        Citations.FormatBibliographyEntry(book, CitationStyle.Iso690)
            .Should().Contain("Reading: AW.");
    }

    [Fact]
    public void FormatBibliographyEntry_Iso690_JournalIncludesVolAndIssue()
    {
        var article = new Source
        {
            Type = SourceType.JournalArticle,
            Author = "Shannon, C.",
            Title = "A Mathematical Theory of Communication",
            Year = "1948",
            Journal = "Bell System Technical Journal",
            Volume = "27",
            Issue = "3",
            Pages = "379-423"
        };
        var entry = Citations.FormatBibliographyEntry(article, CitationStyle.Iso690);
        entry.Should().StartWith("SHANNON, C., 1948.");
        entry.Should().Contain("A Mathematical Theory of Communication.");
        entry.Should().Contain("Bell System Technical Journal");
        entry.Should().Contain("27(3)");
        entry.Should().Contain("379-423");
    }

    // --- StyleName / ParseStyle round-trip for new styles ----------------------------------------

    [Theory]
    [InlineData(CitationStyle.Turabian, "Turabian")]
    [InlineData(CitationStyle.Harvard,  "Harvard")]
    [InlineData(CitationStyle.Vancouver, "Vancouver")]
    [InlineData(CitationStyle.Gost, "GOST")]
    [InlineData(CitationStyle.Iso690, "ISO690")]
    public void StyleName_And_ParseStyle_RoundTrip_NewStyles(CitationStyle style, string name)
    {
        Citations.StyleName(style).Should().Be(name);
        Citations.ParseStyle(name).Should().Be(style);
        Citations.ParseStyle($"  {name.ToLowerInvariant()}  ").Should().Be(style);
    }

    [Fact]
    public void ParseStyle_UnknownName_FallsBackToDefault_WithNewStyles()
    {
        // Vancouver / Harvard / GOST / ISO-690 are distinct from unknown values.
        Citations.ParseStyle("Oxford").Should().Be(CitationStyle.Apa);
        Citations.ParseStyle("Harvard", CitationStyle.Ieee).Should().Be(CitationStyle.Harvard);
    }

    // --- BuildBibliography headings for new styles -----------------------------------------------

    [Fact]
    public void BuildBibliography_NewStyles_UseCorrectHeading()
    {
        var doc = new TextDocument();
        doc.Sources.Add(new Source { Author = "A", Year = "2000", Title = "T" });

        Citations.BuildBibliography(doc, CitationStyle.Turabian)[0].PlainText.Should().Be("Bibliography");
        Citations.BuildBibliography(doc, CitationStyle.Harvard)[0].PlainText.Should().Be("References");
        Citations.BuildBibliography(doc, CitationStyle.Vancouver)[0].PlainText.Should().Be("References");
        Citations.BuildBibliography(doc, CitationStyle.Gost)[0].PlainText.Should().Be("References");
        Citations.BuildBibliography(doc, CitationStyle.Iso690)[0].PlainText.Should().Be("References");
    }

    [Fact]
    public void BuildBibliography_NewStyles_ProduceDistinctEntries()
    {
        // Each new style must produce output distinct from APA so they are genuinely different formatters.
        var source = new Source
        {
            Author = "Doe, J.",
            Year = "2022",
            Title = "Research Paper",
            Publisher = "Springer",
            Type = SourceType.Book
        };
        var doc = new TextDocument();
        doc.Sources.Add(source);

        var apa       = Citations.BuildBibliography(doc, CitationStyle.Apa).Last().PlainText;
        var harvard   = Citations.BuildBibliography(doc, CitationStyle.Harvard).Last().PlainText;
        var vancouver = Citations.BuildBibliography(doc, CitationStyle.Vancouver).Last().PlainText;
        var gost      = Citations.BuildBibliography(doc, CitationStyle.Gost).Last().PlainText;
        var iso690    = Citations.BuildBibliography(doc, CitationStyle.Iso690).Last().PlainText;

        // Each entry must differ from APA to prove they're separate formatters.
        harvard.Should().NotBe(apa, "Harvard year-after-author ordering differs from APA");
        vancouver.Should().NotBe(apa, "Vancouver Publisher;Year ordering differs from APA");
        gost.Should().NotBe(apa, "GOST Publisher, Year ordering differs from APA");
        iso690.Should().NotBe(apa, "ISO-690 ALL-CAPS author differs from APA");
    }
}
