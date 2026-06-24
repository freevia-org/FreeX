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

    private static (TextDocument doc, int bi, int ri) DocWithShape()
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromShape(new Shape(ShapeKind.Ellipse, 72, 36)));
        doc.Blocks.Add(para);
        return (doc, 0, 0);
    }

    private static (TextDocument doc, int bi, int ri) DocWithChart()
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run(string.Empty) { Chart = new Chart { Kind = ChartKind.Line } });
        doc.Blocks.Add(para);
        return (doc, 0, 0);
    }

    [Fact]
    public void ToggleWrapping_Shape_SetsFloating()
    {
        var (doc, bi, ri) = DocWithShape();
        var cmd = new ToggleObjectWrappingCommand(bi, ri, ImageWrapping.Square);
        cmd.Apply(new SimpleCommandContext(doc));

        var shape = ((Paragraph)doc.Blocks[0]).Runs[ri].Shape!;
        shape.IsFloating.Should().BeTrue();
        shape.Placement!.Wrapping.Should().Be(ImageWrapping.Square);
    }

    [Fact]
    public void ToggleWrapping_Shape_Reverts()
    {
        var (doc, bi, ri) = DocWithShape();
        var ctx = new SimpleCommandContext(doc);
        var cmd = new ToggleObjectWrappingCommand(bi, ri, ImageWrapping.Square);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        var shape = ((Paragraph)doc.Blocks[0]).Runs[ri].Shape!;
        shape.IsFloating.Should().BeFalse();
        shape.Placement!.Wrapping.Should().Be(ImageWrapping.Inline);
    }

    [Fact]
    public void ToggleWrapping_Chart_SetsFloating()
    {
        var (doc, bi, ri) = DocWithChart();
        var cmd = new ToggleObjectWrappingCommand(bi, ri, ImageWrapping.InFront);
        cmd.Apply(new SimpleCommandContext(doc));

        var chart = ((Paragraph)doc.Blocks[0]).Runs[ri].Chart!;
        chart.IsFloating.Should().BeTrue();
        chart.Placement!.Wrapping.Should().Be(ImageWrapping.InFront);
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
