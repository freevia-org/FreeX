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
}
