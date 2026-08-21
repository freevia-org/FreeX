using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class GeneratedReferenceMutationCoordinatorTests
{
    [Fact]
    public void Multi_paragraph_insert_is_one_undoable_back_matter_mutation()
    {
        var body = new Paragraph("Body");
        var document = DocumentWith(body);
        var bus = new DocumentCommandBus(new Context(document));
        var generated = new[] { Generated("A"), Generated("Alpha, 1") };

        var result = GeneratedReferenceMutationCoordinator.Insert(
            document, bus, requestedIndex: 99, "Insert Index", generated);

        result.Should().Be(new GeneratedReferenceMutationResult(1, 2));
        document.Blocks.Should().Equal(body, generated[0], generated[1]);
        bus.Undo().Should().BeTrue();
        document.Blocks.Should().ContainSingle().Which.Should().BeSameAs(body);
        bus.CanUndo.Should().BeFalse();
    }

    /// <summary>
    /// r161 remediation. This test previously asserted the opposite: that refreshing collapsed EVERY
    /// generated block in the document into one region at the first marker, deleting the second.
    /// That is the destructive behaviour the shipping coordinator was fixed to stop doing, and a
    /// test asserting it would have defended the defect the moment anyone wired this coordinator up.
    /// A document may legitimately hold two independent generated regions -- a main table of
    /// contents and one for an appendix -- and refreshing one must leave the other alone.
    /// </summary>
    [Fact]
    public void Refresh_replaces_only_the_first_region_and_leaves_a_separate_one_untouched()
    {
        var oldA = Generated("Old A");
        var body = new Paragraph("Body");
        var oldB = Generated("Old B");
        var tail = new Paragraph("Tail");
        var document = DocumentWith(oldA, body, oldB, tail);
        var bus = new DocumentCommandBus(new Context(document));
        var replacement = new[] { Generated("New") };

        GeneratedReferenceMutationCoordinator.Refresh(
            document,
            bus,
            block => block is Paragraph paragraph && paragraph.StyleId == "Generated",
            () => replacement,
            "Update Index");

        // The second generated region is a separate field and must survive a refresh of the first.
        document.Blocks.Should().Equal(replacement[0], body, oldB, tail);

        bus.Undo().Should().BeTrue();
        document.Blocks.Should().Equal(oldA, body, oldB, tail);
    }

    [Fact]
    public void Refresh_without_existing_region_uses_document_end_not_front_matter()
    {
        var first = new Paragraph("First");
        var last = new Paragraph("Last");
        var document = DocumentWith(first, last);
        var bus = new DocumentCommandBus(new Context(document));

        var result = GeneratedReferenceMutationCoordinator.Refresh(
            document,
            bus,
            _ => false,
            () => [Generated("Index")],
            "Update Index");

        result.InsertIndex.Should().Be(2);
        document.Blocks.Should().Equal(first, last, document.Blocks[2]);
        ((Paragraph)document.Blocks[2]).PlainText.Should().Be("Index");
    }

    [Fact]
    public void Builder_failure_rolls_back_deletions_and_leaves_no_undo_entry()
    {
        var old = Generated("Old");
        var body = new Paragraph("Body");
        var document = DocumentWith(old, body);
        var bus = new DocumentCommandBus(new Context(document));

        var act = () => GeneratedReferenceMutationCoordinator.Refresh(
            document,
            bus,
            block => ReferenceEquals(block, old),
            () => throw new InvalidOperationException("generation failed"),
            "Update Index");

        act.Should().Throw<InvalidOperationException>().WithMessage("generation failed");
        document.Blocks.Should().Equal(old, body);
        bus.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Plan_failure_rolls_back_commands_already_applied_and_leaves_no_undo_entry()
    {
        var first = new Paragraph("First");
        var second = new Paragraph("Second");
        var document = DocumentWith(first, second);
        var bus = new DocumentCommandBus(new Context(document));
        var plan = new Plan([1, 99], 0, [Generated("Replacement")]);

        var act = () => GeneratedReferenceMutationCoordinator.ApplyPlan(
            document, bus, plan, "Update Bibliography");

        act.Should().Throw<ArgumentOutOfRangeException>();
        document.Blocks.Should().Equal(first, second);
        bus.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Stabilizing_plan_returns_final_shape_and_is_one_undoable_mutation()
    {
        var body = new Paragraph("Body");
        var document = DocumentWith(body);
        var bus = new DocumentCommandBus(new Context(document));
        var initial = new Plan([], 1, [Generated("Initial")]);
        var stabilized = new Plan([1], 0, [Generated("Final A"), Generated("Final B")]);
        var layoutPasses = 0;
        var refreshBuilds = 0;

        var result = GeneratedReferenceMutationCoordinator.ApplyStabilizingPlan(
            document,
            bus,
            initial,
            "Insert Table of Authorities",
            () =>
            {
                refreshBuilds++;
                return stabilized;
            },
            paragraphs => paragraphs.Count == 2 &&
                          document.Blocks.OfType<Paragraph>().Take(2)
                              .Select(paragraph => paragraph.PlainText)
                              .SequenceEqual(["Final A", "Final B"]),
            () => layoutPasses++);

        result.Should().Be(new GeneratedReferenceMutationResult(0, 2));
        refreshBuilds.Should().Be(2);
        layoutPasses.Should().Be(2);
        document.Blocks.Select(block => ((Paragraph)block).PlainText)
            .Should().Equal("Final A", "Final B", "Body");
        bus.Undo().Should().BeTrue();
        document.Blocks.Should().ContainSingle().Which.Should().BeSameAs(body);
        bus.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Stabilization_failure_rolls_back_every_pass_and_leaves_no_undo_entry()
    {
        var body = new Paragraph("Body");
        var document = DocumentWith(body);
        var bus = new DocumentCommandBus(new Context(document));
        var initial = new Plan([], 1, [Generated("Initial")]);
        var build = 0;

        var act = () => GeneratedReferenceMutationCoordinator.ApplyStabilizingPlan(
            document,
            bus,
            initial,
            "Insert Table of Authorities",
            () =>
            {
                build++;
                return new Plan([1], 1, [Generated($"Pass {build}")]);
            },
            _ => false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Generated reference pagination did not stabilize.");
        build.Should().Be(GeneratedReferenceMutationCoordinator.MaxStabilizationPasses + 1);
        document.Blocks.Should().ContainSingle().Which.Should().BeSameAs(body);
        bus.CanUndo.Should().BeFalse();
    }

    private static Paragraph Generated(string text) => new(text) { StyleId = "Generated" };

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

    private sealed record Plan(
        IReadOnlyList<int> DeleteIndicesDescending,
        int InsertIndex,
        IReadOnlyList<Paragraph> Paragraphs) : IGeneratedReferenceRegionPlan;
}
