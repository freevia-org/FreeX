namespace FreeW.Core.Model.Tests;

public sealed class FloatingObjectArrangeCommandTests
{
    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47];

    private static InlineImage FloatingImage(double x, double y) =>
        new(Png(), 60, 40)
        {
            Wrapping = ImageWrapping.Square,
            HorizontalOffsetPt = x,
            VerticalOffsetPt = y,
            HorizontalAnchor = HorizontalAnchor.Column,
            VerticalAnchor = VerticalAnchor.Paragraph
        };

    private static Shape FloatingShape(double x, double y) =>
        new(ShapeKind.Rectangle, 80, 50)
        {
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = x,
                VerticalOffsetPt = y,
                HorizontalAnchor = HorizontalAnchor.Column,
                VerticalAnchor = VerticalAnchor.Paragraph
            }
        };

    private static (TextDocument Doc, DocumentCommandBus Bus) MixedFloatingDoc()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Page.MarginLeftPt = 90;

        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.FromImage(FloatingImage(12, 18)),
                Run.FromShape(FloatingShape(132, 66)),
                Run.FromImage(FloatingImage(252, 126))
            }
        });

        return (doc, new DocumentCommandBus(new TestContext(doc)));
    }

    [Fact]
    public void AlignToMargin_updates_image_and_shape_offsets_and_anchors()
    {
        var (doc, bus) = MixedFloatingDoc();
        var members = ArrangeFloatingObjectsCommand.CollectFloatingObjectLocations(doc);

        bus.Execute(new ArrangeFloatingObjectsCommand(FloatingObjectArrangeKind.AlignToMargin, members));

        var para = (Paragraph)doc.Blocks[0];
        para.Runs[0].Image!.HorizontalOffsetPt.Should().Be(90);
        para.Runs[0].Image!.HorizontalAnchor.Should().Be(HorizontalAnchor.Margin);
        para.Runs[1].Shape!.Placement!.HorizontalOffsetPt.Should().Be(90);
        para.Runs[1].Shape!.Placement!.HorizontalAnchor.Should().Be(HorizontalAnchor.Margin);
        para.Runs[2].Image!.HorizontalOffsetPt.Should().Be(90);
        para.Runs[2].Image!.HorizontalAnchor.Should().Be(HorizontalAnchor.Margin);
    }

    [Fact]
    public void DistributeHorizontal_preserves_first_and_last_and_evenly_spaces_middle()
    {
        var (doc, bus) = MixedFloatingDoc();
        ((Paragraph)doc.Blocks[0]).Runs[1].Shape!.Placement!.HorizontalOffsetPt = 190;
        var members = ArrangeFloatingObjectsCommand.CollectFloatingObjectLocations(doc);

        bus.Execute(new ArrangeFloatingObjectsCommand(FloatingObjectArrangeKind.DistributeHorizontal, members));

        var para = (Paragraph)doc.Blocks[0];
        para.Runs[0].Image!.HorizontalOffsetPt.Should().Be(12);
        para.Runs[1].Shape!.Placement!.HorizontalOffsetPt.Should().Be(132);
        para.Runs[2].Image!.HorizontalOffsetPt.Should().Be(252);
    }

    [Fact]
    public void DistributeVertical_evenly_spaces_by_y_order()
    {
        var (doc, bus) = MixedFloatingDoc();
        ((Paragraph)doc.Blocks[0]).Runs[1].Shape!.Placement!.VerticalOffsetPt = 126;
        ((Paragraph)doc.Blocks[0]).Runs[2].Image!.VerticalOffsetPt = 66;
        var members = ArrangeFloatingObjectsCommand.CollectFloatingObjectLocations(doc);

        bus.Execute(new ArrangeFloatingObjectsCommand(FloatingObjectArrangeKind.DistributeVertical, members));

        var para = (Paragraph)doc.Blocks[0];
        para.Runs[0].Image!.VerticalOffsetPt.Should().Be(18);
        para.Runs[2].Image!.VerticalOffsetPt.Should().Be(72);
        para.Runs[1].Shape!.Placement!.VerticalOffsetPt.Should().Be(126);
    }

    [Fact]
    public void Undo_restores_image_and_shape_placement()
    {
        var (doc, bus) = MixedFloatingDoc();
        var members = ArrangeFloatingObjectsCommand.CollectFloatingObjectLocations(doc);

        bus.Execute(new ArrangeFloatingObjectsCommand(FloatingObjectArrangeKind.AlignToPage, members));
        bus.Undo().Should().BeTrue();

        var para = (Paragraph)doc.Blocks[0];
        para.Runs[0].Image!.HorizontalOffsetPt.Should().Be(12);
        para.Runs[0].Image!.HorizontalAnchor.Should().Be(HorizontalAnchor.Column);
        para.Runs[1].Shape!.Placement!.HorizontalOffsetPt.Should().Be(132);
        para.Runs[1].Shape!.Placement!.HorizontalAnchor.Should().Be(HorizontalAnchor.Column);
    }

    [Fact]
    public void Distribute_requires_at_least_two_floating_objects()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph { Runs = { Run.FromImage(FloatingImage(12, 18)) } });
        var bus = new DocumentCommandBus(new TestContext(doc));

        bus.Execute(new ArrangeFloatingObjectsCommand(
            FloatingObjectArrangeKind.DistributeHorizontal,
            ArrangeFloatingObjectsCommand.CollectFloatingObjectLocations(doc)));

        ((Paragraph)doc.Blocks[0]).Runs[0].Image!.HorizontalOffsetPt.Should().Be(12);
        bus.Undo().Should().BeTrue("no-op commands are still recorded by the current command bus");
        ((Paragraph)doc.Blocks[0]).Runs[0].Image!.HorizontalOffsetPt.Should().Be(12);
    }

    private sealed class TestContext(TextDocument doc) : IDocumentCommandContext
    {
        public TextDocument Document => doc;
    }
}
