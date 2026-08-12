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

    [Fact]
    public void ShapeTextCommands_edit_a_nested_group_leaf_without_flattening_the_group()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var leaf = Shape.TextBoxWith("hello", 120, 50);
        var inner = new DrawingGroup { WidthPt = 160, HeightPt = 80 };
        inner.Children.Add(new Shape(ShapeKind.Rectangle, 20, 20));
        inner.ChildOffsets.Add((0, 0));
        inner.Children.Add(leaf);
        inner.ChildOffsets.Add((30, 10));
        var outer = new DrawingGroup { WidthPt = 240, HeightPt = 120 };
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((12, 8));
        outer.Children.Add(new Shape(ShapeKind.Ellipse, 30, 20));
        outer.ChildOffsets.Add((180, 70));
        var paragraph = new Paragraph();
        var owner = Run.FromDrawingGroup(outer);
        paragraph.Runs.Add(owner);
        doc.Blocks.Add(paragraph);
        var context = new ShapeTestContext(doc);
        var path = new[] { 0, 1 };

        var set = new SetShapeTextRunCommand(0, 0, 0, 0, "hello!", path);
        set.Apply(context);
        leaf.PlainText.Should().Be("hello!");
        owner.Text.Should().BeEmpty("a grouped drawing run has no flattened text mirror");
        outer.Children.Should().ContainSingle(child => ReferenceEquals(child, inner));
        set.Revert(context);
        leaf.PlainText.Should().Be("hello");

        var split = new InsertShapeTextParagraphBreakCommand(0, 0, 0, 0, 2, path);
        split.Apply(context);
        leaf.PlainText.Should().Be("he\nllo");
        leaf.TextParagraphs.Should().HaveCount(2);
        var merge = new MergeShapeTextParagraphWithPreviousCommand(0, 0, 1, path);
        merge.Apply(context);
        leaf.PlainText.Should().Be("hello");
        merge.Revert(context);
        leaf.PlainText.Should().Be("he\nllo");
        split.Revert(context);
        leaf.PlainText.Should().Be("hello");

        var replacement = new Paragraph();
        replacement.Runs.Add(new Run("reopened"));
        var replace = new ReplaceShapeTextParagraphsCommand(0, 0, [replacement], path);
        replace.Apply(context);
        leaf.PlainText.Should().Be("reopened");
        replace.Revert(context);
        leaf.PlainText.Should().Be("hello");
    }

    [Fact]
    public void SetShapeTextDirectionCommand_targets_nested_leaf_and_restores_group_state()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var leaf = Shape.TextBoxWith("rotated leaf", 120, 50);
        leaf.RotationAngle = 17;
        leaf.FlipH = true;
        leaf.FlipV = true;
        var sibling = Shape.TextBoxWith("sibling", 90, 40);
        var inner = new DrawingGroup { WidthPt = 160, HeightPt = 80, RotationAngle = 23 };
        inner.Children.Add(new Shape(ShapeKind.Rectangle, 20, 20));
        inner.ChildOffsets.Add((0, 0));
        inner.Children.Add(leaf);
        inner.ChildOffsets.Add((30, 10));
        var outer = new DrawingGroup { WidthPt = 240, HeightPt = 120, RotationAngle = 31, FlipV = true };
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((12, 8));
        outer.Children.Add(sibling);
        outer.ChildOffsets.Add((180, 70));
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(outer));
        document.Blocks.Add(paragraph);
        var context = new ShapeTestContext(document);
        var path = new[] { 0, 1 };
        var transforms = (outer.RotationAngle, outer.FlipH, outer.FlipV,
            inner.RotationAngle, inner.FlipH, inner.FlipV,
            leaf.RotationAngle, leaf.FlipH, leaf.FlipV);

        foreach (var direction in new[]
                 { ShapeTextDirection.Horizontal, ShapeTextDirection.Rotate90, ShapeTextDirection.Rotate270 })
        {
            var command = new SetShapeTextDirectionCommand(0, 0, direction, path);
            command.Apply(context);
            leaf.TextDirection.Should().Be(direction);
            sibling.TextDirection.Should().Be(ShapeTextDirection.Horizontal);
            (outer.RotationAngle, outer.FlipH, outer.FlipV,
                inner.RotationAngle, inner.FlipH, inner.FlipV,
                leaf.RotationAngle, leaf.FlipH, leaf.FlipV).Should().Be(transforms);
            command.Revert(context);
            leaf.TextDirection.Should().Be(ShapeTextDirection.Horizontal);
        }
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

    private sealed class ShapeTestContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }
}
