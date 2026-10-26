using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r218: nine commands that are equal-value setters wearing a structural verb. Reposition, Resize
/// and Rotate assign a value the target may already hold -- exactly the shape r207 measured at ~90%
/// defective -- but none of them is named Set*, which is why the r208 scope filter never saw them.
/// The filter was on the NAME; the class is about the SHAPE.
/// <para>
/// The gestures are ordinary. A drag that ends in the cell it began in (picked up and put back, or
/// moved less than one cell) issues a Reposition to the current anchor; Size and Properties
/// pre-fills the current width and height, so tabbing out re-submits them; the rotation box
/// pre-fills the current angle.
/// </para>
/// <para>
/// Two details each carry a lesson from an earlier round. The rotation guards compare against the
/// NORMALISED angle, because that is what Apply writes -- so 370 degrees on an object already at 10
/// is correctly no change. And RotateTextBoxCommand clears IsSourceLoaded (R62) so the writer
/// re-emits the object; correct for a real rotation, but on a re-submitted angle it would throw away
/// a loaded text box's preserved source XML for nothing.
/// </para>
/// </summary>
public sealed class R218_ObjectTransformNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    private static PictureModel Picture(Sheet sheet)
    {
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 2),
            Width = 120,
            Height = 90,
            RotationDegrees = 10,
        };
        sheet.Pictures.Add(picture);
        return picture;
    }

    private static DrawingShapeModel Shape(Sheet sheet)
    {
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 2),
            Width = 120,
            Height = 90,
            RotationDegrees = 10,
        };
        sheet.DrawingShapes.Add(shape);
        return shape;
    }

    private static TextBoxModel TextBox(Sheet sheet)
    {
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 2),
            Width = 120,
            Height = 90,
            RotationDegrees = 10,
        };
        sheet.TextBoxes.Add(textBox);
        return textBox;
    }

    [Fact]
    public void DroppingAPictureBackWhereItWas_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var picture = Picture(sheet);

        new RepositionPictureCommand(sheet.Id, picture.Id, picture.Anchor).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void MovingAPictureToAnotherCell_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var picture = Picture(sheet);
        var target = new CellAddress(sheet.Id, 9, 4);

        var outcome = new RepositionPictureCommand(sheet.Id, picture.Id, target).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        picture.Anchor.Should().Be(target);
    }

    [Fact]
    public void ReSubmittingAPicturesOwnSize_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var picture = Picture(sheet);

        new ResizePictureCommand(sheet.Id, picture.Id, picture.Width, picture.Height).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ReSubmittingAPicturesOwnSizeWhileFlippingIt_DoesNotReportNoOp()
    {
        // The flip arguments are optional and part of the same command, so an unchanged size with a
        // changed flip is a real edit. A guard that only compared width and height would have
        // suppressed it -- the dangerous direction.
        var (sheet, ctx) = Fixture();
        var picture = Picture(sheet);

        new ResizePictureCommand(
                sheet.Id, picture.Id, picture.Width, picture.Height, flipHorizontal: true)
            .Apply(ctx)
            .IsNoOp.Should().BeFalse();
        picture.FlipHorizontal.Should().BeTrue();
    }

    [Fact]
    public void ResizingAPicture_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var picture = Picture(sheet);

        new ResizePictureCommand(sheet.Id, picture.Id, 200, picture.Height).Apply(ctx)
            .IsNoOp.Should().BeFalse();
        picture.Width.Should().Be(200);
    }

    [Fact]
    public void ReSubmittingAPicturesOwnRotation_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var picture = Picture(sheet);

        new RotatePictureCommand(sheet.Id, picture.Id, 10).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void AskingForARotationThatNormalisesToTheCurrentOne_ReportsNoOp()
    {
        // 370 normalises to 10, which is where the picture already is. Comparing against the raw
        // request instead of the value Apply writes would have called this a real edit.
        var (sheet, ctx) = Fixture();
        var picture = Picture(sheet);

        new RotatePictureCommand(sheet.Id, picture.Id, 370).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void RotatingAPicture_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var picture = Picture(sheet);

        new RotatePictureCommand(sheet.Id, picture.Id, 45).Apply(ctx).IsNoOp.Should().BeFalse();
        picture.RotationDegrees.Should().Be(45);
    }

    [Fact]
    public void DroppingAShapeBackWhereItWas_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var shape = Shape(sheet);

        new RepositionShapeCommand(sheet.Id, shape.Id, shape.Anchor).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void DroppingAShapeBackWhereItWas_KeepsItsSubCellOffset()
    {
        // The offset snap below the guard only runs when the anchor changed, so an equal anchor was
        // already a no-op in fact. This pins that it stays one.
        var (sheet, ctx) = Fixture();
        var shape = Shape(sheet);
        shape.AnchorOffsetX = 4.5;
        shape.AnchorOffsetY = 2.25;

        new RepositionShapeCommand(sheet.Id, shape.Id, shape.Anchor).Apply(ctx);

        shape.AnchorOffsetX.Should().Be(4.5);
        shape.AnchorOffsetY.Should().Be(2.25);
    }

    [Fact]
    public void ReSubmittingAShapesOwnSize_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var shape = Shape(sheet);

        new ResizeDrawingShapeCommand(sheet.Id, shape.Id, shape.Width, shape.Height).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ResizingAShape_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var shape = Shape(sheet);

        new ResizeDrawingShapeCommand(sheet.Id, shape.Id, shape.Width, 300).Apply(ctx)
            .IsNoOp.Should().BeFalse();
        shape.Height.Should().Be(300);
    }

    [Fact]
    public void ReSubmittingAShapesOwnRotation_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var shape = Shape(sheet);

        new RotateDrawingShapeCommand(sheet.Id, shape.Id, 370).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void DroppingATextBoxBackWhereItWas_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var textBox = TextBox(sheet);

        new RepositionTextBoxCommand(sheet.Id, textBox.Id, textBox.Anchor).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ReSubmittingATextBoxesOwnSize_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var textBox = TextBox(sheet);

        new ResizeTextBoxCommand(sheet.Id, textBox.Id, textBox.Width, textBox.Height).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ReSubmittingATextBoxesOwnRotation_KeepsItsLoadedSourceXml()
    {
        // The point of the text-box rotation guard. R62 clears IsSourceLoaded so the writer re-emits
        // the object -- necessary for a real rotation, wasteful for one that did not move.
        var (sheet, ctx) = Fixture();
        var textBox = TextBox(sheet);
        textBox.IsSourceLoaded = true;

        new RotateTextBoxCommand(sheet.Id, textBox.Id, 10).Apply(ctx).IsNoOp.Should().BeTrue();

        textBox.IsSourceLoaded.Should().BeTrue();
    }

    [Fact]
    public void RotatingATextBoxForReal_StillClearsItsLoadedSourceXml()
    {
        var (sheet, ctx) = Fixture();
        var textBox = TextBox(sheet);
        textBox.IsSourceLoaded = true;

        var outcome = new RotateTextBoxCommand(sheet.Id, textBox.Id, 45).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        textBox.RotationDegrees.Should().Be(45);
        textBox.IsSourceLoaded.Should().BeFalse(
            "R62's reason for clearing it is untouched for a rotation that rotates something");
    }
}
