using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// freex-hyperlinks F1: a drawing object's (shape/textbox/picture) internal ('Place in This
/// Document') <see cref="DrawingObjectHyperlink"/> target is a verbatim cell/sheet reference (e.g.
/// "A10" or "Sheet1!A10" -- see DrawingObjectHyperlink's own doc comment) that must shift the same
/// way FormulaRewriter already shifts a formula/named-range/hyperlink-bookmark reference on a row or
/// column insert/delete -- but ShiftDrawingShapes/ShiftTextBoxes/ShiftPictures
/// (RowColumnShiftHelpers.AddressState.cs) relocated the object's Anchor/Width/Height (and, for
/// pictures, LinkedSourceRange) on every such structural edit while never touching this field, so
/// the hyperlink kept pointing at the pre-shift cell forever. Mirrors R107's identical fix for
/// RenameSheetCommand/RemoveSheetCommand (DrawingObjectHyperlinkRewriter in SheetCommands.cs) and
/// R106's for DuplicateSheetCommand, extended here to InsertRowsCommand/DeleteRowsCommand/
/// InsertColumnsCommand/DeleteColumnsCommand.
/// </summary>
public sealed class FreeXHyperlinksF1_DrawingObjectHyperlinkRowColumnShiftTests
{
    [Fact]
    public void InsertRows_ShapeBareHyperlinkBelowInsertPoint_TargetShiftsWithTheCellItPointsTo()
    {
        var workbook = new Workbook("ShapeBareHyperlinkInsertRows");
        var sheet = workbook.AddSheet("Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 20, 1),
            Kind = DrawingShapeKind.Rectangle,
            Hyperlink = new DrawingObjectHyperlink("A10")
        };
        sheet.DrawingShapes.Add(shape);
        var ctx = new TestCommandContext(workbook);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 3);
        command.Apply(ctx).Success.Should().BeTrue();

        var shifted = sheet.DrawingShapes.Should().ContainSingle().Subject;
        shifted.Anchor.Row.Should().Be(23, because: "the anchor cell itself must still relocate as before this fix");
        shifted.Hyperlink.Should().NotBeNull();
        shifted.Hyperlink!.Target.Should().Be("A13",
            because: "A10 (below the insert point) must follow the data to its new row, matching the ordinary cell-hyperlink mechanism");

        command.Revert(ctx);
        var reverted = sheet.DrawingShapes.Should().ContainSingle().Subject;
        reverted.Hyperlink!.Target.Should().Be("A10", because: "undo must restore the exact pre-insert hyperlink target");
    }

    [Fact]
    public void DeleteRows_TextBoxSheetQualifiedHyperlink_TargetShiftsWithTheCellItPointsTo()
    {
        var workbook = new Workbook("TextBoxHyperlinkDeleteRows");
        var sheet = workbook.AddSheet("Sheet1");
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 30, 1),
            Text = "Back to summary",
            Hyperlink = new DrawingObjectHyperlink("Sheet1!A20")
        };
        sheet.TextBoxes.Add(textBox);
        var ctx = new TestCommandContext(workbook);

        var command = new DeleteRowsCommand(sheet.Id, startRow: 5, count: 3);
        command.Apply(ctx).Success.Should().BeTrue();

        var shifted = sheet.TextBoxes.Should().ContainSingle().Subject;
        shifted.Anchor.Row.Should().Be(27);
        shifted.Hyperlink!.Target.Should().Be("Sheet1!A17",
            because: "A20 (below the deleted band) must follow the data up to its new row");

        command.Revert(ctx);
        var reverted = sheet.TextBoxes.Should().ContainSingle().Subject;
        reverted.Hyperlink!.Target.Should().Be("Sheet1!A20", because: "undo must restore the exact pre-delete hyperlink target");
    }

    [Fact]
    public void InsertColumns_PictureHyperlink_TargetShiftsWithTheCellItPointsTo()
    {
        var workbook = new Workbook("PictureHyperlinkInsertColumns");
        var sheet = workbook.AddSheet("Sheet1");
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 20),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3],
            ContentType = "image/png",
            Hyperlink = new DrawingObjectHyperlink("J1")
        };
        sheet.Pictures.Add(picture);
        var ctx = new TestCommandContext(workbook);

        var command = new InsertColumnsCommand(sheet.Id, beforeCol: 5, count: 2);
        command.Apply(ctx).Success.Should().BeTrue();

        var shifted = sheet.Pictures.Should().ContainSingle().Subject;
        shifted.Hyperlink!.Target.Should().Be("L1",
            because: "column J (index 10, at/after the insert point) must shift right by the inserted column count");

        command.Revert(ctx);
        var reverted = sheet.Pictures.Should().ContainSingle().Subject;
        reverted.Hyperlink!.Target.Should().Be("J1", because: "undo must restore the exact pre-insert hyperlink target");
    }

    // Sibling no-regression case: an external ("Existing File or Web Page") hyperlink must never be
    // treated as a cell reference, even when its Target text happens to look like one.
    [Fact]
    public void InsertRows_ShapeExternalHyperlink_IsNeverRewritten()
    {
        var workbook = new Workbook("ShapeHyperlinkExternal");
        var sheet = workbook.AddSheet("Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 20, 1),
            Kind = DrawingShapeKind.Rectangle,
            Hyperlink = new DrawingObjectHyperlink("https://example.com/A10", TargetMode: "External")
        };
        sheet.DrawingShapes.Add(shape);
        var ctx = new TestCommandContext(workbook);

        new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 3).Apply(ctx).Success.Should().BeTrue();

        var shifted = sheet.DrawingShapes.Should().ContainSingle().Subject;
        shifted.Hyperlink!.Target.Should().Be("https://example.com/A10",
            because: "an external hyperlink target must never be rewritten by a structural row/column edit");
        shifted.Hyperlink!.TargetMode.Should().Be("External");
    }

    // Sibling no-regression case: a hyperlink pointing ABOVE the insert point (or, for a delete,
    // entirely outside the deleted band) must be left untouched -- only references at/after the
    // shift point move.
    [Fact]
    public void InsertRows_ShapeHyperlinkAboveInsertPoint_IsUnchanged()
    {
        var workbook = new Workbook("ShapeHyperlinkAboveInsert");
        var sheet = workbook.AddSheet("Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 20, 1),
            Kind = DrawingShapeKind.Rectangle,
            Hyperlink = new DrawingObjectHyperlink("A2")
        };
        sheet.DrawingShapes.Add(shape);
        var ctx = new TestCommandContext(workbook);

        new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 3).Apply(ctx).Success.Should().BeTrue();

        var shifted = sheet.DrawingShapes.Should().ContainSingle().Subject;
        shifted.Hyperlink!.Target.Should().Be("A2",
            because: "a reference above the insert point is never shifted by an insert");
    }
}
