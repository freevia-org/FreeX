namespace FreeW.Core.Model.Tests;

public class DocumentIndexTests
{
    [Fact]
    public void Build_EmptyDocument_YieldsOnlyTheHeadingParagraph()
    {
        var doc = new TextDocument();

        var index = DocumentIndex.Build(doc);

        index.Should().ContainSingle();
        index[0].PlainText.Should().Be(DocumentIndex.HeadingText);
        index[0].StyleId.Should().Be(DocumentIndex.HeadingStyleId);
    }

    [Fact]
    public void Build_FromTerms_SortsAlphabeticallyCaseInsensitiveAndDedupes()
    {
        var index = DocumentIndex.Build(new[] { "banana", "Apple", "cherry", "apple", "Banana" });

        // Heading first, then distinct terms sorted case-insensitively (the first-seen casing wins).
        index.Select(p => p.PlainText).Should().Equal(
            DocumentIndex.HeadingText,
            "Apple",
            "banana",
            "cherry");

        // Every entry paragraph carries the index entry style.
        index.Skip(1).Should().OnlyContain(p => p.StyleId == DocumentIndex.EntryStyleId);
    }

    [Fact]
    public void Build_TrimsAndSkipsBlankTerms()
    {
        var index = DocumentIndex.Build(new[] { "  spaced  ", "", "   ", "kept" });

        index.Select(p => p.PlainText).Should().Equal(
            DocumentIndex.HeadingText,
            "kept",
            "spaced");
    }

    [Fact]
    public void Build_FromDocumentIndexEntries_UsesMarkedTerms()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.IndexEntries.Add(new IndexEntry("Zebra"));
        doc.IndexEntries.Add(new IndexEntry("alpha"));
        doc.IndexEntries.Add(new IndexEntry("alpha")); // duplicate collapsed

        var index = DocumentIndex.Build(doc);

        index.Select(p => p.PlainText).Should().Equal(
            DocumentIndex.HeadingText,
            "alpha, 1",
            "Zebra, 1");
    }

    [Fact]
    public void Build_HiddenMarksAggregateDistinctLogicalPagesAndOverrideLegacySideStore()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph
        {
            Runs = { new Run("First"), DocumentIndex.MarkRun("Alpha") }
        });
        doc.Blocks.Add(DocumentOps.CreatePageBreak());
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Second"),
                DocumentIndex.MarkRun("alpha"),
                DocumentIndex.MarkRun("Beta"),
                DocumentIndex.MarkRun("Alpha")
            }
        });
        doc.IndexEntries.Add(new IndexEntry("Alpha"));

        var index = DocumentIndex.Build(doc, blockIndex => blockIndex == 0 ? "iv" : "1");

        index.Select(paragraph => paragraph.PlainText).Should().Equal(
            DocumentIndex.HeadingText,
            "Alpha, iv, 1",
            "Beta, 1");
    }

    [Fact]
    public void MarkRun_RoundTripsQuotedTermThroughFieldInstructionParser()
    {
        var mark = DocumentIndex.MarkRun("  Alpha \\\"quoted\\\"  ");

        mark.Text.Should().BeEmpty();
        mark.ComplexField!.Keyword.Should().Be("XE");
        DocumentIndex.MarkedTerm(mark).Should().Be("Alpha \\\"quoted\\\"");
        DocumentIndex.MarkedTerm(new Run("Alpha")).Should().BeNull();
    }

    [Fact]
    public void Build_DoesNotMutateTheDocument()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.IndexEntries.Add(new IndexEntry("term"));

        var blocksBefore = doc.Blocks.Count;
        var entriesBefore = doc.IndexEntries.Count;

        DocumentIndex.Build(doc);

        doc.Blocks.Should().HaveCount(blocksBefore);
        doc.IndexEntries.Should().HaveCount(entriesBefore);
    }

    [Fact]
    public void IsIndexStyleId_RecognisesGeneratedStyles()
    {
        DocumentIndex.IsIndexStyleId(DocumentIndex.HeadingStyleId).Should().BeTrue();
        DocumentIndex.IsIndexStyleId(DocumentIndex.EntryStyleId).Should().BeTrue();

        DocumentIndex.IsIndexStyleId(null).Should().BeFalse();
        DocumentIndex.IsIndexStyleId("").Should().BeFalse();
        DocumentIndex.IsIndexStyleId("Normal").Should().BeFalse();
        DocumentIndex.IsIndexStyleId("Heading1").Should().BeFalse();
    }

    [Fact]
    public void IsIndexParagraph_TrueOnlyForIndexStyledParagraphs()
    {
        DocumentIndex.IsIndexParagraph(new Paragraph("x") { StyleId = DocumentIndex.EntryStyleId }).Should().BeTrue();
        DocumentIndex.IsIndexParagraph(new Paragraph("x") { StyleId = DocumentIndex.HeadingStyleId }).Should().BeTrue();
        DocumentIndex.IsIndexParagraph(new Paragraph("x") { StyleId = "Heading1" }).Should().BeFalse();
        DocumentIndex.IsIndexParagraph(Table.Create(1, 1)).Should().BeFalse();
    }

    [Fact]
    public void EnsureStyles_RegistersIndexStylesIdempotently()
    {
        var doc = TextDocument.CreateEmpty();

        DocumentIndex.EnsureStyles(doc);
        DocumentIndex.EnsureStyles(doc); // second call must not throw or duplicate

        doc.Styles.Should().ContainKey(DocumentIndex.HeadingStyleId);
        doc.Styles.Should().ContainKey(DocumentIndex.EntryStyleId);
    }

    [Fact]
    public void EnsureStyles_DoesNotOverwriteAnExistingStyle()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Styles[DocumentIndex.HeadingStyleId] = new DocumentStyle
        {
            Id = DocumentIndex.HeadingStyleId,
            Name = "Custom"
        };

        DocumentIndex.EnsureStyles(doc);

        doc.Styles[DocumentIndex.HeadingStyleId].Name.Should().Be("Custom");
    }

    [Fact]
    public void IndexEntry_TrimsTermAtConstruction()
    {
        new IndexEntry("  hello  ").Term.Should().Be("hello");
    }

    [Fact]
    public void CreateEmpty_RegistersBuiltInIndexStyles()
    {
        var doc = TextDocument.CreateEmpty();

        doc.Styles.Should().ContainKey(DocumentIndex.HeadingStyleId);
        doc.Styles.Should().ContainKey(DocumentIndex.EntryStyleId);
    }
}
