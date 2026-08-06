using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class BibliographyRegionPlannerTests
{
    [Fact]
    public void BuildInsertPlan_ClampsInsertIndexAndBuildsStyledBibliography()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Body"));
        document.Sources.Add(new Source { Tag = "Sm24", Author = "Smith", Title = "A Work", Year = "2024" });

        var plan = BibliographyRegionPlanner.BuildInsertPlan(document, insertAt: 99);

        plan.DeleteIndicesDescending.Should().BeEmpty();
        plan.InsertIndex.Should().Be(1);
        plan.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("References", "Smith. (2024). A Work.");
        document.Styles.Should().ContainKey(Citations.EntryStyleId);
    }

    [Fact]
    public void BuildInsertPlan_UsesSharedBibliographyRoleFormatting()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Sources.Add(new Source
        {
            Type = SourceType.BookSection,
            Author = "Lee, S.",
            Title = "Citation Tools",
            BookTitle = "The Word Processor Handbook",
            Year = "2025",
            Editors =
            [
                SourceAuthorPerson.Create("Helen", string.Empty, "Ortiz")
            ],
            Translators =
            [
                SourceAuthorPerson.Create("Marco", string.Empty, "Silva")
            ]
        });

        var plan = BibliographyRegionPlanner.BuildInsertPlan(document, insertAt: 0);

        plan.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal(
                "References",
                "Lee, S. (2025). Citation Tools. The Word Processor Handbook, " +
                "Ed. Helen Ortiz, Trans. Marco Silva.");
    }

    [Fact]
    public void BuildInsertPlan_MarksGeneratedOutputAsSharedBibliographyBlockControl()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Sources.Add(new Source { Tag = "Sm24", Author = "Smith", Title = "A Work", Year = "2024" });

        var plan = BibliographyRegionPlanner.BuildInsertPlan(document, insertAt: 0);

        plan.BlockContentControl.Should().NotBeNull();
        plan.BlockContentControl!.Kind.Should().Be(BlockContentControlKind.Bibliography);
        plan.BlockContentControl.DocPartGallery.Should().Be(BlockContentControl.BibliographyGallery);
        plan.BlockContentControl.DocPartUnique.Should().BeTrue();
        plan.Paragraphs.Should().HaveCount(2);
        plan.Paragraphs.Should().OnlyContain(paragraph =>
            ReferenceEquals(paragraph.BlockContentControl, plan.BlockContentControl));
        plan.Paragraphs[0].SpanningFieldOwner.Should().BeNull();
        plan.Paragraphs[1].SpanningFieldStart!.Instruction.Should().Be(Citations.NativeFieldInstruction);
        plan.Paragraphs[1].SpanningFieldOwner.Should().BeSameAs(plan.Paragraphs[1].SpanningFieldStart);
        plan.Paragraphs[1].EndsSpanningField.Should().BeTrue();
    }

    [Fact]
    public void BuildRefreshPlan_WithExistingRegion_ReusesFirstPositionAndDeletesDescending()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Before"));
        document.Blocks.AddRange(Citations.BuildBibliography(document, CitationStyle.Apa));
        document.Blocks.Add(new Paragraph("After"));
        document.Sources.Add(new Source { Tag = "Do24", Author = "Doe", Title = "New Work", Year = "2024" });

        var plan = BibliographyRegionPlanner.BuildRefreshPlan(document);

        plan.DeleteIndicesDescending.Should().Equal(2, 1);
        plan.InsertIndex.Should().Be(1);
        plan.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("References", "Doe. (2024). New Work.");
    }

    [Fact]
    public void BuildRefreshPlan_NumericStyleRefreshesReferenceListFromCurrentSourceOrder()
    {
        var ada = new Source { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" };
        var turing = new Source { Tag = "Tur1936", Author = "Alan Turing", Title = "Computable Numbers", Year = "1936" };
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.BibliographyStyle = CitationStyle.Ieee;
        document.Sources.Add(ada);
        document.Sources.Add(turing);
        document.Blocks.AddRange(Citations.BuildBibliography(document, document.BibliographyStyle));

        document.Sources.Clear();
        document.Sources.Add(turing);
        document.Sources.Add(ada);

        var plan = BibliographyRegionPlanner.BuildRefreshPlan(document);

        plan.DeleteIndicesDescending.Should().Equal(2, 1, 0);
        plan.InsertIndex.Should().Be(0);
        plan.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal(
                "References",
                "[1] Alan Turing, \"Computable Numbers,\" 1936.",
                "[2] Ada Lovelace, \"Notes,\" 1843.");
    }
}
