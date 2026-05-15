using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class CompositeWorkbookCommandTests
{
    [Fact]
    public void Apply_RunsCommandsAsSingleUndoableUnit()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);
        var command = new CompositeWorkbookCommand(
            "Grouped Edit",
            [
                EditCellsCommand.ForValue(sheet1.Id, new CellAddress(sheet1.Id, 1, 1), new TextValue("A")),
                EditCellsCommand.ForValue(sheet2.Id, new CellAddress(sheet2.Id, 1, 1), new TextValue("B"))
            ]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().BeEquivalentTo([
            new CellAddress(sheet1.Id, 1, 1),
            new CellAddress(sheet2.Id, 1, 1)
        ]);
        sheet1.GetValue(new CellAddress(sheet1.Id, 1, 1)).Should().Be(new TextValue("A"));
        sheet2.GetValue(new CellAddress(sheet2.Id, 1, 1)).Should().Be(new TextValue("B"));

        command.Revert(ctx);

        sheet1.GetCell(new CellAddress(sheet1.Id, 1, 1)).Should().BeNull();
        sheet2.GetCell(new CellAddress(sheet2.Id, 1, 1)).Should().BeNull();
    }

    [Fact]
    public void Apply_RevertsAlreadyAppliedCommandsWhenLaterCommandFails()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);
        sheet2.IsProtected = true;
        var command = new CompositeWorkbookCommand(
            "Grouped Edit",
            [
                EditCellsCommand.ForValue(sheet1.Id, new CellAddress(sheet1.Id, 1, 1), new TextValue("A")),
                EditCellsCommand.ForValue(sheet2.Id, new CellAddress(sheet2.Id, 1, 1), new TextValue("B"))
            ]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet1.GetCell(new CellAddress(sheet1.Id, 1, 1)).Should().BeNull();
        sheet2.GetCell(new CellAddress(sheet2.Id, 1, 1)).Should().BeNull();
    }

    [Fact]
    public void Apply_CanInsertPicturesAcrossGroupedSheetsAsOneUndoableUnit()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);
        var command = new CompositeWorkbookCommand(
            "Insert Picture",
            [
                new InsertPictureCommand(sheet1.Id, new CellAddress(sheet1.Id, 2, 3), [1, 2], "image/png"),
                new InsertPictureCommand(sheet2.Id, new CellAddress(sheet2.Id, 2, 3), [1, 2], "image/png")
            ]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet1.Pictures.Should().ContainSingle(p => p.Anchor.Row == 2 && p.Anchor.Col == 3);
        sheet2.Pictures.Should().ContainSingle(p => p.Anchor.Row == 2 && p.Anchor.Col == 3);

        command.Revert(ctx);

        sheet1.Pictures.Should().BeEmpty();
        sheet2.Pictures.Should().BeEmpty();
    }

    [Fact]
    public void Apply_CanResizeAndRotatePicturesAcrossGroupedSheetsAsOneUndoableUnit()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);
        var picture1 = new PictureModel { Anchor = new CellAddress(sheet1.Id, 2, 3), Width = 100, Height = 80 };
        var picture2 = new PictureModel { Anchor = new CellAddress(sheet2.Id, 2, 3), Width = 100, Height = 80 };
        sheet1.Pictures.Add(picture1);
        sheet2.Pictures.Add(picture2);
        var command = new CompositeWorkbookCommand(
            "Picture Format",
            [
                new ResizePictureCommand(sheet1.Id, picture1.Id, 160, 90),
                new RotatePictureCommand(sheet1.Id, picture1.Id, 30),
                new ResizePictureCommand(sheet2.Id, picture2.Id, 160, 90),
                new RotatePictureCommand(sheet2.Id, picture2.Id, 30)
            ]);

        command.Apply(ctx).Success.Should().BeTrue();

        picture1.Width.Should().Be(160);
        picture1.Height.Should().Be(90);
        picture1.RotationDegrees.Should().Be(30);
        picture2.Width.Should().Be(160);
        picture2.Height.Should().Be(90);
        picture2.RotationDegrees.Should().Be(30);

        command.Revert(ctx);

        picture1.Width.Should().Be(100);
        picture1.Height.Should().Be(80);
        picture1.RotationDegrees.Should().Be(0);
        picture2.Width.Should().Be(100);
        picture2.Height.Should().Be(80);
        picture2.RotationDegrees.Should().Be(0);
    }

    [Fact]
    public void Apply_CanInsertTextBoxesAndShapesAcrossGroupedSheetsAsOneUndoableUnit()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);
        var command = new CompositeWorkbookCommand(
            "Insert Objects",
            [
                new AddTextBoxCommand(sheet1.Id, new CellAddress(sheet1.Id, 4, 2), "Note"),
                new AddDrawingShapeCommand(sheet1.Id, new CellAddress(sheet1.Id, 5, 2), DrawingShapeKind.Rectangle),
                new AddTextBoxCommand(sheet2.Id, new CellAddress(sheet2.Id, 4, 2), "Note"),
                new AddDrawingShapeCommand(sheet2.Id, new CellAddress(sheet2.Id, 5, 2), DrawingShapeKind.Rectangle)
            ]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet1.TextBoxes.Should().ContainSingle(t => t.Anchor.Row == 4 && t.Anchor.Col == 2 && t.Text == "Note");
        sheet2.TextBoxes.Should().ContainSingle(t => t.Anchor.Row == 4 && t.Anchor.Col == 2 && t.Text == "Note");
        sheet1.DrawingShapes.Should().ContainSingle(s => s.Anchor.Row == 5 && s.Kind == DrawingShapeKind.Rectangle);
        sheet2.DrawingShapes.Should().ContainSingle(s => s.Anchor.Row == 5 && s.Kind == DrawingShapeKind.Rectangle);

        command.Revert(ctx);

        sheet1.TextBoxes.Should().BeEmpty();
        sheet2.TextBoxes.Should().BeEmpty();
        sheet1.DrawingShapes.Should().BeEmpty();
        sheet2.DrawingShapes.Should().BeEmpty();
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
