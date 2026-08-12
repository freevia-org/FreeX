namespace FreeW.Core.Model.Tests;

public sealed class FloatingObjectTests
{
    [Fact]
    public void Shape_IsInlineByDefault()
    {
        var shape = new Shape(ShapeKind.Rectangle, 72, 36);
        shape.IsFloating.Should().BeFalse();
        shape.Placement.Should().BeNull();
    }

    [Fact]
    public void Chart_IsInlineByDefault()
    {
        var chart = new Chart { Kind = ChartKind.Column };
        chart.IsFloating.Should().BeFalse();
        chart.Placement.Should().BeNull();
    }

    [Fact]
    public void SmartArt_IsInlineByDefault()
    {
        var art = new SmartArt { Kind = SmartArtKind.List };
        art.IsFloating.Should().BeFalse();
        art.Placement.Should().BeNull();
    }

    [Fact]
    public void WordArt_IsInlineByDefault()
    {
        var wa = new WordArt("Hello");
        wa.IsFloating.Should().BeFalse();
        wa.Placement.Should().BeNull();
    }

    [Fact]
    public void FloatingPlacement_DefaultsCorrect()
    {
        var p = new FloatingPlacement();
        p.Wrapping.Should().Be(ImageWrapping.Inline);
        p.IsFloating.Should().BeFalse();
        p.HorizontalOffsetPt.Should().Be(0);
        p.VerticalOffsetPt.Should().Be(0);
        p.HorizontalAnchor.Should().Be(HorizontalAnchor.Column);
        p.VerticalAnchor.Should().Be(VerticalAnchor.Paragraph);
        p.ZOrderIndex.Should().Be(0);
    }

    [Fact]
    public void FloatingPlacement_IsFloating_WhenWrappingNotInline()
    {
        var p = new FloatingPlacement { Wrapping = ImageWrapping.Square };
        p.IsFloating.Should().BeTrue();
    }

    [Fact]
    public void ChangeZOrder_GeneralizedToShape()
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        var shape1 = new Shape(ShapeKind.Rectangle, 72, 36) { Placement = new FloatingPlacement { Wrapping = ImageWrapping.Square, ZOrderIndex = 0 } };
        var shape2 = new Shape(ShapeKind.Ellipse, 60, 30) { Placement = new FloatingPlacement { Wrapping = ImageWrapping.Square, ZOrderIndex = 1 } };
        para.Runs.Add(Run.FromShape(shape1));
        para.Runs.Add(Run.FromShape(shape2));
        doc.Blocks.Add(para);

        var cmd = new ChangeZOrderCommand(0, 0, ZOrderOperation.BringToFront);
        cmd.Apply(new SimpleCommandContext(doc));

        shape1.Placement!.ZOrderIndex.Should().BeGreaterThan(shape2.Placement!.ZOrderIndex);
    }
}

file sealed class SimpleCommandContext(TextDocument doc) : IDocumentCommandContext
{
    public TextDocument Document => doc;
}
