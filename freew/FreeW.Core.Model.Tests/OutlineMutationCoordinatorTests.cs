namespace FreeW.Core.Model.Tests;

public sealed class OutlineMutationCoordinatorTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    [Fact]
    public void Promote_executes_one_undoable_style_change()
    {
        var (document, bus) = Create(new Paragraph("Heading") { StyleId = "Heading3" });

        OutlineMutationCoordinator.Promote(bus, document, 0).Should().BeTrue();
        ((Paragraph)document.Blocks[0]).StyleId.Should().Be("Heading2");

        bus.Undo().Should().BeTrue();
        ((Paragraph)document.Blocks[0]).StyleId.Should().Be("Heading3");
        bus.Redo().Should().BeTrue();
        ((Paragraph)document.Blocks[0]).StyleId.Should().Be("Heading2");
    }

    [Fact]
    public void Promote_suppresses_non_heading_no_op()
    {
        var (document, bus) = Create(new Paragraph("Body") { StyleId = "Normal" });

        OutlineMutationCoordinator.Promote(bus, document, 0).Should().BeFalse();

        ((Paragraph)document.Blocks[0]).StyleId.Should().Be("Normal");
        bus.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SetHeadingLevel_suppresses_an_already_applied_level()
    {
        var (document, bus) = Create(new Paragraph("Heading") { StyleId = "Heading2" });

        OutlineMutationCoordinator.SetHeadingLevel(bus, document, 0, 2).Should().BeFalse();

        bus.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Demote_turns_body_text_into_heading_one()
    {
        var (document, bus) = Create(new Paragraph("Body") { StyleId = "Normal" });

        OutlineMutationCoordinator.Demote(bus, document, 0).Should().BeTrue();

        ((Paragraph)document.Blocks[0]).StyleId.Should().Be("Heading1");
    }

    [Theory]
    [InlineData(-1, "Normal")]
    [InlineData(0, "Title")]
    [InlineData(1, "Heading1")]
    [InlineData(99, "Heading6")]
    public void SetHeadingLevel_uses_one_shared_level_mapping(int level, string expectedStyleId)
    {
        var (document, bus) = Create(new Paragraph("Text") { StyleId = "Heading3" });

        OutlineMutationCoordinator.SetHeadingLevel(bus, document, 0, level).Should().BeTrue();

        ((Paragraph)document.Blocks[0]).StyleId.Should().Be(expectedStyleId);
    }

    [Fact]
    public void Style_mutation_rejects_invalid_or_non_paragraph_targets_without_history()
    {
        var table = new Table();
        var (document, bus) = Create(table);

        OutlineMutationCoordinator.PromoteToHeading1(bus, document, -1).Should().BeFalse();
        OutlineMutationCoordinator.PromoteToHeading1(bus, document, 0).Should().BeFalse();
        OutlineMutationCoordinator.PromoteToHeading1(bus, document, 1).Should().BeFalse();

        bus.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void MoveHeading_moves_the_whole_subtree_and_reports_the_new_index()
    {
        var first = new Paragraph("First") { StyleId = "Heading1" };
        var firstBody = new Paragraph("First body") { StyleId = "Normal" };
        var second = new Paragraph("Second") { StyleId = "Heading1" };
        var secondBody = new Paragraph("Second body") { StyleId = "Normal" };
        var (document, bus) = Create(first, firstBody, second, secondBody);

        var beforeExecuteCalls = 0;
        var result = OutlineMutationCoordinator.MoveHeading(
            bus,
            document,
            0,
            moveUp: false,
            () => beforeExecuteCalls++);

        result.Should().Be(new OutlineMoveResult(0, 2, WasMoved: true));
        beforeExecuteCalls.Should().Be(1);
        document.Blocks.Should().ContainInOrder(second, secondBody, first, firstBody);
        bus.Undo().Should().BeTrue();
        document.Blocks.Should().ContainInOrder(first, firstBody, second, secondBody);
    }

    [Fact]
    public void MoveHeading_suppresses_edge_and_non_heading_no_ops()
    {
        var heading = new Paragraph("Heading") { StyleId = "Heading1" };
        var body = new Paragraph("Body") { StyleId = "Normal" };
        var (document, bus) = Create(heading, body);

        var beforeExecuteCalls = 0;
        OutlineMutationCoordinator.MoveHeading(bus, document, 0, moveUp: true, () => beforeExecuteCalls++)
            .Should().Be(OutlineMoveResult.NoChange(0));
        OutlineMutationCoordinator.MoveHeading(bus, document, 1, moveUp: false, () => beforeExecuteCalls++)
            .Should().Be(OutlineMoveResult.NoChange(1));

        beforeExecuteCalls.Should().Be(0);
        bus.CanUndo.Should().BeFalse();
    }

    private static (TextDocument Document, DocumentCommandBus Bus) Create(params Block[] blocks)
    {
        var document = new TextDocument();
        document.Blocks.AddRange(blocks);
        return (document, new DocumentCommandBus(new Context(document)));
    }
}
