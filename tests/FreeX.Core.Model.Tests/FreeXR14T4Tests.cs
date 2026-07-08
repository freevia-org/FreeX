using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-14 bucket T4 regression tests:
///   - R14-camera-linked-picture-1: a cross-sheet linked picture's LinkedSourceRange must shift
///     (and its rendered snapshot refresh) when a structural row/column edit lands on the *source*
///     sheet, even though the picture itself is hosted on a different sheet.
///   - R14-form-controls-1: a form control's sub-cell AnchorOffsets must shift in lockstep with its
///     whole-cell Anchor on row/column insert/delete (and drive removal for VML-only-anchored
///     controls whose Anchor is null), mirroring Excel moving the control with its cell.
/// </summary>
public sealed class FreeXR14T4Tests
{
    // ── R14-camera-linked-picture-1 ─────────────────────────────────────────

    [Fact]
    public void CrossSheetLinkedPicture_InsertRowOnSourceSheet_ShiftsLinkedRangeAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);

        // Populate the live source data on Sheet2 (the sheet that gets structurally edited).
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new TextValue("X1"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 2), new TextValue("Y1"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new TextValue("X2"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 2), new TextValue("Y2"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 3, 1), new TextValue("X3"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 3, 2), new TextValue("Y3"));

        var sourceRange = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 3, 2));
        var sourceCells = new[]
        {
            (new CellAddress(sheet2.Id, 1, 1), "X1"),
            (new CellAddress(sheet2.Id, 1, 2), "Y1"),
            (new CellAddress(sheet2.Id, 2, 1), "X2"),
            (new CellAddress(sheet2.Id, 2, 2), "Y2"),
            (new CellAddress(sheet2.Id, 3, 1), "X3"),
            (new CellAddress(sheet2.Id, 3, 2), "Y3"),
        };

        // Paste-linked-picture across sheets: the picture is hosted on Sheet1 but linked to
        // Sheet2's A1:B3 (Excel's "camera" / Paste Special > Linked Picture).
        var pasteCommand = new PasteRangeAsPictureCommand(
            sheet1.Id,
            sourceRange,
            sourceCells,
            new CellAddress(sheet1.Id, 1, 1),
            isLinkedToSourceRange: true,
            sourceSheetName: "Sheet2");
        pasteCommand.Apply(ctx).Success.Should().BeTrue();

        var picture = sheet1.Pictures.Should().ContainSingle().Subject;
        picture.LinkedSourceRange.Should().Be(sourceRange);

        // Insert 1 row before Sheet2 row 1 — Excel's camera follows its source down to A2:B4 even
        // though the structural edit runs against Sheet2, not the picture's own hosting Sheet1.
        var insertCommand = new InsertRowsCommand(sheet2.Id, beforeRow: 1, count: 1);
        insertCommand.Apply(ctx).Success.Should().BeTrue();

        var expectedShiftedRange = new GridRange(new CellAddress(sheet2.Id, 2, 1), new CellAddress(sheet2.Id, 4, 2));
        picture.LinkedSourceRange.Should().Be(expectedShiftedRange,
            "the picture's cross-sheet linked source range must follow a structural insert on the source sheet");
        picture.IsLinkedToSourceRange.Should().BeTrue();
        picture.SourceRowCount.Should().Be(3);
        picture.SourceColumnCount.Should().Be(2);
        picture.Cells.Should().Contain(cell => cell.RowOffset == 0 && cell.ColumnOffset == 0 && cell.Text == "X1",
            "the cached snapshot must be rebuilt from the live grid at the new (post-shift) range");

        insertCommand.Revert(ctx);

        picture.LinkedSourceRange.Should().Be(sourceRange,
            "undo must restore the picture's original cross-sheet linked source range");
        picture.IsLinkedToSourceRange.Should().BeTrue();
    }

    // ── R14-form-controls-1 ─────────────────────────────────────────────────

    [Fact]
    public void FormControlAnchorOffsets_ShiftWithAnchorOnInsert_AndDriveRemovalForVmlOnlyAnchor()
    {
        // Part 1: a normal (Anchor + AnchorOffsets both set) control shifts both together, and
        // undo restores both.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            Anchor = new GridRange(new CellAddress(sheet.Id, 5, 2), new CellAddress(sheet.Id, 5, 2)),
            // 0-based sub-cell EMU offsets for the same B5 cell (row 5 -> 0-based row 4).
            AnchorOffsets = new DrawingAnchorRange(
                new DrawingAnchorPoint(1, 12345, 4, 6789),
                new DrawingAnchorPoint(1, 98765, 4, 43210)),
        };
        sheet.FormControls.Add(control);

        var insertCommand = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 3);
        insertCommand.Apply(ctx).Success.Should().BeTrue();

        control.Anchor!.Value.Start.Row.Should().Be(8, "Anchor shifts from row 5 to row 8 after inserting 3 rows above");
        control.AnchorOffsets.Should().NotBeNull();
        control.AnchorOffsets!.From.Row.Should().Be(7, "AnchorOffsets' 0-based row must shift in lockstep with Anchor (row 8 -> 0-based 7)");
        control.AnchorOffsets.To.Row.Should().Be(7);
        control.AnchorOffsets.From.Column.Should().Be(1, "column is untouched by a row insert");
        control.AnchorOffsets.From.ColumnOffsetEmu.Should().Be(12345, "sub-cell EMU offsets never change, only the cell they anchor to");
        control.AnchorOffsets.From.RowOffsetEmu.Should().Be(6789);
        control.AnchorOffsets.To.ColumnOffsetEmu.Should().Be(98765);
        control.AnchorOffsets.To.RowOffsetEmu.Should().Be(43210);

        insertCommand.Revert(ctx);

        control.Anchor!.Value.Start.Row.Should().Be(5, "undo restores the original Anchor row");
        control.AnchorOffsets!.From.Row.Should().Be(4, "undo restores the original AnchorOffsets row");
        control.AnchorOffsets.To.Row.Should().Be(4);

        // Part 2: a VML-only-anchored control (Anchor null, AnchorOffsets set) is removed when its
        // row is deleted, exactly like a normal Anchor-bearing control would be.
        var wb2 = new Workbook("test2");
        var sheet2 = wb2.AddSheet("Sheet1");
        var ctx2 = new TestCommandContext(wb2);

        var vmlOnlyControl = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            Anchor = null,
            AnchorOffsets = new DrawingAnchorRange(
                new DrawingAnchorPoint(0, 0, 4, 0),
                new DrawingAnchorPoint(0, 0, 4, 0)),
        };
        sheet2.FormControls.Add(vmlOnlyControl);

        var deleteCommand = new DeleteRowsCommand(sheet2.Id, startRow: 5, count: 1);
        deleteCommand.Apply(ctx2).Success.Should().BeTrue();

        sheet2.FormControls.Should().BeEmpty(
            "a VML-only-anchored control whose sub-cell AnchorOffsets row was deleted must be removed, same as an Anchor-bearing control");
    }
}
