using FreeW.App.Presentation.DocumentView;

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

    [Fact]
    public void Refresh_replaces_scattered_region_at_first_marker_and_undo_restores_exact_blocks()
    {
        var oldA = Generated("Old A");
        var body = new Paragraph("Body");
        var oldB = Generated("Old B");
        var tail = new Paragraph("Tail");
        var document = DocumentWith(oldA, body, oldB, tail);
        var bus = new DocumentCommandBus(new Context(document));
        var replacement = new[] { Generated("New") };

        var result = GeneratedReferenceMutationCoordinator.Refresh(
            document,
            bus,
            block => block is Paragraph paragraph && paragraph.StyleId == "Generated",
            () => replacement,
            "Update Index");

        result.Should().Be(new GeneratedReferenceMutationResult(0, 1));
        document.Blocks.Should().Equal(replacement[0], body, tail);
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
}
