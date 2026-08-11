using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class TableOfContentsMutationCoordinatorTests
{
    [Fact]
    public void Refresh_replaces_all_existing_regions_at_first_marker_and_undo_restores_them()
    {
        var firstOld = TocParagraph("Old A");
        var secondOld = TocParagraph("Old B");
        var body = new Paragraph("Body");
        var heading = Heading("Chapter");
        var document = DocumentWith(firstOld, body, secondOld, heading);
        var bus = new DocumentCommandBus(new Context(document));
        var insertionIndex = TableOfContentsMutationCoordinator.FindRefreshInsertionIndex(document);

        TableOfContentsMutationCoordinator.Apply(
            document,
            bus,
            insertionIndex,
            "Update Table of Contents",
            replaceExisting: true,
            () => TableOfContents.Build(document));

        document.Blocks.Where(TableOfContents.IsTocParagraph)
            .Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal(TableOfContents.HeadingText, "Chapter\t1");
        document.Blocks[0].Should().BeOfType<Paragraph>()
            .Which.PlainText.Should().Be(TableOfContents.HeadingText);

        bus.Undo().Should().BeTrue();
        document.Blocks.Should().Equal(firstOld, body, secondOld, heading);
    }

    [Fact]
    public void Insert_preserves_preexisting_generated_regions()
    {
        var existing = TocParagraph("Existing");
        var document = DocumentWith(existing, Heading("New Chapter"));
        var bus = new DocumentCommandBus(new Context(document));

        TableOfContentsMutationCoordinator.Apply(
            document,
            bus,
            1,
            "Insert Table of Contents",
            replaceExisting: false,
            () => TableOfContents.Build(document));

        document.Blocks.Should().Contain(existing);
        document.Blocks.Count(TableOfContents.IsTocParagraph).Should().Be(3);
    }

    [Fact]
    public void Stabilization_rebuilds_after_layout_and_commits_as_one_undo_step()
    {
        var heading = Heading("Chapter");
        var document = DocumentWith(heading);
        var bus = new DocumentCommandBus(new Context(document));
        var pageText = "1";
        var layoutPasses = 0;
        IReadOnlyList<Paragraph> Build() =>
        [
            new Paragraph(TableOfContents.HeadingText) { StyleId = TableOfContents.HeadingStyleId },
            new Paragraph($"Chapter\t{pageText}") { StyleId = TableOfContents.EntryStyleId(1) },
        ];

        TableOfContentsMutationCoordinator.Apply(
            document,
            bus,
            0,
            "Insert Table of Contents",
            replaceExisting: false,
            Build,
            () =>
            {
                layoutPasses++;
                pageText = "2";
            });

        layoutPasses.Should().Be(2);
        document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Contain("Chapter\t2");
        bus.Undo().Should().BeTrue();
        document.Blocks.Should().ContainSingle().Which.Should().BeSameAs(heading);
    }

    [Fact]
    public void Non_stabilizing_generation_rolls_back_every_model_mutation()
    {
        var heading = Heading("Chapter");
        var document = DocumentWith(heading);
        var bus = new DocumentCommandBus(new Context(document));
        var buildCount = 0;
        IReadOnlyList<Paragraph> Build() =>
        [
            new Paragraph(TableOfContents.HeadingText) { StyleId = TableOfContents.HeadingStyleId },
            new Paragraph($"Chapter\t{++buildCount}") { StyleId = TableOfContents.EntryStyleId(1) },
        ];

        var act = () => TableOfContentsMutationCoordinator.Apply(
            document,
            bus,
            0,
            "Insert Table of Contents",
            replaceExisting: false,
            Build);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*did not stabilize*");
        document.Blocks.Should().ContainSingle().Which.Should().BeSameAs(heading);
        bus.CanUndo.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    [InlineData(3, 0)]
    public void Insertion_index_uses_shared_front_matter_fallback(int requested, int expected)
    {
        var document = DocumentWith(new Paragraph("A"), new Paragraph("B"));

        TableOfContentsMutationCoordinator.NormalizeInsertionIndex(document, requested)
            .Should().Be(expected);
    }

    private static Paragraph Heading(string text) => new(text) { StyleId = "Heading1" };

    private static Paragraph TocParagraph(string text) =>
        new(text) { StyleId = TableOfContents.EntryStyleId(1) };

    private static TextDocument DocumentWith(params Block[] blocks)
    {
        var document = new TextDocument();
        document.Blocks.AddRange(blocks);
        return document;
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }
}
