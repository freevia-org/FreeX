using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R86-commands-insert-move-refadjust-5-2: whole-row/whole-column Insert/Delete only moved a
/// Picture/DrawingShape/TextBox's single-CellAddress Anchor (via ShiftAddress) — it never grew or
/// shrank Height/Width when the insert/delete band fell INSIDE the object's existing pixel span, the
/// default Excel "Move and size with cells" (twoCellAnchor) behavior. A picture anchored at row 5
/// with Height=140 (spanning rows 5-11 at the default 20px row height) kept its pre-insert pixel
/// height after 3 rows were inserted at row 7 (inside its span, below its anchor) — silently ending
/// up covering fewer of the shifted rows than it originally spanned.
/// </summary>
public sealed class R86_PictureShapeTextBoxResizeOnShiftTests
{
    // ── Finding: a picture must grow when the insert band falls inside its vertical span ──────────

    [Fact]
    public void InsertRows_PictureAnchoredAboveInsertBand_GrowsHeightAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        // sheet.DefaultRowHeight is 20 by default: a picture anchored at row 5 with Height=140 spans
        // rows 5-11 (top edge (5-1)*20=80, bottom edge 80+140=220, i.e. up to but not including row 12).
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 5, 1),
            Width = 240,
            Height = 140,
        };
        sheet.Pictures.Add(picture);

        // Insert 3 rows at row 7 — strictly inside the picture's row 5-11 span, below its anchor.
        var command = new InsertRowsCommand(sheet.Id, beforeRow: 7, count: 3);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Pictures.Should().ContainSingle();
        picture.Anchor.Should().Be(new CellAddress(sheet.Id, 5, 1),
            "the anchor (row 5) is above the insert point (row 7) and is left unchanged, matching the finding scenario");
        picture.Height.Should().Be(200,
            "3 inserted rows fall inside the picture's original row 5-11 span, so its bottom edge must keep " +
            "tracking the same underlying row by growing 3*20=60px, not silently cover fewer rows than before");
        picture.Width.Should().Be(240, "no columns were inserted");

        command.Revert(ctx);
        picture.Height.Should().Be(140);
        picture.Anchor.Should().Be(new CellAddress(sheet.Id, 5, 1));
    }

    // ── No-regression sibling: an insert ABOVE the anchor moves the whole object, no resize ────────

    [Fact]
    public void InsertRows_PictureAnchoredBelowInsertBand_ShiftsWithoutResizingAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 5, 1),
            Width = 240,
            Height = 140,
        };
        sheet.Pictures.Add(picture);

        // Insert 3 rows at row 2 — entirely above the picture's anchor, so the whole picture moves
        // down as a rigid block instead of being resized.
        var command = new InsertRowsCommand(sheet.Id, beforeRow: 2, count: 3);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Pictures.Should().ContainSingle();
        picture.Anchor.Should().Be(new CellAddress(sheet.Id, 8, 1), "the anchor row (5) is at/after the insert point and moves down by 3");
        picture.Height.Should().Be(140, "the insert point is above the anchor, so the picture moves as a whole and must not resize");

        command.Revert(ctx);
        picture.Anchor.Should().Be(new CellAddress(sheet.Id, 5, 1));
        picture.Height.Should().Be(140);
    }

    [Fact]
    public void DeleteRows_PictureAnchoredAboveDeleteBand_ShrinksHeightAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        // Picture spans rows 5-11 (Height=200 => bottom edge 80+200=280, i.e. up to row 14).
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 5, 1),
            Width = 240,
            Height = 200,
        };
        sheet.Pictures.Add(picture);

        // Delete 3 rows starting at row 7 — inside the picture's span, below its anchor.
        var command = new DeleteRowsCommand(sheet.Id, startRow: 7, count: 3);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Pictures.Should().ContainSingle();
        picture.Anchor.Should().Be(new CellAddress(sheet.Id, 5, 1));
        picture.Height.Should().Be(140,
            "3 deleted rows fall inside the picture's span, so its bottom edge must keep tracking the same " +
            "underlying row by shrinking 3*20=60px, not silently keep covering rows that no longer exist there");

        command.Revert(ctx);
        picture.Height.Should().Be(200);
    }
}
