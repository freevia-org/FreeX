namespace FreeW.Core.Model.Tests;

public class TableOfAuthoritiesTests
{
    [Fact]
    public void Build_EmptyDocument_YieldsOnlyTheHeadingParagraph()
    {
        var doc = new TextDocument();

        var table = TableOfAuthorities.Build(doc);

        table.Should().ContainSingle();
        table[0].PlainText.Should().Be(TableOfAuthorities.HeadingText);
        table[0].StyleId.Should().Be(TableOfAuthorities.HeadingStyleId);
    }

    [Fact]
    public void Build_GroupsByCategoryInWordOrderWithCategoryHeadings()
    {
        var citations = new[]
        {
            new Citation("17 U.S.C. § 107", CitationCategory.Statutes),
            new Citation("Brown v. Board, 347 U.S. 483", CitationCategory.Cases),
            new Citation("Fed. R. Civ. P. 12", CitationCategory.Rules)
        };

        var table = TableOfAuthorities.Build(citations).Select(p => p.PlainText).ToList();

        // Heading, then Cases, Statutes, Rules in Word's display order, each with a category heading.
        table.Should().Equal(
            TableOfAuthorities.HeadingText,
            "Cases",
            "Brown v. Board, 347 U.S. 483",
            "Statutes",
            "17 U.S.C. § 107",
            "Rules",
            "Fed. R. Civ. P. 12");
    }

    [Fact]
    public void Build_SortsEntriesAlphabeticallyAndDedupesWithinCategory()
    {
        var citations = new[]
        {
            new Citation("Zoning Act § 5", CitationCategory.Statutes),
            new Citation("Antitrust Act § 1", CitationCategory.Statutes),
            new Citation("antitrust act § 1", CitationCategory.Statutes) // case-insensitive duplicate
        };

        var table = TableOfAuthorities.Build(citations).Select(p => p.PlainText).ToList();

        table.Should().Equal(
            TableOfAuthorities.HeadingText,
            "Statutes",
            "Antitrust Act § 1",
            "Zoning Act § 5");
    }

    [Fact]
    public void Build_SkipsBlankLongCitationsAndEmptyCategories()
    {
        var citations = new[]
        {
            new Citation("   ", CitationCategory.Cases),       // blank long form — skipped
            new Citation("Real Statute", CitationCategory.Statutes)
        };

        var table = TableOfAuthorities.Build(citations).Select(p => p.PlainText).ToList();

        // No "Cases" category heading (it had only a blank entry), only Statutes.
        table.Should().Equal(
            TableOfAuthorities.HeadingText,
            "Statutes",
            "Real Statute");
    }

    [Fact]
    public void Build_CategoryParagraphsCarryTheCategoryStyleAndEntriesTheEntryStyle()
    {
        var citations = new[] { new Citation("Some Case", CitationCategory.Cases) };

        var table = TableOfAuthorities.Build(citations);

        table.Single(p => p.PlainText == "Cases").StyleId.Should().Be(TableOfAuthorities.CategoryStyleId);
        table.Single(p => p.PlainText == "Some Case").StyleId.Should().Be(TableOfAuthorities.EntryStyleId);
    }

    [Fact]
    public void Build_FromDocument_CollectsBodyCitationMarksAndSideStore()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.CitationMark(new Citation("Body Case", CitationCategory.Cases)));
        doc.Blocks.Add(paragraph);
        doc.Citations.Add(new Citation("Side Statute", CitationCategory.Statutes));

        var table = TableOfAuthorities.Build(doc).Select(p => p.PlainText).ToList();

        table.Should().Equal(
            TableOfAuthorities.HeadingText,
            "Cases",
            "Body Case",
            "Statutes",
            "Side Statute");
    }

    [Fact]
    public void Build_DoesNotMutateTheDocument()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Citations.Add(new Citation("Case", CitationCategory.Cases));

        var blocksBefore = doc.Blocks.Count;
        var citationsBefore = doc.Citations.Count;

        TableOfAuthorities.Build(doc);

        doc.Blocks.Should().HaveCount(blocksBefore);
        doc.Citations.Should().HaveCount(citationsBefore);
    }

    [Fact]
    public void IsTableOfAuthoritiesStyleId_RecognisesGeneratedStyles()
    {
        TableOfAuthorities.IsTableOfAuthoritiesStyleId(TableOfAuthorities.HeadingStyleId).Should().BeTrue();
        TableOfAuthorities.IsTableOfAuthoritiesStyleId(TableOfAuthorities.CategoryStyleId).Should().BeTrue();
        TableOfAuthorities.IsTableOfAuthoritiesStyleId(TableOfAuthorities.EntryStyleId).Should().BeTrue();

        TableOfAuthorities.IsTableOfAuthoritiesStyleId(null).Should().BeFalse();
        TableOfAuthorities.IsTableOfAuthoritiesStyleId("").Should().BeFalse();
        TableOfAuthorities.IsTableOfAuthoritiesStyleId("Normal").Should().BeFalse();
    }

    [Fact]
    public void IsTableOfAuthoritiesParagraph_TrueOnlyForToaStyledParagraphs()
    {
        TableOfAuthorities.IsTableOfAuthoritiesParagraph(
            new Paragraph("x") { StyleId = TableOfAuthorities.EntryStyleId }).Should().BeTrue();
        TableOfAuthorities.IsTableOfAuthoritiesParagraph(
            new Paragraph("x") { StyleId = "Heading1" }).Should().BeFalse();
        TableOfAuthorities.IsTableOfAuthoritiesParagraph(Table.Create(1, 1)).Should().BeFalse();
    }

    [Fact]
    public void EnsureStyles_RegistersToaStylesIdempotently()
    {
        var doc = TextDocument.CreateEmpty();

        TableOfAuthorities.EnsureStyles(doc);
        TableOfAuthorities.EnsureStyles(doc); // second call must not throw or duplicate

        doc.Styles.Should().ContainKey(TableOfAuthorities.HeadingStyleId);
        doc.Styles.Should().ContainKey(TableOfAuthorities.CategoryStyleId);
        doc.Styles.Should().ContainKey(TableOfAuthorities.EntryStyleId);
    }

    [Fact]
    public void CreateEmpty_RegistersBuiltInToaStyles()
    {
        var doc = TextDocument.CreateEmpty();

        doc.Styles.Should().ContainKey(TableOfAuthorities.HeadingStyleId);
        doc.Styles.Should().ContainKey(TableOfAuthorities.CategoryStyleId);
        doc.Styles.Should().ContainKey(TableOfAuthorities.EntryStyleId);
    }

    [Fact]
    public void Citation_TrimsFieldsAtConstruction()
    {
        var citation = new Citation("  Brown v. Board  ", CitationCategory.Cases, "  Brown  ");

        citation.LongCitation.Should().Be("Brown v. Board");
        citation.ShortCitation.Should().Be("Brown");
    }
}
