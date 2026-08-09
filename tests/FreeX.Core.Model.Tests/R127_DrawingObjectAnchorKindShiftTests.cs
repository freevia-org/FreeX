using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R127-editas-shift-gate: <c>RowColumnShiftHelpers.ShiftPictures</c>/<c>ShiftDrawingShapes</c>/
/// <c>ShiftTextBoxes</c> used to unconditionally apply twoCellAnchor ("move and size with cells")
/// semantics to every picture/shape/text box on row/column insert-delete, regardless of what the
/// source file's anchor actually declared. A picture loaded from a source .xlsx with
/// <c>xdr:twoCellAnchor editAs="oneCell"</c> ("move but don't size with cells") kept getting resized
/// whenever an insert/delete band fell inside its span, and one loaded as an <c>xdr:absoluteAnchor</c>
/// ("don't move or size with cells" -- always anchored at row 1/col 1 per
/// <c>XlsxDrawingAnchorApplier.ApplyToPicture</c>'s own doc comment) got relocated by any row/column
/// insert at or before row/col 1, i.e. almost any insert. <see cref="ChartModel.DrawingAnchorKind"/>
/// already gated this correctly for charts (<c>ShiftChartPositionRowsUp</c> etc., see
/// R86_ChartDrawingPositionShiftTests) -- this class pins the same gate now applying to
/// Picture/DrawingShape/TextBox via their new <c>DrawingAnchorKind</c> field.
/// </summary>
public sealed class R127_DrawingObjectAnchorKindShiftTests
{
    // ── OneCell: moves with the cell but never resizes ──────────────────────────────────────────

    [Fact]
    public void InsertRows_OneCellAnchorPicture_MovesButKeepsOriginalSize()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 5, 1),
            Width = 240,
            Height = 140,
            DrawingAnchorKind = ChartDrawingAnchorKind.OneCell,
        };
        sheet.Pictures.Add(picture);

        // Insert 3 rows at row 7 -- strictly inside the picture's row 5-11 span. A twoCellAnchor
        // picture would grow to Height=200 here (see R86_PictureShapeTextBoxResizeOnShiftTests); a
        // oneCellAnchor picture must keep its authored 140px height untouched.
        var command = new InsertRowsCommand(sheet.Id, beforeRow: 7, count: 3);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Pictures.Should().ContainSingle();
        picture.Height.Should().Be(140, "oneCellAnchor (\"move but don't size with cells\") must never resize");
        picture.Anchor.Should().Be(new CellAddress(sheet.Id, 5, 1), "the anchor row (5) is above the insert point (7)");

        command.Revert(ctx);
        picture.Height.Should().Be(140);
        picture.Anchor.Should().Be(new CellAddress(sheet.Id, 5, 1));
    }

    [Fact]
    public void InsertRows_OneCellAnchorPicture_AboveAnchorStillMovesTheAnchorCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 5, 1),
            Width = 240,
            Height = 140,
            DrawingAnchorKind = ChartDrawingAnchorKind.OneCell,
        };
        sheet.Pictures.Add(picture);

        // Insert 3 rows at row 2 -- entirely above the anchor. "Move but don't size" still MOVES: the
        // anchor cell tracks the shift like any move-with-cells object, only the size stays fixed.
        var command = new InsertRowsCommand(sheet.Id, beforeRow: 2, count: 3);
        command.Apply(ctx).Success.Should().BeTrue();

        picture.Anchor.Should().Be(new CellAddress(sheet.Id, 8, 1), "oneCellAnchor still moves with the cells");
        picture.Height.Should().Be(140);

        command.Revert(ctx);
        picture.Anchor.Should().Be(new CellAddress(sheet.Id, 5, 1));
    }

    // ── Absolute: never moves, never resizes ─────────────────────────────────────────────────────

    [Fact]
    public void InsertRows_AbsoluteAnchorPicture_AtRowOneStaysCompletelyFixed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        // An absoluteAnchor picture's loaded Anchor is always row 1/col 1 (its real sheet-relative
        // pixel position lives in AnchorOffsetX/Y) -- see XlsxDrawingAnchorApplier.ApplyToPicture.
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            AnchorOffsetX = 300,
            AnchorOffsetY = 150,
            Width = 240,
            Height = 140,
            DrawingAnchorKind = ChartDrawingAnchorKind.Absolute,
        };
        sheet.Pictures.Add(picture);

        // Insert a row at row 1 -- the worst case: shift.Start (1) <= anchor.Row (1), so a twoCell- or
        // oneCell-style ShiftAddress call would relocate the anchor to row 4. absoluteAnchor
        // ("don't move or size with cells") must stay pinned exactly where it was.
        var command = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 3);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Pictures.Should().ContainSingle();
        picture.Anchor.Should().Be(new CellAddress(sheet.Id, 1, 1), "absoluteAnchor never moves, even for an insert at row 1");
        picture.AnchorOffsetX.Should().Be(300);
        picture.AnchorOffsetY.Should().Be(150);
        picture.Width.Should().Be(240);
        picture.Height.Should().Be(140);

        command.Revert(ctx);
        picture.Anchor.Should().Be(new CellAddress(sheet.Id, 1, 1));
        picture.Height.Should().Be(140);
    }

    [Fact]
    public void InsertColumns_AbsoluteAnchorShape_StaysFixedAndDeleteColumnsAlsoLeavesItInPlace()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            AnchorOffsetX = 80,
            AnchorOffsetY = 40,
            Width = 120,
            Height = 70,
            DrawingAnchorKind = ChartDrawingAnchorKind.Absolute,
        };
        sheet.DrawingShapes.Add(shape);

        var insert = new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 2);
        insert.Apply(ctx).Success.Should().BeTrue();
        shape.Anchor.Should().Be(new CellAddress(sheet.Id, 1, 1));
        shape.Width.Should().Be(120);

        var delete = new DeleteColumnsCommand(sheet.Id, startCol: 1, count: 1);
        delete.Apply(ctx).Success.Should().BeTrue();
        shape.Anchor.Should().Be(new CellAddress(sheet.Id, 1, 1), "absoluteAnchor also ignores a delete landing on/before it");
        shape.Width.Should().Be(120);

        delete.Revert(ctx);
        insert.Revert(ctx);
        shape.Anchor.Should().Be(new CellAddress(sheet.Id, 1, 1));
        shape.Width.Should().Be(120);
    }

    // ── No-regression sibling: default (TwoCell / unset) text boxes keep the existing move+resize ──

    [Fact]
    public void InsertRows_DefaultTwoCellAnchorTextBox_StillGrowsHeight_NoRegression()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 5, 1),
            Width = 240,
            Height = 140,
            // DrawingAnchorKind intentionally left at its default (TwoCell) -- this is the ordinary
            // freshly-inserted-object case (Insert > Text Box) that must keep resizing exactly as
            // R86_PictureShapeTextBoxResizeOnShiftTests already pins for pictures.
        };
        sheet.TextBoxes.Add(textBox);

        textBox.DrawingAnchorKind.Should().Be(ChartDrawingAnchorKind.TwoCell, "TwoCell is the class default");

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 7, count: 3);
        command.Apply(ctx).Success.Should().BeTrue();

        textBox.Height.Should().Be(200, "an unset/TwoCell text box must keep the pre-existing move+resize behavior");
        textBox.Anchor.Should().Be(new CellAddress(sheet.Id, 5, 1));

        command.Revert(ctx);
        textBox.Height.Should().Be(140);
    }
}
