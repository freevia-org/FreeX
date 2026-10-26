namespace FreeW.Core.Model.Tests;

public sealed class ShapeTextParagraphAlignmentCommandTests
{
    [Theory]
    // r204: Left was here too, but the fixture's leaf is ALREADY Left (see the Undo assertion
    // below), so that case was asserting that a command which changes nothing still round-trips
    // through undo -- the phantom entry the r203 census identified, which also cleared redo.
    // It now has its own test, immediately after this one.
    [InlineData(TextAlignment.Center)]
    [InlineData(TextAlignment.Right)]
    [InlineData(TextAlignment.Justify)]
    public void Nested_leaf_alignment_formats_only_the_leaf_and_round_trips_undo_redo(TextAlignment alignment)
    {
        var document = BuildNestedDocument(out var outer, out var inner, out var leaf, out var sibling);
        var bus = new DocumentCommandBus(new Context(document));
        var outerTransform = (outer.RotationAngle, outer.FlipH, outer.FlipV);
        var innerTransform = (inner.RotationAngle, inner.FlipH, inner.FlipV);
        var leafTransform = (leaf.RotationAngle, leaf.FlipH, leaf.FlipV);
        var siblingFormatting = sibling.TextParagraphs.Select(paragraph => paragraph.Formatting).ToArray();

        bus.Execute(new SetShapeTextParagraphAlignmentCommand(0, 0, alignment, [0, 1]));

        leaf.TextParagraphs.Should().NotBeEmpty();
        leaf.TextParagraphs.Should().OnlyContain(paragraph => paragraph.Formatting.Alignment == alignment);
        sibling.TextParagraphs.Select(paragraph => paragraph.Formatting).Should().Equal(siblingFormatting);
        (outer.RotationAngle, outer.FlipH, outer.FlipV).Should().Be(outerTransform);
        (inner.RotationAngle, inner.FlipH, inner.FlipV).Should().Be(innerTransform);
        (leaf.RotationAngle, leaf.FlipH, leaf.FlipV).Should().Be(leafTransform);

        bus.Undo().Should().BeTrue();
        leaf.TextParagraphs.Should().OnlyContain(paragraph => paragraph.Formatting.Alignment == TextAlignment.Left);
        bus.Redo().Should().BeTrue();
        leaf.TextParagraphs.Should().OnlyContain(paragraph => paragraph.Formatting.Alignment == alignment);
    }

    [Fact]
    public void Applying_the_alignment_the_leaf_already_has_pushes_no_undo_entry()
    {
        // r204: the leaf starts Left. Re-applying Left is what a user does by clicking the ribbon's
        // already-highlighted alignment button, and it must not push an entry -- pushing one clears
        // the redo stack, discarding a real edit the user could still have redone.
        var document = BuildNestedDocument(out _, out _, out var leaf, out _);
        var bus = new DocumentCommandBus(new Context(document));
        leaf.TextParagraphs.Should().OnlyContain(p => p.Formatting.Alignment == TextAlignment.Left);

        // A real edit first, undone, so there is a redo to lose.
        bus.Execute(new SetShapeTextParagraphAlignmentCommand(0, 0, TextAlignment.Center, [0, 1]));
        bus.Undo().Should().BeTrue();
        bus.CanRedo.Should().BeTrue();

        bus.Execute(new SetShapeTextParagraphAlignmentCommand(0, 0, TextAlignment.Left, [0, 1]));

        bus.CanRedo.Should().BeTrue("a command that changed nothing must not discard the redo");
        leaf.TextParagraphs.Should().OnlyContain(p => p.Formatting.Alignment == TextAlignment.Left);
    }

    [Fact]
    public void Direct_shape_alignment_command_is_independent_from_containing_document_paragraph()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Right }
        };
        var shape = Shape.TextBoxWith("Direct shape", 120, 48);
        paragraph.Runs.Add(Run.FromShape(shape));
        document.Blocks.Add(paragraph);
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new SetParagraphFormattingCommand(
            0,
            paragraph.Formatting with { Alignment = TextAlignment.Center }));

        paragraph.Formatting.Alignment.Should().Be(TextAlignment.Center);
        shape.TextParagraphs.Single().Formatting.Alignment.Should().Be(TextAlignment.Left);
    }

    private static TextDocument BuildNestedDocument(
        out DrawingGroup outer,
        out DrawingGroup inner,
        out Shape leaf,
        out Shape sibling)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        leaf = Shape.TextBoxWith("Nested leaf", 100, 48);
        leaf.RotationAngle = 17;
        leaf.FlipH = true;
        inner = new DrawingGroup { WidthPt = 160, HeightPt = 80, RotationAngle = -23, FlipV = true };
        inner.Children.Add(new Shape(ShapeKind.Rectangle, 24, 20));
        inner.ChildOffsets.Add((5, 5));
        inner.Children.Add(leaf);
        inner.ChildOffsets.Add((32, 12));
        sibling = Shape.TextBoxWith("Sibling", 90, 40);
        outer = new DrawingGroup { WidthPt = 240, HeightPt = 130, RotationAngle = 31, FlipH = true };
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((18, 10));
        outer.Children.Add(sibling);
        outer.ChildOffsets.Add((180, 70));

        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(outer));
        document.Blocks.Add(paragraph);
        return document;
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }
}
