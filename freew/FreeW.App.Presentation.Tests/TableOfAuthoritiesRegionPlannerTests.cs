using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class TableOfAuthoritiesRegionPlannerTests
{
    [Fact]
    public void BuildInsertPlan_ClampsInsertIndexAndBuildsParagraphsWithStyles()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Body"));
        document.Citations.Add(new Citation("Brown v. Board", CitationCategory.Cases));

        var plan = TableOfAuthoritiesRegionPlanner.BuildInsertPlan(document, insertAt: 99);

        plan.DeleteIndicesDescending.Should().BeEmpty();
        plan.InsertIndex.Should().Be(1);
        plan.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Table of Authorities", "Cases", "Brown v. Board");
        document.Styles.Should().ContainKey(TableOfAuthorities.EntryStyleId);
    }

    [Fact]
    public void BuildInsertPlan_UsesSharedExplicitBreakPageReferencePlan()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Intro"));
        document.Blocks.Add(CitationMarkParagraph("Brown v. Board"));
        document.Blocks.Add(DocumentOps.CreatePageBreak());
        document.Blocks.Add(CitationMarkParagraph("Brown v. Board"));

        var plan = TableOfAuthoritiesRegionPlanner.BuildInsertPlan(document, insertAt: 1);

        plan.DeleteIndicesDescending.Should().BeEmpty();
        plan.InsertIndex.Should().Be(1);
        plan.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Table of Authorities", "Cases", "Brown v. Board\t1, 2");
        plan.Paragraphs.Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId)
            .Runs.Select(run => run.Text).Should().Equal("Brown v. Board", "\t", "1, 2");
    }

    [Fact]
    public void BuildInsertPlan_PassesHostPageResolverIntoSharedTableBuilder()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(CitationMarkParagraph("Brown v. Board"));
        document.Blocks.Add(new Paragraph("Overflow body"));
        document.Blocks.Add(CitationMarkParagraph("Brown v. Board"));

        var plan = TableOfAuthoritiesRegionPlanner.BuildInsertPlan(
            document,
            insertAt: 1,
            pageResolver: (_, blockIndex, _, _) => new ToaCitationPageReference(
                blockIndex == 2 ? 2 : 1,
                blockIndex == 2 ? "2" : "1"));

        plan.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Table of Authorities", "Cases", "Brown v. Board\t1, 2");
    }

    [Fact]
    public void BuildInsertPlanWithTableAddresses_ForwardsNestedCellAddress()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var outer = Table.Create(1, 1);
        var nested = Table.Create(1, 1);
        nested.Rows[0].Cells[0].Paragraphs[0] = CitationMarkParagraph("Brown v. Board");
        outer.Rows[0].Cells[0].NestedTables.Add(nested);
        document.Blocks.Add(outer);

        TableParagraphAddress? requestedAddress = null;
        var plan = TableOfAuthoritiesRegionPlanner.BuildInsertPlanWithTableAddresses(
            document,
            insertAt: 1,
            pageResolver: (_, _, tableParagraph, _, _) =>
            {
                requestedAddress = tableParagraph;
                return TableOfAuthorities.CreatePageReference(2);
            });

        requestedAddress.Should().Be(new TableParagraphAddress(
            0,
            0,
            ParagraphIndex: -1,
            NestedTableIndex: 0,
            NestedParagraph: new TableParagraphAddress(0, 0, 0)));
        plan.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Table of Authorities", "Cases", "Brown v. Board\t2");
    }

    [Fact]
    public void BuildRefreshPlan_WithExistingRegion_DeletesGeneratedParagraphsDescendingAndReusesFirstPosition()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Before"));
        document.Blocks.AddRange(TableOfAuthorities.Build(
            new[] { new Citation("Old Case", CitationCategory.Cases) }));
        document.Blocks.Add(new Paragraph("After"));
        document.Citations.Add(new Citation("New Case", CitationCategory.Cases));

        var plan = TableOfAuthoritiesRegionPlanner.BuildRefreshPlan(document);

        plan.DeleteIndicesDescending.Should().Equal(3, 2, 1);
        plan.InsertIndex.Should().Be(1);
        plan.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Table of Authorities", "Cases", "New Case");
    }

    [Fact]
    public void ContainsRegion_ReturnsTrueForExistingGeneratedTableOfAuthoritiesParagraphs()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Body"));

        TableOfAuthoritiesRegionPlanner.ContainsRegion(document).Should().BeFalse();

        document.Blocks.AddRange(TableOfAuthorities.Build(
            new[] { new Citation("Old Case", CitationCategory.Cases) }));

        TableOfAuthoritiesRegionPlanner.ContainsRegion(document).Should().BeTrue();
    }

    [Fact]
    public void BuildRefreshPlan_ReplacesExistingRegionWithSharedPageReferencesAtSameLocation()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Before"));
        document.Blocks.Add(CitationMarkParagraph("New Case"));
        document.Blocks.Add(DocumentOps.CreatePageBreak());
        document.Blocks.Add(CitationMarkParagraph("New Case"));
        document.Blocks.AddRange(TableOfAuthorities.Build(
            new[] { new Citation("Old Case", CitationCategory.Cases) }));
        document.Blocks.Add(new Paragraph("After"));

        var plan = TableOfAuthoritiesRegionPlanner.BuildRefreshPlan(document);

        plan.DeleteIndicesDescending.Should().Equal(6, 5, 4);
        plan.InsertIndex.Should().Be(4);
        plan.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Table of Authorities", "Cases", "New Case\t1, 2");
    }

    [Fact]
    public void BuildRefreshPlan_PassesHostPageResolverIntoSharedTableBuilder()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Before"));
        document.Blocks.Add(CitationMarkParagraph("New Case"));
        document.Blocks.Add(new Paragraph("Overflow body"));
        document.Blocks.Add(CitationMarkParagraph("New Case"));
        document.Blocks.AddRange(TableOfAuthorities.Build(
            new[] { new Citation("Old Case", CitationCategory.Cases) }));

        var plan = TableOfAuthoritiesRegionPlanner.BuildRefreshPlan(
            document,
            pageResolver: (_, blockIndex, _, _) => new ToaCitationPageReference(
                blockIndex == 3 ? 2 : 1,
                blockIndex == 3 ? "2" : "1"));

        plan.DeleteIndicesDescending.Should().Equal(6, 5, 4);
        plan.InsertIndex.Should().Be(4);
        plan.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Table of Authorities", "Cases", "New Case\t1, 2");
    }

    [Fact]
    public void BuildRefreshPlan_WithoutExistingRegion_InsertsAtDocumentEnd()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Intro"));
        document.Blocks.Add(new Paragraph("Body"));
        document.Citations.Add(new Citation("Roe v. Wade", CitationCategory.Cases));

        var plan = TableOfAuthoritiesRegionPlanner.BuildRefreshPlan(document);

        plan.DeleteIndicesDescending.Should().BeEmpty();
        plan.InsertIndex.Should().Be(2);
        plan.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Table of Authorities", "Cases", "Roe v. Wade");
    }

    [Fact]
    public void BuildRefreshPlan_CarriesOptionsIntoGeneratedParagraphs()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Citations.Add(new Citation("Case A", CitationCategory.Cases));
        document.Citations.Add(new Citation("17 U.S.C. 107", CitationCategory.Statutes));

        var plan = TableOfAuthoritiesRegionPlanner.BuildRefreshPlan(
            document,
            new ToaOptions
            {
                CategoryFilter = CitationCategory.Statutes,
                TabLeader = ToaTabLeader.None
            });

        plan.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Table of Authorities", "Statutes", "17 U.S.C. 107");
        plan.Paragraphs.Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId)
            .Formatting.TabStops.Should().ContainSingle()
            .Which.Leader.Should().Be(TabLeader.None);
    }

    [Fact]
    public void BuildRefreshPlan_WithoutExplicitOptions_PreservesImportedNativeFieldOptions()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Citations.Add(new Citation("Case A", CitationCategory.Cases));
        document.Citations.Add(new Citation("17 U.S.C. 107", CitationCategory.Statutes));
        document.Blocks.Add(new Paragraph("Old statute\t1")
        {
            StyleId = "Normal",
            SpanningFieldOwner = new ComplexField(" TOA \\h \\c \"2\" \\p "),
            Formatting = ParagraphFormatting.Default with
            {
                TabStops = [new TabStop(468, TabStopAlignment.Right, TabLeader.Dashes)]
            }
        });

        var plan = TableOfAuthoritiesRegionPlanner.BuildRefreshPlan(document);

        plan.DeleteIndicesDescending.Should().Equal(0);
        plan.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Table of Authorities", "Statutes", "17 U.S.C. 107");
        plan.Paragraphs[1].SpanningFieldStart!.Instruction
            .Should().Be(" TOA \\h \\c \"2\" \\p ");
        plan.Paragraphs.Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId)
            .Formatting.TabStops.Should().ContainSingle()
            .Which.Leader.Should().Be(TabLeader.Dashes);
    }

    [Fact]
    public void BuildRefreshPlan_ProducesWordLikeRenderedEntryMetadata()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Page.WidthPt = 700;
        document.Page.MarginLeftPt = 80;
        document.Page.MarginRightPt = 90;

        for (var i = 0; i < 5; i++)
        {
            var mark = Run.CitationMark(new Citation("Roe v. Wade", CitationCategory.Cases));
            if (i == 0)
                mark.Formatting = new RunFormatting { Bold = true, Underline = true, ColorHex = "#C00000" };
            document.Blocks.Add(new Paragraph { Runs = { mark } });
        }

        document.Blocks.AddRange(TableOfAuthorities.Build(
            new[] { new Citation("Old Case", CitationCategory.Cases) }));

        var plan = TableOfAuthoritiesRegionPlanner.BuildRefreshPlan(
            document,
            new ToaOptions
            {
                CategoryFilter = CitationCategory.Cases,
                KeepOriginalFormatting = true,
                UsePassim = true,
                TabLeader = ToaTabLeader.Dashes
            });

        plan.DeleteIndicesDescending.Should().Equal(7, 6, 5);
        plan.InsertIndex.Should().Be(5);
        plan.Paragraphs.Select(paragraph => paragraph.StyleId)
            .Should().Equal(
                TableOfAuthorities.HeadingStyleId,
                TableOfAuthorities.CategoryStyleId,
                TableOfAuthorities.EntryStyleId);
        plan.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Table of Authorities", "Cases", "Roe v. Wade passim");

        var entry = plan.Paragraphs.Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId);
        entry.Formatting.TabStops.Should().Equal(
            new TabStop(530, TabStopAlignment.Right, TabLeader.Dashes));
        entry.Runs.Single().Formatting.Should().Be(new RunFormatting
        {
            Bold = true,
            Underline = true,
            ColorHex = "#C00000"
        });

        document.Styles.Should().ContainKeys(
            TableOfAuthorities.HeadingStyleId,
            TableOfAuthorities.CategoryStyleId,
            TableOfAuthorities.EntryStyleId);
    }

    private static Paragraph CitationMarkParagraph(string longCitation)
    {
        var mark = Run.CitationMark(new Citation(longCitation, CitationCategory.Cases));
        return new Paragraph { Runs = { mark } };
    }
}
