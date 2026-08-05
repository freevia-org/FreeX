namespace FreeW.Core.Model.Tests;

public class DocumentIndexTests
{
    [Fact]
    public void Build_EmptyDocument_YieldsNoIndexResultParagraphs()
    {
        var doc = new TextDocument();

        var index = DocumentIndex.Build(doc);

        index.Should().BeEmpty();
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
            "A",
            "alpha, 1",
            "Z",
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
            "A",
            "Alpha, iv, 1",
            "B",
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
    public void Build_HierarchicalXeMarksEmitIndentedSubentriesAndCrossReferenceWithoutPage()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph { Runs = { new Run("Cats"), DocumentIndex.MarkRun(new IndexMark("Animals", "Cats")) } });
        doc.Blocks.Add(new Paragraph { Runs = { new Run("Dogs"), DocumentIndex.MarkRun(new IndexMark("Animals", "Dogs")) } });
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Transport"),
                DocumentIndex.MarkRun(new IndexMark("Transportation", CrossReference: "See Vehicles"))
            }
        });

        var index = DocumentIndex.Build(doc, _ => "1");

        index.Select(paragraph => paragraph.PlainText).Should().Equal(
            "A",
            "Animals",
            "Cats, 1",
            "Dogs, 1",
            "T",
            "Transportation. See Vehicles");
        index[1].Formatting.Should().Match<ParagraphFormatting>(format =>
            format.IndentLeftPt == 12 && format.FirstLineIndentPt == -12);
        index[2].Formatting.Should().Match<ParagraphFormatting>(format =>
            format.IndentLeftPt == 24 && format.FirstLineIndentPt == -12);
        index[5].PlainText.Should().NotContain(", 1");
    }

    [Fact]
    public void MarkRun_SerializesAndParsesSubentryAndCrossReference()
    {
        var run = DocumentIndex.MarkRun(new IndexMark(
            "  Animals  ",
            " Cats:Longhair ",
            " See Pet care "));

        run.ComplexField!.Instruction.Should().Be(" XE \"Animals:Cats:Longhair\" \\t \"See Pet care\" ");
        DocumentIndex.MarkedEntry(run).Should().Be(new IndexMark(
            "Animals",
            "Cats:Longhair",
            "See Pet care"));
        DocumentIndex.MarkedTerm(run).Should().Be("Animals:Cats:Longhair");
    }

    [Fact]
    public void Build_PageNumberRunMergesBoldAndItalicFormattingForSamePage()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Alpha"),
                DocumentIndex.MarkRun(new IndexMark("Alpha", BoldPageNumber: true)),
                DocumentIndex.MarkRun(new IndexMark("Alpha", ItalicPageNumber: true))
            }
        });

        var entry = DocumentIndex.Build(doc).Single(paragraph => paragraph.PlainText == "Alpha, 1");

        entry.Runs.Select(run => run.Text).Should().Equal("Alpha", ", ", "1");
        entry.Runs[0].Formatting.Bold.Should().BeFalse();
        entry.Runs[1].Formatting.Bold.Should().BeFalse();
        entry.Runs[2].Formatting.Bold.Should().BeTrue();
        entry.Runs[2].Formatting.Italic.Should().BeTrue();
    }

    [Fact]
    public void MarkRun_SerializesAndParsesPageNumberFormattingSwitches()
    {
        var run = DocumentIndex.MarkRun(new IndexMark(
            "Alpha",
            BoldPageNumber: true,
            ItalicPageNumber: true));

        run.ComplexField!.Instruction.Should().Be(" XE \"Alpha\" \\b \\i ");
        DocumentIndex.MarkedEntry(run).Should().Be(new IndexMark(
            "Alpha",
            BoldPageNumber: true,
            ItalicPageNumber: true));
    }

    [Fact]
    public void MarkRun_SerializesAndParsesBookmarkPageRangeSwitch()
    {
        var run = DocumentIndex.MarkRun(new IndexMark(
            "Alpha",
            BoldPageNumber: true,
            BookmarkName: "TopicRange"));

        run.ComplexField!.Instruction.Should().Be(" XE \"Alpha\" \\r \"TopicRange\" \\b ");
        DocumentIndex.MarkedEntry(run).Should().Be(new IndexMark(
            "Alpha",
            BoldPageNumber: true,
            BookmarkName: "TopicRange"));
    }

    [Fact]
    public void MarkRun_SerializesAndParsesAlternateIndexIdentifier()
    {
        var run = DocumentIndex.MarkRun(new IndexMark(
            "Alpha",
            BoldPageNumber: true,
            Identifier: "People"));

        run.ComplexField!.Instruction.Should().Be(" XE \"Alpha\" \\f \"People\" \\b ");
        DocumentIndex.MarkedEntry(run).Should().Be(new IndexMark(
            "Alpha",
            BoldPageNumber: true,
            Identifier: "People"));
    }

    [Fact]
    public void Build_FiltersAlternateIdentifiersAndTreatsIAsDefault()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph { Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha")) } });
        doc.Blocks.Add(new Paragraph { Runs = { DocumentIndex.MarkRun(new IndexMark("Beta", Identifier: "I")) } });
        doc.Blocks.Add(new Paragraph { Runs = { DocumentIndex.MarkRun(new IndexMark("Carol", Identifier: "People")) } });
        doc.Blocks.Add(new Paragraph { Runs = { DocumentIndex.MarkRun(new IndexMark("Dave", Identifier: "people")) } });
        doc.Blocks.Add(new Paragraph { Runs = { DocumentIndex.MarkRun(new IndexMark("Rome", Identifier: "Places")) } });
        doc.IndexEntries.Add(new IndexEntry("Zebra"));

        DocumentIndex.Build(doc).Select(paragraph => paragraph.PlainText).Should().Equal(
            "A", "Alpha, 1", "B", "Beta, 1", "Z", "Zebra, 1");
        DocumentIndex.Build(doc, identifier: "People").Select(paragraph => paragraph.PlainText).Should().Equal(
            "C", "Carol, 1", "D", "Dave, 1");
        DocumentIndex.Build(doc, identifier: "Places").Select(paragraph => paragraph.PlainText).Should().Equal(
            "R", "Rome, 1");
    }

    [Fact]
    public void AlternateIndexStylesIdentifyOnlyTheirOwnGeneratedRegion()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Add(new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Ada", Identifier: "People")) }
        });
        DocumentIndex.EnsureStyles(doc, "People");
        var paragraphs = DocumentIndex.Build(doc, identifier: "People");

        var headingStyle = DocumentIndex.HeadingStyleIdFor("People");
        var entryStyle = DocumentIndex.EntryStyleIdFor("People");
        doc.Styles.Should().ContainKey(headingStyle);
        doc.Styles.Should().ContainKey(entryStyle);
        paragraphs[0].StyleId.Should().Be(headingStyle);
        DocumentIndex.IsIndexParagraph(paragraphs[0]).Should().BeTrue();
        DocumentIndex.IsIndexParagraph(paragraphs[0], "People").Should().BeTrue();
        DocumentIndex.IsIndexParagraph(paragraphs[0], "Places").Should().BeFalse();
        DocumentIndex.IsIndexParagraph(paragraphs[0], null).Should().BeFalse();
        DocumentIndex.HeadingStyleIdFor("people").Should().Be(headingStyle);
    }

    [Fact]
    public void Build_WordDefaultGroupsSymbolsDigitsAndEnglishDiacriticsLikeWord()
    {
        var doc = new TextDocument();
        foreach (var term in new[] { "1alpha", "!bang", "Éclair", "Zulu" })
        {
            doc.Blocks.Add(new Paragraph
            {
                Runs = { DocumentIndex.MarkRun(term) }
            });
        }

        DocumentIndex.Build(doc).Select(paragraph => paragraph.PlainText).Should().Equal(
            "!", "!bang, 1",
            "1", "1alpha, 1",
            "E", "Éclair, 1",
            "Z", "Zulu, 1");
    }

    [Fact]
    public void Build_LegacyOptionsRetainSyntheticTitleWithoutAlphabeticHeadings()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph { Runs = { DocumentIndex.MarkRun("Alpha") } });

        DocumentIndex.Build(doc, options: IndexBuildOptions.LegacyTitleOnly)
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("Index", "Alpha, 1");
    }

    [Fact]
    public void Build_BookmarkPageRangeUsesFirstAndLastLogicalPageLabels()
    {
        var doc = new TextDocument();
        var start = new Paragraph("Range start");
        start.BookmarkNames.Add("TopicRange");
        start.BookmarkBoundaries.Add(new BookmarkBoundary(
            "42", BookmarkBoundaryKind.Start, 0, "TopicRange"));
        doc.Blocks.Add(start);
        doc.Blocks.Add(new Paragraph("Range middle"));
        var end = new Paragraph
        {
            Runs =
            {
                new Run("Range end"),
                DocumentIndex.MarkRun(new IndexMark(
                    "Alpha",
                    ItalicPageNumber: true,
                    BookmarkName: "TopicRange"))
            }
        };
        end.BookmarkBoundaries.Add(new BookmarkBoundary("42", BookmarkBoundaryKind.End, 0));
        doc.Blocks.Add(end);

        var entry = DocumentIndex.Build(doc, blockIndex => blockIndex switch
        {
            0 => "iv",
            2 => "vi",
            _ => null
        }).Single(paragraph => paragraph.PlainText == "Alpha, iv\u2013vi");

        entry.Runs.Select(run => run.Text).Should().Equal("Alpha", ", ", "iv\u2013vi");
        entry.Runs[^1].Formatting.Italic.Should().BeTrue();
    }

    [Fact]
    public void MarkAllTargets_FindWholeTermParagraphsAndSkipGeneratedOrExistingMarks()
    {
        var mark = new IndexMark("Alpha", "Topic");
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Alpha starts Alpha"));
        doc.Blocks.Add(new Paragraph("alphabet is not the term"));
        doc.Blocks.Add(new Paragraph("Another ALPHA appears"));
        doc.Blocks.Add(new Paragraph("Alpha, 1") { StyleId = DocumentIndex.EntryStyleId });
        doc.Blocks.Add(new Paragraph
        {
            Runs = { new Run("Alpha"), DocumentIndex.MarkRun(mark), new Run(" already marked") }
        });

        DocumentIndex.MarkAllTargets(doc, " alpha ", mark).Should().Equal(
            new IndexMarkTarget(0, 5),
            new IndexMarkTarget(0, 18),
            new IndexMarkTarget(2, 13));
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
