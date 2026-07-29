namespace FreeW.Core.Model.Tests;

public class ShapesTests
{
    [Fact]
    public void Preset_CreatesTextlessShapeWithSizeAndFill()
    {
        var shape = Shape.Preset(ShapeKind.Ellipse, widthPt: 90, heightPt: 45, fillColorHex: "#FF0000");

        shape.Kind.Should().Be(ShapeKind.Ellipse);
        shape.WidthPt.Should().Be(90);
        shape.HeightPt.Should().Be(45);
        shape.FillColorHex.Should().Be("#FF0000");
        shape.HasText.Should().BeFalse();
        shape.PlainText.Should().BeEmpty();
    }

    [Fact]
    public void TextBoxWith_CreatesTextBoxCarryingItsText()
    {
        var shape = Shape.TextBoxWith("Hello", widthPt: 200, heightPt: 80);

        shape.Kind.Should().Be(ShapeKind.TextBox);
        shape.HasText.Should().BeTrue();
        shape.TextParagraphs.Should().ContainSingle();
        shape.PlainText.Should().Be("Hello");
    }

    [Fact]
    public void PlainText_JoinsParagraphsWithNewlines()
    {
        var shape = new Shape(ShapeKind.TextBox, 100, 100);
        foreach (var line in new[] { "one", "two" })
        {
            var p = new Paragraph();
            p.Runs.Add(new Run(line));
            shape.TextParagraphs.Add(p);
        }

        shape.PlainText.Should().Be("one\ntwo");
    }

    [Fact]
    public void FromShape_TextBox_MirrorsPlainTextAsRunFallback()
    {
        var run = Run.FromShape(Shape.TextBoxWith("caption", 120, 40));

        run.Shape.Should().NotBeNull();
        run.Text.Should().Be("caption");
    }

    [Fact]
    public void FromShape_TextlessShape_HasEmptyRunText()
    {
        var run = Run.FromShape(Shape.Preset(ShapeKind.Rectangle, 50, 50));

        run.Shape.Should().NotBeNull();
        run.Text.Should().BeEmpty();
    }

    [Fact]
    public void ShapeTextCommands_split_merge_and_sync_the_outer_run_mirror()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        var shape = Shape.TextBoxWith("First line", 120, 60);
        var owner = Run.FromShape(shape);
        paragraph.Runs.Add(owner);
        doc.Blocks.Add(paragraph);
        var context = new ShapeTestContext(doc);

        var split = new InsertShapeTextParagraphBreakCommand(0, 0, 0, 0, 5);
        split.Apply(context);

        shape.PlainText.Should().Be("First\n line");
        owner.Text.Should().Be("First\n line");
        shape.TextParagraphs.Should().HaveCount(2);

        var merge = new MergeShapeTextParagraphWithPreviousCommand(0, 0, 1);
        merge.Apply(context);
        shape.PlainText.Should().Be("First line");
        owner.Text.Should().Be("First line");

        merge.Revert(context);
        shape.PlainText.Should().Be("First\n line");
        owner.Text.Should().Be("First\n line");
        split.Revert(context);
        shape.PlainText.Should().Be("First line");
        owner.Text.Should().Be("First line");
    }

    // ── W26: Body rotation / flip properties ─────────────────────────────────────────────────────

    [Fact]
    public void Shape_DefaultRotationAndFlip_AreZeroAndFalse()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);

        shape.RotationAngle.Should().Be(0, "rotation defaults to 0");
        shape.FlipH.Should().BeFalse("FlipH defaults to false");
        shape.FlipV.Should().BeFalse("FlipV defaults to false");
    }

    [Fact]
    public void Shape_RotationAngle_CanBeSetAndRead()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.RotationAngle = 45;

        shape.RotationAngle.Should().Be(45);
    }

    [Fact]
    public void Shape_FlipH_CanBeSetAndRead()
    {
        var shape = Shape.Preset(ShapeKind.Ellipse, widthPt: 80, heightPt: 40);
        shape.FlipH = true;

        shape.FlipH.Should().BeTrue();
        shape.FlipV.Should().BeFalse("setting FlipH must not affect FlipV");
    }

    [Fact]
    public void Shape_FlipV_CanBeSetAndRead()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 80, heightPt: 40);
        shape.FlipV = true;

        shape.FlipV.Should().BeTrue();
        shape.FlipH.Should().BeFalse("setting FlipV must not affect FlipH");
    }

    [Fact]
    public void SetShapeRotationCommand_AppliesAndReverts()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        paragraph.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(paragraph);
        var context = new ShapeTestContext(doc);

        var cmd = new SetShapeRotationCommand(0, 0, 90, flipH: true, flipV: false);
        cmd.Apply(context);

        shape.RotationAngle.Should().Be(90);
        shape.FlipH.Should().BeTrue();
        shape.FlipV.Should().BeFalse();

        cmd.Revert(context);

        shape.RotationAngle.Should().Be(0);
        shape.FlipH.Should().BeFalse();
        shape.FlipV.Should().BeFalse();
    }

    [Fact]
    public void SetShapeWrappingCommand_AppliesAndReverts()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        paragraph.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(paragraph);
        var context = new ShapeTestContext(doc);

        var cmd = new SetShapeWrappingCommand(0, 0, ImageWrapping.Square);
        cmd.Apply(context);

        shape.Placement.Should().NotBeNull("SetShapeWrappingCommand must create FloatingPlacement if absent");
        shape.Placement!.Wrapping.Should().Be(ImageWrapping.Square);

        cmd.Revert(context);

        shape.Placement.Wrapping.Should().Be(ImageWrapping.Inline, "Revert must restore original wrapping");
    }

    private sealed class ShapeTestContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }
}
