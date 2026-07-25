using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R90-commands-shape-geometry-5-2: a column/row resize must resize a shape/picture/text box that is
/// anchored across the resized column(s)/row(s), matching Excel's default "Move and size with cells"
/// anchor behavior (FreeX does not model any other anchor mode, so every drawing object is treated as
/// the default). Exercises the real product entry point -- SetColumnWidthCommand/SetRowHeightCommand,
/// the same commands the ribbon's column/row-resize UI dispatches -- rather than constructing the
/// resized geometry directly.
/// </summary>
public class R90_ShapeColumnRowResizeCommandTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    [Fact]
    public void SetColumnWidthCommand_ResizesShapeAnchoredAcrossTheResizedColumn()
    {
        var (_, sheet, ctx) = Setup();
        // Column A is 8 char-width-units (64 px @ *8), column B likewise 8 units (64 px). A
        // rectangle anchored at B with no sub-cell offset, exactly as wide as column B, mirrors
        // the failure scenario: "insert a rectangle spanning column B".
        sheet.ColumnWidths[1] = 8;
        sheet.ColumnWidths[2] = 8;
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 2),
            AnchorOffsetX = 0,
            Width = 64,
            Height = 40,
        };
        sheet.DrawingShapes.Add(shape);

        // Double column B's width (8 -> 16 char-width-units, i.e. 64 -> 128 px).
        var cmd = new SetColumnWidthCommand(sheet.Id, 2, 2, 16);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        shape.Width.Should().Be(128, "the shape's default anchor mode is Move and size with cells, so widening its whole column should widen it by the same amount");

        cmd.Revert(ctx);

        shape.Width.Should().Be(64, "undo must restore the shape's pre-resize width");
    }

    [Fact]
    public void SetColumnWidthCommand_DoesNotResizeShapeAnchoredInAnUnrelatedColumn()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ColumnWidths[1] = 8;
        sheet.ColumnWidths[2] = 8;
        // Shape anchored entirely within column A -- resizing column B must leave it untouched.
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            AnchorOffsetX = 0,
            Width = 64,
            Height = 40,
        };
        sheet.DrawingShapes.Add(shape);

        var cmd = new SetColumnWidthCommand(sheet.Id, 2, 2, 16);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        shape.Width.Should().Be(64, "a shape anchored in a column that wasn't resized must not change size");

        cmd.Revert(ctx);

        shape.Width.Should().Be(64);
    }

    [Fact]
    public void SetRowHeightCommand_ResizesPictureAnchoredAcrossTheResizedRow()
    {
        var (_, sheet, ctx) = Setup();
        sheet.RowHeights[1] = 20;
        sheet.RowHeights[2] = 20;
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 1),
            AnchorOffsetY = 0,
            Height = 20,
        };
        sheet.Pictures.Add(picture);

        var cmd = new SetRowHeightCommand(sheet.Id, 2, 2, 40);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        picture.Height.Should().Be(40, "the picture's default anchor mode is Move and size with cells, so heightening its whole row should heighten it by the same amount");

        cmd.Revert(ctx);

        picture.Height.Should().Be(20, "undo must restore the picture's pre-resize height");
    }
}
