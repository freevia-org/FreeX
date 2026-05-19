using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class DrawingTargetResolverTests
{
    [Fact]
    public void GetTargetPicture_PrefersLastPictureAnchoredAtSelectedCell()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var selected = new CellAddress(sheet.Id, 2, 2);
        var first = new PictureModel { Anchor = selected };
        var lastAtSelection = new PictureModel { Anchor = selected };
        var finalPicture = new PictureModel { Anchor = new CellAddress(sheet.Id, 5, 5) };
        sheet.Pictures.AddRange([first, lastAtSelection, finalPicture]);

        DrawingTargetResolver.GetTargetPicture(sheet, selected).Should().BeSameAs(lastAtSelection);
    }

    [Fact]
    public void GetTargetPicture_FallsBackToLastPictureWhenSelectionHasNoAnchorMatch()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var last = new PictureModel { Anchor = new CellAddress(sheet.Id, 5, 5) };
        sheet.Pictures.Add(new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1) });
        sheet.Pictures.Add(last);

        DrawingTargetResolver.GetTargetPicture(sheet, new CellAddress(sheet.Id, 2, 2)).Should().BeSameAs(last);
    }

    [Fact]
    public void GetTargetDrawingShape_PrefersLastShapeAnchoredAtSelectedCell()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var selected = new CellAddress(sheet.Id, 3, 3);
        var expected = new DrawingShapeModel { Anchor = selected };
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = selected });
        sheet.DrawingShapes.Add(expected);
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 4, 4) });

        DrawingTargetResolver.GetTargetDrawingShape(sheet, selected).Should().BeSameAs(expected);
    }

    [Fact]
    public void GetTargetDrawingObject_DefaultsToShapeBeforeTextboxWhenShapesExist()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Width = 111,
            Height = 55,
            RotationDegrees = 15,
            FillColor = new CellColor(1, 2, 3),
            OutlineColor = new CellColor(4, 5, 6)
        };
        sheet.DrawingShapes.Add(shape);
        sheet.TextBoxes.Add(new TextBoxModel { Anchor = new CellAddress(sheet.Id, 2, 2) });

        var target = DrawingTargetResolver.GetTargetDrawingObject(sheet, selectedAnchor: new CellAddress(sheet.Id, 2, 2));

        target.Should().NotBeNull();
        target!.Kind.Should().Be(DrawingObjectTargetKind.Shape);
        target.Id.Should().Be(shape.Id);
        target.Width.Should().Be(111);
        target.Height.Should().Be(55);
        target.RotationDegrees.Should().Be(15);
        target.FillColor.Should().Be(new CellColor(1, 2, 3));
        target.OutlineColor.Should().Be(new CellColor(4, 5, 6));
    }

    [Fact]
    public void GetTargetDrawingObject_HonorsPreferredTextbox()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var selected = new CellAddress(sheet.Id, 2, 2);
        var textBox = new TextBoxModel { Anchor = selected, Width = 90, Height = 40 };
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) });
        sheet.TextBoxes.Add(textBox);

        var target = DrawingTargetResolver.GetTargetDrawingObject(
            sheet,
            selected,
            DrawingObjectTargetKind.TextBox);

        target.Should().NotBeNull();
        target!.Kind.Should().Be(DrawingObjectTargetKind.TextBox);
        target.Id.Should().Be(textBox.Id);
        target.Width.Should().Be(90);
        target.Height.Should().Be(40);
    }
}
