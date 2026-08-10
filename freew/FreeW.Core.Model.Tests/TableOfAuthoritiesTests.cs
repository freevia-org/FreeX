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
    public void Build_AuthorsOneNativeToaOwnerAroundCategoryAndEntryResults()
    {
        var table = TableOfAuthorities.Build(
            new[] { new Citation("Some Case", CitationCategory.Cases) });

        table[0].SpanningFieldOwner.Should().BeNull();
        table.Skip(1).Should().OnlyContain(paragraph =>
            paragraph.SpanningFieldOwner != null
            && paragraph.SpanningFieldOwner.Instruction == " TOA \\h \\c \"1\" \\f ");
        table[1].SpanningFieldStart!.Instruction.Should().Be(" TOA \\h \\c \"1\" \\f ");
        table[^1].EndsSpanningField.Should().BeTrue();
    }

    [Fact]
    public void NativeFieldInstructionAndExistingOptions_MapWordSwitchSemantics()
    {
        var options = new ToaOptions
        {
            CategoryFilter = CitationCategory.Statutes,
            UsePassim = true,
            KeepOriginalFormatting = true,
            TabLeader = ToaTabLeader.Dashes
        };
        TableOfAuthorities.NativeFieldInstructionFor(options, CitationCategory.Statutes)
            .Should().Be(" TOA \\h \\c \"2\" \\p ");

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Statute A\t1")
        {
            StyleId = TableOfAuthorities.EntryStyleId,
            SpanningFieldOwner = new ComplexField(" TOA \\h \\c \"2\" \\p "),
            Formatting = ParagraphFormatting.Default with
            {
                TabStops = [new TabStop(468, TabStopAlignment.Right, TabLeader.Dashes)]
            }
        });

        var imported = TableOfAuthorities.ExistingOptions(document);
        imported.Should().NotBeNull();
        imported!.CategoryFilter.Should().Be(CitationCategory.Statutes);
        imported.UsePassim.Should().BeTrue();
        imported.KeepOriginalFormatting.Should().BeTrue();
        imported.TabLeader.Should().Be(ToaTabLeader.Dashes);

        document.Blocks.Add(new Paragraph("Case A\t1")
        {
            SpanningFieldOwner = new ComplexField(" TOA \\h \\c \"1\" \\p ")
        });
        TableOfAuthorities.ExistingOptions(document)!.CategoryFilter.Should().BeNull(
            "Word represents an all-category insertion as one native field per used category");
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
    public void BuildWithTableAddresses_CollectsDirectAndNestedCellMarksInSerializedStoryOrder()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(CitationMarkParagraph("Case A"));

        var outer = Table.Create(1, 1);
        var nested = Table.Create(1, 1);
        nested.Rows[0].Cells[0].Paragraphs[0] = CitationMarkParagraph("Case A");
        outer.Rows[0].Cells[0].NestedTables.Add(nested);
        outer.Rows[0].Cells[0].Paragraphs[0] = CitationMarkParagraph("Case A");
        document.Blocks.Add(outer);
        document.Blocks.Add(CitationMarkParagraph("Case A"));

        var requests = new List<(int BlockIndex, TableParagraphAddress? TableParagraph)>();
        var result = TableOfAuthorities.BuildWithTableAddresses(
            document,
            ToaOptions.Default,
            (_, blockIndex, tableParagraph, _, _) =>
            {
                requests.Add((blockIndex, tableParagraph));
                return TableOfAuthorities.CreatePageReference(requests.Count);
            });

        result.Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId)
            .PlainText.Should().Be("Case A\t1, 2, 3, 4");
        requests.Should().Equal(
            (0, null),
            (1, new TableParagraphAddress(
                0,
                0,
                ParagraphIndex: -1,
                NestedTableIndex: 0,
                NestedParagraph: new TableParagraphAddress(0, 0, 0))),
            (1, new TableParagraphAddress(0, 0, 0)),
            (2, null));
        TableOfAuthorities.CollectCitations(document)
            .Select(citation => citation.LongCitation)
            .Should().Equal("Case A", "Case A", "Case A", "Case A");
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
        TableOfAuthorities.IsTableOfAuthoritiesParagraph(new Paragraph("x")
        {
            StyleId = "Normal",
            SpanningFieldOwner = new ComplexField(" TOA \\h \\c \"1\" ")
        }).Should().BeTrue();
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

    [Fact]
    public void Build_UniquePriorShortCitationAliasCollapsesIntoLongCitation()
    {
        var citations = new[]
        {
            new Citation("Brown v. Board of Education, 347 U.S. 483 (1954)", CitationCategory.Cases, "Brown"),
            new Citation("Brown", CitationCategory.Cases)
        };

        var table = TableOfAuthorities.Build(citations).Select(p => p.PlainText).ToList();

        table.Should().Equal(
            TableOfAuthorities.HeadingText,
            "Cases",
            "Brown v. Board of Education, 347 U.S. 483 (1954)");
    }

    [Fact]
    public void Build_AmbiguousShortCitationAliasDoesNotMergeAuthorities()
    {
        var citations = new[]
        {
            new Citation("Alpha v. One, 1 U.S. 1", CitationCategory.Cases, "Signal"),
            new Citation("Beta v. Two, 2 U.S. 2", CitationCategory.Cases, "Signal"),
            new Citation("Signal", CitationCategory.Cases)
        };

        var table = TableOfAuthorities.Build(citations).Select(p => p.PlainText).ToList();

        table.Should().Equal(
            TableOfAuthorities.HeadingText,
            "Cases",
            "Alpha v. One, 1 U.S. 1",
            "Beta v. Two, 2 U.S. 2",
            "Signal");
    }

    [Fact]
    public void Build_ShortCitationAliasPreservesCategoryBoundaries()
    {
        var citations = new[]
        {
            new Citation("Case Long", CitationCategory.Cases, "Shared"),
            new Citation("Shared", CitationCategory.Cases),
            new Citation("Shared", CitationCategory.Statutes)
        };

        var table = TableOfAuthorities.Build(citations).Select(p => p.PlainText).ToList();

        table.Should().Equal(
            TableOfAuthorities.HeadingText,
            "Cases",
            "Case Long",
            "Statutes",
            "Shared");
    }

    // --- ToaOptions depth -----------------------------------------------------------------------

    [Fact]
    public void ToaOptions_Default_HasSaneValues()
    {
        var opts = ToaOptions.Default;
        opts.UsePassim.Should().BeFalse();
        opts.KeepOriginalFormatting.Should().BeFalse();
        opts.CategoryFilter.Should().BeNull();
        opts.TabLeader.Should().Be(ToaTabLeader.Dots);
    }

    [Fact]
    public void Build_WithDefaultOptions_MatchesBuildWithNoCitations()
    {
        var citations = new[]
        {
            new Citation("Roe v. Wade", CitationCategory.Cases),
            new Citation("42 U.S.C. § 1983", CitationCategory.Statutes)
        };

        var withDefault = TableOfAuthorities.Build(citations, ToaOptions.Default)
            .Select(p => p.PlainText).ToList();
        var withoutOptions = TableOfAuthorities.Build(citations)
            .Select(p => p.PlainText).ToList();

        withDefault.Should().Equal(withoutOptions);
    }

    [Fact]
    public void Build_CategoryFilter_EmitsOnlyThatCategory()
    {
        var citations = new[]
        {
            new Citation("Brown v. Board", CitationCategory.Cases),
            new Citation("17 U.S.C. § 107", CitationCategory.Statutes),
            new Citation("Fed. R. Civ. P. 12", CitationCategory.Rules)
        };

        var opts = new ToaOptions { CategoryFilter = CitationCategory.Statutes };
        var table = TableOfAuthorities.Build(citations, opts).Select(p => p.PlainText).ToList();

        // Only the heading + Statutes category heading + Statutes entry: no Cases, no Rules.
        table.Should().Equal(
            TableOfAuthorities.HeadingText,
            "Statutes",
            "17 U.S.C. § 107");
        table.Should().NotContain("Cases");
        table.Should().NotContain("Rules");
    }

    [Fact]
    public void Build_CategoryFilter_WhenFilteredCategoryHasNoCitations_AuthorsNativeEmptyResult()
    {
        var citations = new[] { new Citation("Brown v. Board", CitationCategory.Cases) };

        var opts = new ToaOptions { CategoryFilter = CitationCategory.Statutes };
        var table = TableOfAuthorities.Build(citations, opts);

        table.Select(paragraph => paragraph.PlainText).Should().Equal(
            TableOfAuthorities.HeadingText,
            TableOfAuthorities.EmptyResultText);
        table[1].Runs.Should().ContainSingle();
        table[1].Runs[0].ComplexField!.Instruction.Should().Be(" TOA \\h \\c \"2\" \\f ");
        TableOfAuthorities.IsTableOfAuthoritiesParagraph(table[1]).Should().BeTrue();
    }

    [Fact]
    public void Build_UsePassim_WithoutPageEvidenceKeepsLegacyOccurrenceFallback()
    {
        // A citation that appears 5 times must get " passim" appended.
        var citation = new Citation("Brown v. Board", CitationCategory.Cases);
        var citations = Enumerable.Repeat(citation, 5).ToList();

        var opts = new ToaOptions { UsePassim = true };
        // Use the document overload so occurrence counting works.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        foreach (var c in citations)
            paragraph.Runs.Add(Run.CitationMark(c));
        doc.Blocks.Add(paragraph);

        var table = TableOfAuthorities.Build(doc, opts).Select(p => p.PlainText).ToList();

        table.Should().ContainSingle(t => t.Contains(" passim"),
            "an entry appearing 5 times must be annotated with passim");
    }

    [Fact]
    public void Build_UsePassim_NoSuffixWhenFewerThanFiveOccurrences()
    {
        var citation = new Citation("Brown v. Board", CitationCategory.Cases);
        var citations = Enumerable.Repeat(citation, 4).ToList();

        var opts = new ToaOptions { UsePassim = true };
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        foreach (var c in citations)
            paragraph.Runs.Add(Run.CitationMark(c));
        doc.Blocks.Add(paragraph);

        var table = TableOfAuthorities.Build(doc, opts).Select(p => p.PlainText).ToList();

        table.Should().NotContain(t => t.Contains(" passim"),
            "fewer than 5 occurrences must not be annotated with passim");
    }

    [Fact]
    public void Build_UsePassim_False_NeverAnnotatesEvenWithManyOccurrences()
    {
        // When UsePassim is off, even 10 occurrences must produce no passim suffix.
        var citations = Enumerable.Repeat(new Citation("Case X", CitationCategory.Cases), 10);
        var opts = new ToaOptions { UsePassim = false };

        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        foreach (var c in citations)
            paragraph.Runs.Add(Run.CitationMark(c));
        doc.Blocks.Add(paragraph);

        var table = TableOfAuthorities.Build(doc, opts).Select(p => p.PlainText).ToList();
        table.Should().NotContain(t => t.Contains(" passim"));
    }

    [Fact]
    public void ToaOptions_TabLeader_CanBeSet()
    {
        var opts = new ToaOptions { TabLeader = ToaTabLeader.Dashes };
        opts.TabLeader.Should().Be(ToaTabLeader.Dashes);
    }

    [Fact]
    public void Build_DefaultOptions_AddsRightAlignedDottedLeaderTabStopToEntries()
    {
        var table = TableOfAuthorities.Build(new[] { new Citation("Case A", CitationCategory.Cases) });

        var entry = table.Single(p => p.StyleId == TableOfAuthorities.EntryStyleId);

        entry.Formatting.TabStops.Should().Equal(
            new TabStop(
                TableOfAuthorities.DefaultEntryRightTabStopPt,
                TabStopAlignment.Right,
                TabLeader.Dots));
    }

    [Fact]
    public void Build_TabLeaderOption_CarriesSelectedLeaderOnEntryTabStop()
    {
        var table = TableOfAuthorities.Build(
            new[] { new Citation("Case A", CitationCategory.Cases) },
            new ToaOptions { TabLeader = ToaTabLeader.Underline });

        table.Single(p => p.StyleId == TableOfAuthorities.EntryStyleId)
            .Formatting.TabStops.Should().Equal(
                new TabStop(
                    TableOfAuthorities.DefaultEntryRightTabStopPt,
                    TabStopAlignment.Right,
                    TabLeader.Underline));
    }

    [Fact]
    public void Build_FromDocument_UsesWritablePageWidthForEntryTabStop()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Page.WidthPt = 720;
        doc.Page.MarginLeftPt = 90;
        doc.Page.MarginRightPt = 54;
        doc.Citations.Add(new Citation("Case A", CitationCategory.Cases));

        var table = TableOfAuthorities.Build(doc, new ToaOptions { TabLeader = ToaTabLeader.Dashes });

        table.Single(p => p.StyleId == TableOfAuthorities.EntryStyleId)
            .Formatting.TabStops.Should().Equal(
                new TabStop(576, TabStopAlignment.Right, TabLeader.Dashes));
    }

    [Fact]
    public void Build_KeepOriginalFormatting_CopiesFirstBodyMarkRunFormatting()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var mark = Run.CitationMark(new Citation("Formatted Case", CitationCategory.Cases));
        mark.Formatting = new RunFormatting { Bold = true, Italic = true, ColorHex = "#C00000" };
        doc.Blocks.Add(new Paragraph { Runs = { mark } });

        var table = TableOfAuthorities.Build(doc, new ToaOptions { KeepOriginalFormatting = true });

        table.Single(p => p.StyleId == TableOfAuthorities.EntryStyleId)
            .Runs.Single().Formatting.Should().Be(mark.Formatting);
    }

    [Fact]
    public void Build_KeepOriginalFormatting_FalseLeavesEntryFormattingToStyle()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var mark = Run.CitationMark(new Citation("Formatted Case", CitationCategory.Cases));
        mark.Formatting = new RunFormatting { Bold = true };
        doc.Blocks.Add(new Paragraph { Runs = { mark } });

        var table = TableOfAuthorities.Build(doc, new ToaOptions { KeepOriginalFormatting = false });

        table.Single(p => p.StyleId == TableOfAuthorities.EntryStyleId)
            .Runs.Single().Formatting.Should().Be(RunFormatting.Default);
    }

    [Fact]
    public void Build_FromDocument_ExplicitPageBreaksAppendUniqueAscendingPageReferences()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(CitationMarkParagraph("Case A"));
        doc.Blocks.Add(DocumentOps.CreatePageBreak());
        doc.Blocks.Add(CitationMarkParagraph("Case A"));

        var entry = TableOfAuthorities.Build(doc)
            .Single(p => p.StyleId == TableOfAuthorities.EntryStyleId);

        entry.PlainText.Should().Be("Case A\t1, 2");
        entry.Runs.Select(run => run.Text).Should().Equal("Case A", "\t", "1, 2");
    }

    [Fact]
    public void Build_FromDocument_PageResolverSuppliesOverflowPageReferencesWithoutExplicitBreaks()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(CitationMarkParagraph("Case A"));
        doc.Blocks.Add(new Paragraph("Overflow body"));
        doc.Blocks.Add(CitationMarkParagraph("Case A"));

        var entry = TableOfAuthorities.Build(
                doc,
                ToaOptions.Default,
                (_, blockIndex, _, _) => new ToaCitationPageReference(
                    blockIndex == 2 ? 2 : 1,
                    blockIndex == 2 ? "2" : "1"))
            .Single(p => p.StyleId == TableOfAuthorities.EntryStyleId);

        entry.PlainText.Should().Be("Case A\t1, 2");
        entry.Runs.Select(run => run.Text).Should().Equal("Case A", "\t", "1, 2");
    }

    [Fact]
    public void Build_FromDocument_PageResolverPreservesDisplayTextAndDedupesPhysicalPages()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(CitationMarkParagraph("Case A"));
        doc.Blocks.Add(CitationMarkParagraph("Case A"));

        var entry = TableOfAuthorities.Build(
                doc,
                ToaOptions.Default,
                (_, _, _, _) => new ToaCitationPageReference(2, "ii"))
            .Single(p => p.StyleId == TableOfAuthorities.EntryStyleId);

        entry.PlainText.Should().Be("Case A\tii");
    }

    [Fact]
    public void Build_FromDocument_BlockPageAssignmentAdapterSuppliesPageReferences()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(CitationMarkParagraph("Case A"));
        doc.Blocks.Add(new Paragraph("Middle"));
        doc.Blocks.Add(CitationMarkParagraph("Case A"));

        var entry = TableOfAuthorities.Build(doc, new[] { 0, 0, 1 })
            .Single(p => p.StyleId == TableOfAuthorities.EntryStyleId);

        entry.PlainText.Should().Be("Case A\t1, 2");
    }

    [Fact]
    public void Build_FromDocument_ShortCitationAliasAggregatesPageReferences()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(CitationMarkParagraph(
            "Brown v. Board of Education, 347 U.S. 483 (1954)",
            shortCitation: "Brown"));
        doc.Blocks.Add(DocumentOps.CreatePageBreak());
        doc.Blocks.Add(CitationMarkParagraph("Brown"));

        var entry = TableOfAuthorities.Build(doc)
            .Single(p => p.StyleId == TableOfAuthorities.EntryStyleId);

        entry.PlainText.Should().Be("Brown v. Board of Education, 347 U.S. 483 (1954)\t1, 2");
        entry.Runs.Select(run => run.Text).Should().Equal(
            "Brown v. Board of Education, 347 U.S. 483 (1954)",
            "\t",
            "1, 2");
    }

    [Fact]
    public void Build_FromDocument_UsePassimCountsCanonicalShortCitationAlias()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(CitationMarkParagraph(
            "National Federation of Independent Business v. Sebelius, 567 U.S. 519 (2012)",
            shortCitation: "NFIB"));
        for (var i = 0; i < 4; i++)
        {
            doc.Blocks.Add(DocumentOps.CreatePageBreak());
            doc.Blocks.Add(CitationMarkParagraph("NFIB"));
        }

        var entry = TableOfAuthorities.Build(doc, new ToaOptions { UsePassim = true })
            .Single(p => p.StyleId == TableOfAuthorities.EntryStyleId);

        entry.PlainText.Should().Be(
            "National Federation of Independent Business v. Sebelius, 567 U.S. 519 (2012)\tpassim");
        entry.Runs.Select(run => run.Text).Should().Equal(
            "National Federation of Independent Business v. Sebelius, 567 U.S. 519 (2012)",
            "\t",
            "passim");
    }

    [Fact]
    public void Build_FromDocument_DuplicatesOnSameExplicitPageCollapsePageReference()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.CitationMark(new Citation("Case A", CitationCategory.Cases)));
        paragraph.Runs.Add(Run.CitationMark(new Citation("Case A", CitationCategory.Cases)));
        doc.Blocks.Add(paragraph);
        doc.Blocks.Add(DocumentOps.CreatePageBreak());

        var entry = TableOfAuthorities.Build(doc)
            .Single(p => p.StyleId == TableOfAuthorities.EntryStyleId);

        entry.PlainText.Should().Be("Case A\t1");
    }

    [Fact]
    public void Build_FromDocument_NoExplicitPageInformationKeepsTextOnlyEntry()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(CitationMarkParagraph("Case A"));

        var entry = TableOfAuthorities.Build(doc)
            .Single(p => p.StyleId == TableOfAuthorities.EntryStyleId);

        entry.PlainText.Should().Be("Case A");
        entry.Runs.Should().ContainSingle().Which.Text.Should().Be("Case A");
    }

    [Fact]
    public void Build_FromDocument_LiveSinglePageResolverSuppliesPageOneWithoutChangingFallback()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(CitationMarkParagraph("Case A"));

        TableOfAuthorities.Build(doc)
            .Single(p => p.StyleId == TableOfAuthorities.EntryStyleId)
            .PlainText.Should().Be("Case A");

        var entry = TableOfAuthorities.Build(
                doc,
                ToaOptions.Default,
                (_, _, _, _) => TableOfAuthorities.CreatePageReference(1))
            .Single(p => p.StyleId == TableOfAuthorities.EntryStyleId);

        entry.PlainText.Should().Be("Case A\t1");
        entry.Runs.Select(run => run.Text).Should().Equal("Case A", "\t", "1");
    }

    [Fact]
    public void Build_FromDocument_UsePassimRequiresFiveDistinctPageReferences()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(CitationMarkParagraph("Case A"));
        for (var i = 0; i < 4; i++)
        {
            doc.Blocks.Add(DocumentOps.CreatePageBreak());
            doc.Blocks.Add(CitationMarkParagraph("Case A"));
        }

        var entry = TableOfAuthorities.Build(doc, new ToaOptions { UsePassim = true })
            .Single(p => p.StyleId == TableOfAuthorities.EntryStyleId);

        entry.PlainText.Should().Be("Case A\tpassim");
        entry.Runs.Select(run => run.Text).Should().Equal("Case A", "\t", "passim");
    }

    [Fact]
    public void Build_FromDocument_UsePassimDoesNotCountFiveMarksOnOnePageAsPassim()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        for (var i = 0; i < 5; i++)
            doc.Blocks.Add(CitationMarkParagraph("Case A"));

        var entry = TableOfAuthorities.Build(
                doc,
                new ToaOptions { UsePassim = true },
                (_, _, _, _) => TableOfAuthorities.CreatePageReference(1))
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId);

        entry.PlainText.Should().Be("Case A\t1");
        entry.Runs.Select(run => run.Text).Should().Equal("Case A", "\t", "1");
    }

    [Fact]
    public void Build_KeepOriginalFormatting_WithPageReferencesAppliesOnlyToCitationTextRun()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var formatting = new RunFormatting { Bold = true, Italic = true, ColorHex = "#C00000" };
        doc.Blocks.Add(CitationMarkParagraph("Formatted Case", formatting: formatting));
        doc.Blocks.Add(DocumentOps.CreatePageBreak());
        doc.Blocks.Add(CitationMarkParagraph("Formatted Case"));

        var entry = TableOfAuthorities.Build(doc, new ToaOptions { KeepOriginalFormatting = true })
            .Single(p => p.StyleId == TableOfAuthorities.EntryStyleId);

        entry.PlainText.Should().Be("Formatted Case\t1, 2");
        entry.Runs.Should().HaveCount(3);
        entry.Runs[0].Formatting.Should().Be(formatting);
        entry.Runs[1].Formatting.Should().Be(RunFormatting.Default);
        entry.Runs[2].Formatting.Should().Be(RunFormatting.Default);
    }

    [Fact]
    public void Build_FromDocument_ManualPageBreakRunsAdvancePageReferencesWithinParagraph()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.CitationMark(new Citation("Case A", CitationCategory.Cases)));
        paragraph.Runs.Add(Run.PageBreak());
        paragraph.Runs.Add(Run.CitationMark(new Citation("Case A", CitationCategory.Cases)));
        doc.Blocks.Add(paragraph);

        var entry = TableOfAuthorities.Build(doc)
            .Single(p => p.StyleId == TableOfAuthorities.EntryStyleId);

        entry.PlainText.Should().Be("Case A\t1, 2");
    }

    [Theory]
    [InlineData(SectionBreakKind.NextPage, "1, 2")]
    [InlineData(SectionBreakKind.EvenPage, "1, 2")]
    [InlineData(SectionBreakKind.OddPage, "1, 3")]
    public void Build_FromDocument_PageSectionBreaksAdvancePageReferences(
        SectionBreakKind breakKind,
        string expectedPages)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(CitationMarkParagraph("Section Case"));
        doc.Blocks.Add(new Paragraph("Section end")
        {
            SectionBreak = new Section(new PageSettings(), breakKind)
        });
        doc.Blocks.Add(CitationMarkParagraph("Section Case"));

        var entry = TableOfAuthorities.Build(doc)
            .Single(p => p.StyleId == TableOfAuthorities.EntryStyleId);

        entry.PlainText.Should().Be($"Section Case\t{expectedPages}");
    }

    [Fact]
    public void Build_WithCitationsAndOptions_CategoryFilter_FromEnumerableOverload()
    {
        // The IEnumerable<Citation> overload that takes ToaOptions also respects CategoryFilter.
        var citations = new[]
        {
            new Citation("Case A", CitationCategory.Cases),
            new Citation("Statute B", CitationCategory.Statutes)
        };

        var opts = new ToaOptions { CategoryFilter = CitationCategory.Cases };
        var table = TableOfAuthorities.Build(citations, opts).Select(p => p.PlainText).ToList();

        table.Should().Equal(TableOfAuthorities.HeadingText, "Cases", "Case A");
        table.Should().NotContain("Statutes");
    }

    private static Paragraph CitationMarkParagraph(
        string longCitation,
        CitationCategory category = CitationCategory.Cases,
        RunFormatting? formatting = null,
        string? shortCitation = null)
    {
        var mark = Run.CitationMark(new Citation(longCitation, category, shortCitation));
        if (formatting is not null)
            mark.Formatting = formatting;
        return new Paragraph { Runs = { mark } };
    }
}
