using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class AltTextCommandTests
{
    [Fact]
    public void SetPictureAltTextCommand_SetsAltTextAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            AltText = "Old picture text"
        };
        sheet.Pictures.Add(picture);

        var command = new SetPictureAltTextCommand(sheet.Id, picture.Id, "New picture text");

        command.Apply(ctx).Success.Should().BeTrue();
        picture.AltText.Should().Be("New picture text");

        command.Revert(ctx);

        picture.AltText.Should().Be("Old picture text");
    }

    [Fact]
    public void SetDrawingShapeAltTextCommand_SetsAltTextAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            AltText = "Old shape text"
        };
        sheet.DrawingShapes.Add(shape);

        var command = new SetDrawingShapeAltTextCommand(sheet.Id, shape.Id, "New shape text");

        command.Apply(ctx).Success.Should().BeTrue();
        shape.AltText.Should().Be("New shape text");

        command.Revert(ctx);

        shape.AltText.Should().Be("Old shape text");
    }

    [Fact]
    public void SetTextBoxAltTextCommand_SetsAltTextAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Text = "Note",
            AltText = "Old text box text"
        };
        sheet.TextBoxes.Add(textBox);

        var command = new SetTextBoxAltTextCommand(sheet.Id, textBox.Id, "New text box text");

        command.Apply(ctx).Success.Should().BeTrue();
        textBox.AltText.Should().Be("New text box text");

        command.Revert(ctx);

        textBox.AltText.Should().Be("Old text box text");
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
