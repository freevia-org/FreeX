using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Cleanup batch MED5 — round-10 MED findings P23, P25, and P83 (FreeX.Core.Commands).
/// </summary>
public sealed class FreeXCleanupMED5Tests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) Setup()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    private static CellAddress Addr(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));

    // ── P23: undo of a row insert must restore a linked picture's rendered snapshot,  ──────────
    // ── not just its LinkedSourceRange/Anchor. ─────────────────────────────────────────────────

    [Fact]
    public void InsertRow_InsideLinkedPictureRange_ThenUndo_RestoresCellsAndSourceRowCount()
    {
        var (_, sheet, ctx) = Setup();

        // 2x2 linked picture over A1:B2 with two populated cells.
        sheet.SetCell(Addr(sheet, 1, 1), new NumberValue(1));
        sheet.SetCell(Addr(sheet, 2, 1), new NumberValue(2));

        var picture = new PictureModel
        {
            Anchor = Addr(sheet, 1, 3),
            IsLinkedToSourceRange = true,
            LinkedSourceRange = Range(sheet, 1, 1, 2, 2),
            SourceRowCount = 2,
            SourceColumnCount = 2
        };
        picture.Cells.Add(new PictureCellSnapshot(0, 0, "1"));
        picture.Cells.Add(new PictureCellSnapshot(0, 1, ""));
        picture.Cells.Add(new PictureCellSnapshot(1, 0, "2"));
        picture.Cells.Add(new PictureCellSnapshot(1, 1, ""));
        sheet.Pictures.Add(picture);

        // Insert a row at row 2 — lands INSIDE the linked range (A1:B2 grows to A1:B3).
        var command = new InsertRowsCommand(sheet.Id, beforeRow: 2, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        var afterInsert = sheet.Pictures.Should().ContainSingle().Subject;
        afterInsert.LinkedSourceRange.Should().Be(Range(sheet, 1, 1, 3, 2));
        afterInsert.SourceRowCount.Should().Be(3, "RefreshLinkedPictureSnapshot grows the row count to match the widened range");
        afterInsert.Cells.Should().HaveCount(6, "the refreshed snapshot has one entry per cell in the new 3x2 range");

        // Undo: range/anchor restore is not enough — geometry and cell cache must revert too.
        command.Revert(ctx);

        var afterUndo = sheet.Pictures.Should().ContainSingle().Subject;
        afterUndo.LinkedSourceRange.Should().Be(Range(sheet, 1, 1, 2, 2), "undo restores the pre-insert range");
        afterUndo.SourceRowCount.Should().Be(2, "P23: undo must restore the pre-insert row count, not leave the post-insert 3");
        afterUndo.SourceColumnCount.Should().Be(2);
        afterUndo.Cells.Should().HaveCount(4, "P23: undo must restore the pre-insert 2x2 cell snapshot, not the post-insert 3x2 one");
    }

    // ── P25: a linked picture's refreshed cell snapshot must format DateTimeValue cells using ──
    // ── the cell's own display formatting, not the record's synthesized ToString() garbage. ────
    // ── (R14-camera-linked-picture-2 made this format-aware -- matching NumberFormatter.Format, ─
    // ── the same call ViewportService.GetDisplayText uses to render the live grid -- instead of ─
    // ── the raw OLE-automation numeric serial the picture used to show regardless of format.) ───

    [Fact]
    public void InsertRow_AboveLinkedPictureRange_FormatsDateCellAsNumberNotRecordToString()
    {
        var (_, sheet, ctx) = Setup();

        // B2 holds a date (6/18/2026), inside the picture's linked range A1:B2.
        var date = new DateTime(2026, 6, 18);
        sheet.SetCell(Addr(sheet, 2, 2), DateTimeValue.FromDateTime(date));

        var picture = new PictureModel
        {
            Anchor = Addr(sheet, 1, 3),
            IsLinkedToSourceRange = true,
            LinkedSourceRange = Range(sheet, 1, 1, 2, 2),
            SourceRowCount = 2,
            SourceColumnCount = 2
        };
        sheet.Pictures.Add(picture);

        // Insert a row ABOVE the range so RefreshLinkedPictureSnapshot re-snapshots every cell,
        // including the date cell, exercising FormatPictureCellText's DateTimeValue arm.
        var command = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        // The row insert at row 1 shifts the whole linked range down by one (A1:B2 -> A2:B3),
        // and the date cell (originally sheet row 2) physically moves down to sheet row 3 with
        // it — so within the refreshed range its offset is (row 3 - range start row 2, col 1) = (1,1).
        var refreshed = sheet.Pictures.Should().ContainSingle().Subject;
        refreshed.LinkedSourceRange.Should().Be(Range(sheet, 2, 1, 3, 2));
        var dateCell = refreshed.Cells.Should().ContainSingle(c => c.RowOffset == 1 && c.ColumnOffset == 1).Subject;

        dateCell.Text.Should().NotContain("DateTimeValue", "P25: a date cell must never render the record's synthesized ToString()");
        // Excel's camera always shows a cell exactly as the grid displays it. This cell has no
        // explicit number format (style default "General"), and FreeX's General format renders a
        // DateTimeValue as a short date string (NumberFormatter -> FormatGeneralDateTime), the same
        // as ViewportService.GetDisplayText would show for this cell in the live grid -- not the raw
        // OLE-automation numeric serial.
        dateCell.Text.Should().Be(
            NumberFormatter.Format(DateTimeValue.FromDateTime(date), "General", uses1904DateSystem: false),
            "a linked picture must render the cell's display text (matching the live grid and Excel's " +
            "camera), not a raw unformatted numeric serial");
        dateCell.IsNumericOrDate.Should().BeTrue();
    }

    // ── P83: a form control hosted on a DIFFERENT sheet whose LinkedCell explicitly ────────────
    // ── qualifies a reference to the edited sheet must have that reference shifted too. ────────

    [Fact]
    public void InsertRow_OnSheet1_ShiftsCrossSheetLinkedCellOnControlHostedOnSheet2()
    {
        var (workbook, sheet1, ctx) = Setup();
        var sheet2 = workbook.AddSheet("Sheet2");

        // Checkbox lives on Sheet2 but is linked to a cell on Sheet1.
        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            LinkedCell = "Sheet1!$A$5",
            Anchor = Range(sheet2, 1, 1, 1, 2)
        };
        sheet2.FormControls.Add(control);

        // Insert 2 rows above row 5 on Sheet1 — the logical cell moves from A5 to A7.
        var command = new InsertRowsCommand(sheet1.Id, beforeRow: 1, count: 2);
        command.Apply(ctx).Success.Should().BeTrue();

        control.LinkedCell.Should().Be(
            "Sheet1!$A$7",
            "P83: a cross-sheet LinkedCell explicitly qualified to the edited sheet must shift with it");

        // The control itself is untouched structurally (still on Sheet2, anchor unchanged).
        sheet2.FormControls.Should().ContainSingle();
        control.Anchor!.Value.Start.Row.Should().Be(1);

        command.Revert(ctx);

        control.LinkedCell.Should().Be("Sheet1!$A$5", "undo restores the original cross-sheet reference");
    }

    [Fact]
    public void InsertRow_OnSheet1_DoesNotTouchBareLinkedCellOnOtherSheetsOwnControl()
    {
        var (workbook, sheet1, ctx) = Setup();
        var sheet2 = workbook.AddSheet("Sheet2");

        // A control hosted on Sheet2 with a BARE (unqualified) LinkedCell means a cell on
        // Sheet2 itself — it must never be reinterpreted as a Sheet1 reference just because
        // Sheet1 is the sheet being structurally edited.
        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            LinkedCell = "$A$5",
            Anchor = Range(sheet2, 1, 1, 1, 2)
        };
        sheet2.FormControls.Add(control);

        var command = new InsertRowsCommand(sheet1.Id, beforeRow: 1, count: 2);
        command.Apply(ctx).Success.Should().BeTrue();

        control.LinkedCell.Should().Be("$A$5", "a bare ref on another sheet's control belongs to that sheet, not the edited one");

        command.Revert(ctx);
        control.LinkedCell.Should().Be("$A$5");
    }
}
