using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class SetDrawingObjectRotationCommandTests
{
    [Fact]
    public void SetsShapeRotationAndUndoRestoresPriorValue()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), RotationDegrees = 15 };
        sheet.DrawingShapes.Add(shape);

        var command = new SetDrawingObjectRotationCommand(sheet.Id, SelectionPaneObjectKind.Shape, shape.Id, 90);

        command.Apply(ctx).Success.Should().BeTrue();
        shape.RotationDegrees.Should().Be(90);

        command.Revert(ctx);

        shape.RotationDegrees.Should().Be(15);
    }

    [Fact]
    public void SetsPictureRotationAndUndoRestoresPriorValue()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var picture = new PictureModel { Anchor = new CellAddress(sheet.Id, 2, 3), RotationDegrees = 30 };
        sheet.Pictures.Add(picture);

        var command = new SetDrawingObjectRotationCommand(sheet.Id, SelectionPaneObjectKind.Picture, picture.Id, 120);

        command.Apply(ctx).Success.Should().BeTrue();
        picture.RotationDegrees.Should().Be(120);

        command.Revert(ctx);

        picture.RotationDegrees.Should().Be(30);
    }

    [Fact]
    public void SetsTextBoxRotationAndUndoRestoresPriorValue()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 4, 2), Text = "hello", RotationDegrees = 5 };
        sheet.TextBoxes.Add(textBox);

        var command = new SetDrawingObjectRotationCommand(sheet.Id, SelectionPaneObjectKind.TextBox, textBox.Id, 270);

        command.Apply(ctx).Success.Should().BeTrue();
        textBox.RotationDegrees.Should().Be(270);

        command.Revert(ctx);

        textBox.RotationDegrees.Should().Be(5);
    }

    [Fact]
    public void NormalizesRotationOutsideZeroToThreeSixty()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        sheet.DrawingShapes.Add(shape);

        new SetDrawingObjectRotationCommand(sheet.Id, SelectionPaneObjectKind.Shape, shape.Id, 450)
            .Apply(ctx).Success.Should().BeTrue();

        shape.RotationDegrees.Should().Be(90);
    }

    [Fact]
    public void RejectsNonFiniteRotation()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), RotationDegrees = 10 };
        sheet.DrawingShapes.Add(shape);

        new SetDrawingObjectRotationCommand(sheet.Id, SelectionPaneObjectKind.Shape, shape.Id, double.NaN)
            .Apply(ctx).Success.Should().BeFalse();

        shape.RotationDegrees.Should().Be(10);
    }

    [Fact]
    public void RejectsMissingObject()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);

        new SetDrawingObjectRotationCommand(sheet.Id, SelectionPaneObjectKind.Shape, Guid.NewGuid(), 45)
            .Apply(ctx).Success.Should().BeFalse();
    }

    [Fact]
    public void RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), RotationDegrees = 10 };
        sheet.DrawingShapes.Add(shape);
        sheet.IsProtected = true;

        new SetDrawingObjectRotationCommand(sheet.Id, SelectionPaneObjectKind.Shape, shape.Id, 45)
            .Apply(ctx).Success.Should().BeFalse();

        shape.RotationDegrees.Should().Be(10);
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
