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
}
