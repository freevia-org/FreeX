using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class ShapeCommandTests
{
    [Theory]
    [InlineData(DrawingShapeKind.Rectangle)]
    [InlineData(DrawingShapeKind.Ellipse)]
    [InlineData(DrawingShapeKind.Line)]
    public void AddDrawingShapeCommand_AddsShapeAndUndoRemovesIt(DrawingShapeKind kind)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var anchor = new CellAddress(sheet.Id, 4, 2);

        var command = new AddDrawingShapeCommand(sheet.Id, anchor, kind, 120, 70);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.DrawingShapes.Should().ContainSingle();
        sheet.DrawingShapes[0].Anchor.Should().Be(anchor);
        sheet.DrawingShapes[0].Kind.Should().Be(kind);
        sheet.DrawingShapes[0].Width.Should().Be(120);
        sheet.DrawingShapes[0].Height.Should().Be(70);

        command.Revert(ctx);

        sheet.DrawingShapes.Should().BeEmpty();
    }

    [Fact]
    public void AddDrawingShapeCommand_RejectsShapeOnDifferentSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);

        var command = new AddDrawingShapeCommand(
            sheet1.Id,
            new CellAddress(sheet2.Id, 1, 1),
            DrawingShapeKind.Rectangle);

        command.Apply(ctx).Success.Should().BeFalse();
        sheet1.DrawingShapes.Should().BeEmpty();
    }

    [Fact]
    public void AddDrawingShapeCommand_RejectsInvalidShapeKind()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);

        var command = new AddDrawingShapeCommand(
            sheet.Id,
            new CellAddress(sheet.Id, 1, 1),
            (DrawingShapeKind)99);

        command.Apply(ctx).Success.Should().BeFalse();
        sheet.DrawingShapes.Should().BeEmpty();
    }

    [Fact]
    public void AddDrawingShapeCommand_RejectsInvalidInitialSize()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var anchor = new CellAddress(sheet.Id, 4, 2);

        new AddDrawingShapeCommand(sheet.Id, anchor, DrawingShapeKind.Rectangle, double.PositiveInfinity, 70)
            .Apply(ctx).Success.Should().BeFalse();
        new AddDrawingShapeCommand(sheet.Id, anchor, DrawingShapeKind.Rectangle, 120, double.NaN)
            .Apply(ctx).Success.Should().BeFalse();
        new AddDrawingShapeCommand(sheet.Id, anchor, DrawingShapeKind.Rectangle, -1, 70)
            .Apply(ctx).Success.Should().BeFalse();

        sheet.DrawingShapes.Should().BeEmpty();
    }

    [Fact]
    public void BringDrawingShapeForwardCommand_MovesShapeLaterAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var back = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Kind = DrawingShapeKind.Rectangle };
        var front = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Kind = DrawingShapeKind.Ellipse };
        sheet.DrawingShapes.Add(back);
        sheet.DrawingShapes.Add(front);

        var command = new BringDrawingShapeForwardCommand(sheet.Id, back.Id);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.DrawingShapes.Should().Equal(front, back);

        command.Revert(ctx);

        sheet.DrawingShapes.Should().Equal(back, front);
    }

    [Fact]
    public void SendDrawingShapeBackwardCommand_MovesShapeEarlierAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var back = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Kind = DrawingShapeKind.Rectangle };
        var front = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Kind = DrawingShapeKind.Ellipse };
        sheet.DrawingShapes.Add(back);
        sheet.DrawingShapes.Add(front);

        var command = new SendDrawingShapeBackwardCommand(sheet.Id, front.Id);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.DrawingShapes.Should().Equal(front, back);

        command.Revert(ctx);

        sheet.DrawingShapes.Should().Equal(back, front);
    }

    [Fact]
    public void ResizeDrawingShapeCommand_SetsSizeAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Width = 120, Height = 70 };
        sheet.DrawingShapes.Add(shape);

        var command = new ResizeDrawingShapeCommand(sheet.Id, shape.Id, 160, 90);

        command.Apply(ctx).Success.Should().BeTrue();
        shape.Width.Should().Be(160);
        shape.Height.Should().Be(90);

        command.Revert(ctx);

        shape.Width.Should().Be(120);
        shape.Height.Should().Be(70);
    }

    [Fact]
    public void RotateDrawingShapeCommand_SetsRotationAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), RotationDegrees = 15 };
        sheet.DrawingShapes.Add(shape);

        var command = new RotateDrawingShapeCommand(sheet.Id, shape.Id, 450);

        command.Apply(ctx).Success.Should().BeTrue();
        shape.RotationDegrees.Should().Be(90);

        command.Revert(ctx);

        shape.RotationDegrees.Should().Be(15);
    }

    [Fact]
    public void ShapeFormattingCommands_RejectInvalidNumbers()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Width = 120, Height = 70 };
        sheet.DrawingShapes.Add(shape);

        new ResizeDrawingShapeCommand(sheet.Id, shape.Id, double.NaN, 90)
            .Apply(ctx).Success.Should().BeFalse();
        new RotateDrawingShapeCommand(sheet.Id, shape.Id, double.PositiveInfinity)
            .Apply(ctx).Success.Should().BeFalse();

        shape.Width.Should().Be(120);
        shape.Height.Should().Be(70);
        shape.RotationDegrees.Should().Be(0);
    }

    [Fact]
    public void SetDrawingShapeColorsCommand_SetsColorsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            FillColor = new CellColor(10, 20, 30),
            OutlineColor = new CellColor(40, 50, 60),
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.25),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25)
        };
        sheet.DrawingShapes.Add(shape);

        var command = new SetDrawingShapeColorsCommand(
            sheet.Id,
            shape.Id,
            new CellColor(200, 210, 220),
            new CellColor(30, 40, 50));

        command.Apply(ctx).Success.Should().BeTrue();
        shape.FillColor.Should().Be(new CellColor(200, 210, 220));
        shape.OutlineColor.Should().Be(new CellColor(30, 40, 50));
        shape.FillThemeColor.Should().BeNull();
        shape.OutlineThemeColor.Should().BeNull();

        command.Revert(ctx);

        shape.FillColor.Should().Be(new CellColor(10, 20, 30));
        shape.OutlineColor.Should().Be(new CellColor(40, 50, 60));
        shape.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.25));
        shape.OutlineThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25));
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
