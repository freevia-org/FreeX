using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R149-remediation: the r149 fix taught <c>DuplicateSheetDrawingCloner.ClonePicture</c> to carry
/// <see cref="PictureModel.IsDecorative"/> forward but left its two siblings --
/// <see cref="DuplicateSheetDrawingCloner.CloneDrawingShape"/> and
/// <see cref="DuplicateSheetDrawingCloner.CloneTextBox"/> -- dropping the analogous
/// <see cref="DrawingShapeModel.IsDecorative"/>/<see cref="TextBoxModel.IsDecorative"/> flags. Both
/// cloners are shared by every duplication gesture, not just Duplicate Sheet: this file drives the
/// other two named in the gap report -- a single-object Ctrl+C/Ctrl+V
/// (<see cref="DuplicateDrawingObjectCommand"/>) and a plain range copy/paste that carries a
/// floating object along (<see cref="PasteShapesCommand"/>/<see cref="PasteTextBoxesCommand"/>) --
/// confirming the same cloner fix closes all of them at once.
/// </summary>
public sealed class R149_DrawingObjectDecorativeCloneGestureTests
{
    // ---------------------------------------------------------------- Ctrl+C/Ctrl+V (DuplicateDrawingObjectCommand)

    [Fact]
    public void DuplicateDrawingObjectCommand_DecorativeShape_PreservesIsDecorativeOnCopy()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var anchor = new CellAddress(sheet.Id, 2, 2);
        new AddDrawingShapeCommand(sheet.Id, anchor, DrawingShapeKind.Rectangle).Apply(ctx).Success.Should().BeTrue();
        var originalShape = sheet.DrawingShapes[0];
        originalShape.IsDecorative = true;

        var command = new DuplicateDrawingObjectCommand(sheet.Id, sheet.Id, SelectionPaneObjectKind.Shape, originalShape.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var duplicate = sheet.DrawingShapes.Single(s => s.Id != originalShape.Id);
        duplicate.IsDecorative.Should().BeTrue(
            "Ctrl+C/Ctrl+V on a decorative shape must not drop the 'Mark as decorative' flag");
    }

    [Fact]
    public void DuplicateDrawingObjectCommand_DecorativeTextBox_PreservesIsDecorativeOnCopy()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 100,
            Height = 60,
            IsDecorative = true
        });
        var originalTextBox = sheet.TextBoxes[0];

        var command = new DuplicateDrawingObjectCommand(sheet.Id, sheet.Id, SelectionPaneObjectKind.TextBox, originalTextBox.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var duplicate = sheet.TextBoxes.Single(t => t.Id != originalTextBox.Id);
        duplicate.IsDecorative.Should().BeTrue(
            "Ctrl+C/Ctrl+V on a decorative text box must not drop the 'Mark as decorative' flag");
    }

    // ---------------------------------------------------------------- Range paste carry (PasteShapesCommand/PasteTextBoxesCommand)

    [Fact]
    public void PasteShapesCommand_RangeCopyCarry_DecorativeShape_PreservesIsDecorativeOnCopy()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = DrawingShapeKind.Rectangle,
            IsDecorative = true
        };
        sheet.DrawingShapes.Add(shape);

        var destination = new CellAddress(sheet.Id, 10, 10);
        var command = new PasteShapesCommand(
            sheet.Id, new GridRange(shape.Anchor, shape.Anchor), destination, [shape], transpose: false);
        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.DrawingShapes.Single(s => s.Id != shape.Id);
        pasted.IsDecorative.Should().BeTrue(
            "a range paste that carries a decorative shape along must not drop the flag");
    }

    [Fact]
    public void PasteTextBoxesCommand_RangeCopyCarry_DecorativeTextBox_PreservesIsDecorativeOnCopy()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Width = 100,
            Height = 60,
            IsDecorative = true
        };
        sheet.TextBoxes.Add(textBox);

        var destination = new CellAddress(sheet.Id, 10, 10);
        var command = new PasteTextBoxesCommand(
            sheet.Id, new GridRange(textBox.Anchor, textBox.Anchor), destination, [textBox], transpose: false);
        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.TextBoxes.Single(t => t.Id != textBox.Id);
        pasted.IsDecorative.Should().BeTrue(
            "a range paste that carries a decorative text box along must not drop the flag");
    }
}
