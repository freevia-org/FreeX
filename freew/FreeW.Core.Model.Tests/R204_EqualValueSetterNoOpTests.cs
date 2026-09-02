namespace FreeW.Core.Model.Tests;

/// <summary>
/// r204: the behavioural half of paying down r203's confirmed debt. All 32 commands the r203 census
/// confirmed were no-op-capable now declare <c>HasEffect</c>; these pin the shape they all share --
/// re-applying the value the object already carries must not push an undo entry, because that push
/// clears the redo stack.
/// <para>
/// Re-confirming a value is an ordinary gesture, not a contrived one: ribbon galleries highlight the
/// current selection and format dialogs pre-populate the current numbers, so clicking the
/// already-active option or pressing OK unchanged is what a user does all day.
/// </para>
/// </summary>
public sealed class R204_EqualValueSetterNoOpTests
{
    private sealed class Ctx(TextDocument doc) : IDocumentCommandContext
    {
        public TextDocument Document => doc;
    }

    private static (TextDocument Document, InlineImage Image) DocumentWithImage()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var image = new InlineImage([1, 2, 3], 120, 90, ImageFormat.Png)
        {
            AltText = "A chart of quarterly revenue",
            RotationAngle = 30,
        };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("") { Image = image });
        document.Blocks.Add(paragraph);
        return (document, image);
    }

    private static (TextDocument Document, Shape Shape) DocumentWithShape()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var shape = new Shape(ShapeKind.Rectangle, 80, 40) { AltText = "A callout", FillColorHex = "#FF0000" };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("") { Shape = shape });
        document.Blocks.Add(paragraph);
        return (document, shape);
    }

    [Fact]
    public void ReApplyingAnImagesOwnSize_HasNoEffect()
    {
        var (document, image) = DocumentWithImage();

        new SetImageSizeCommand(0, 0, image.WidthPt, image.HeightPt)
            .HasEffect(new Ctx(document)).Should().BeFalse();
    }

    [Fact]
    public void ChangingAnImagesSize_HasEffect()
    {
        var (document, image) = DocumentWithImage();

        new SetImageSizeCommand(0, 0, image.WidthPt + 10, image.HeightPt)
            .HasEffect(new Ctx(document)).Should().BeTrue();
    }

    [Fact]
    public void ReApplyingAnImagesOwnAltText_HasNoEffect()
    {
        var (document, image) = DocumentWithImage();

        new SetImageAltTextCommand(0, 0, image.AltText)
            .HasEffect(new Ctx(document)).Should().BeFalse();
    }

    [Fact]
    public void ReApplyingAnImagesOwnRotation_HasNoEffect()
    {
        var (document, image) = DocumentWithImage();

        new SetImageRotationCommand(0, 0, image.RotationAngle, image.FlipH, image.FlipV)
            .HasEffect(new Ctx(document)).Should().BeFalse();
    }

    [Fact]
    public void FlippingAnImage_HasEffect()
    {
        var (document, image) = DocumentWithImage();

        new SetImageRotationCommand(0, 0, image.RotationAngle, !image.FlipH, image.FlipV)
            .HasEffect(new Ctx(document)).Should().BeTrue();
    }

    [Fact]
    public void ReApplyingAShapesOwnFill_HasNoEffect()
    {
        var (document, shape) = DocumentWithShape();

        new SetShapeFillCommand(0, 0, shape.FillColorHex)
            .HasEffect(new Ctx(document)).Should().BeFalse();
    }

    [Fact]
    public void ChangingAShapesFill_HasEffect()
    {
        var (document, _) = DocumentWithShape();

        new SetShapeFillCommand(0, 0, "#00FF00").HasEffect(new Ctx(document)).Should().BeTrue();
    }

    [Fact]
    public void ReApplyingAShapesOwnSize_HasNoEffect()
    {
        var (document, shape) = DocumentWithShape();

        new SetShapeSizeCommand(0, 0, shape.WidthPt, shape.HeightPt)
            .HasEffect(new Ctx(document)).Should().BeFalse();
    }

    [Fact]
    public void SettingAShapePositionWhenItHasNoPlacementYet_HasEffect()
    {
        // The trap this class carries: applying CREATES the placement, so its absence is a change --
        // and HasEffect must reach that answer without creating one itself.
        var (document, shape) = DocumentWithShape();
        shape.Placement.Should().BeNull();

        new SetShapePositionCommand(0, 0, 0, 0, HorizontalAnchor.Page, VerticalAnchor.Page)
            .HasEffect(new Ctx(document)).Should().BeTrue();

        shape.Placement.Should().BeNull("asking must not mutate the document");
    }

    [Fact]
    public void ReApplyingAShapesOwnPosition_HasNoEffect()
    {
        var (document, shape) = DocumentWithShape();
        shape.Placement = new FloatingPlacement
        {
            HorizontalOffsetPt = 12,
            VerticalOffsetPt = 34,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Paragraph,
        };

        new SetShapePositionCommand(0, 0, 12, 34, HorizontalAnchor.Margin, VerticalAnchor.Paragraph)
            .HasEffect(new Ctx(document)).Should().BeFalse();
    }

    [Fact]
    public void ACommandTargetingAMissingRun_HasNoEffect()
    {
        var (document, _) = DocumentWithImage();

        new SetImageSizeCommand(0, 99, 10, 10).HasEffect(new Ctx(document)).Should().BeFalse();
    }
}
