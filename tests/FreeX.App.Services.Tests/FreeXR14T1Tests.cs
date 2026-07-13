using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for round-14 fix bucket T1: linked-picture number-format loss on refresh,
/// a save-point (depth,version) false-clean aliasing after undo/substitute-edit/undo/redo, F4/Repeat
/// Last Action replaying a stale style factory after an unrelated plain edit, and Avalonia's
/// clipboard copy/cut truncating a selection taller than the on-screen viewport.
/// </summary>
public sealed class FreeXR14T1Tests
{
    /// <summary>
    /// R14-camera-linked-picture-2: RefreshLinkedPictureCells (WorkbookSession.cs) rebuilt a linked
    /// picture's cached cell text with a raw <c>number.Value.ToString(CultureInfo.CurrentCulture)</c>
    /// on every source-cell edit, discarding the cell's own number format. The initial paste snapshot
    /// (CreatePictureSnapshot) correctly used the format-aware DisplayText, so the picture's text
    /// permanently diverged from what it showed right after paste as soon as the source cell was
    /// edited. Excel's camera always keeps showing the formatted value.
    /// </summary>
    [Fact]
    public void RefreshLinkedPictureCells_AfterSourceCellEdit_KeepsSourceCellsNumberFormat()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d5 = new CellAddress(sheet.Id, 5, 4);
        sheet.SetCell(a1, new NumberValue(1234.5));
        // Widen the source column so the "$1,234.50" currency text fits; at the default width it now
        // correctly renders as the ### width indicator, which this test is not exercising.
        sheet.ColumnWidths[1] = 30;

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);
        session.SetSelectedRangeNumberFormat("$#,##0.00").Success.Should().BeTrue();

        session.SelectRange(new GridRange(a1, a1));
        var clipboardText = session.CopySelectedRangeText();
        session.SelectCell(d5);
        var pasteResult = session.PastePictureFromClipboardAtActiveCell(clipboardText, linkedPicture: true);
        pasteResult.Success.Should().BeTrue(pasteResult.ErrorMessage);

        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.IsLinkedToSourceRange.Should().BeTrue();
        picture.Cells.Should().Contain(cell => cell.Text == "$1,234.50",
            "the initial paste snapshot renders the source cell's formatted display text");

        // Edit the source cell -- Excel's camera keeps rendering the formatted value on every
        // refresh, not just at paste time.
        session.SelectCell(a1);
        session.CommitCellText("2000").Success.Should().BeTrue();

        var refreshedPicture = sheet.Pictures.Should().ContainSingle().Subject;
        refreshedPicture.Cells.Should().Contain(cell => cell.Text == "$2,000.00",
            "a refreshed linked picture must keep applying the source cell's currency number format, matching Excel's camera");
        refreshedPicture.Cells.Should().NotContain(cell => cell.Text == "2000",
            "the refreshed text must not be the raw unformatted number");
    }

    /// <summary>
    /// R14-undo-redo-depth-1: TryMarkCleanIfAtSavePoint treats a matching (depth,version) pair as
    /// proof the live undo stack equals the one recorded at save time. Undoing past the save point,
    /// making a DIFFERENT edit, undoing it, then redoing it returns both depth and version to their
    /// saved values (push/pop is exactly self-inverse) even though the redone entry is a different
    /// command than the one that was on the stack when the workbook was saved -- the in-memory
    /// content ({1,2,99}) then differs from the saved file ({1,2,3}) while incorrectly reporting
    /// clean.
    /// </summary>
    [Fact]
    public void RedoLastEdit_AfterSubstitutedEditAtSavedDepthAndVersion_StaysDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        session.CommitCellText("1").Success.Should().BeTrue();
        session.CommitCellText("2").Success.Should().BeTrue();
        session.CommitCellText("3").Success.Should().BeTrue();

        session.MarkSaved(@"C:\FreeXTests\Book.fxl");
        session.IsDirty.Should().BeFalse();

        // Undo past the save point, then make a DIFFERENT edit at the same depth (this clears the
        // redo entry for "3" permanently -- CommandBus.Push always clears the redo stack).
        session.UndoLastEdit().Success.Should().BeTrue();
        session.CommitCellText("99").Success.Should().BeTrue();

        // Now undo and redo the substituted edit -- depth and the old push/pop counter both return
        // to their saved values, but the stack content ({1,2,99}) is not the saved one ({1,2,3}).
        session.UndoLastEdit().Success.Should().BeTrue();
        var redo = session.RedoLastEdit();
        redo.Success.Should().BeTrue(redo.ErrorMessage);

        sheet.GetValue(a1).Should().Be(new NumberValue(99));
        session.IsDirty.Should().BeTrue(
            "the undo stack returned to the saved depth by a different path (a substituted edit), " +
            "so the in-memory content no longer matches the saved file and must still show as modified");
    }

    /// <summary>
    /// R14-undo-redo-depth-2: WorkbookSession wires exactly one command (style changes) through
    /// ExecuteRepeatableEditCommand so F4/Repeat Last Action can replay it; every other mutating
    /// method (including a plain cell-value edit) goes through the non-repeatable ExecuteEditCommand,
    /// which never updated or cleared the stored repeatable factory. A style change followed by an
    /// ordinary edit left the stale style factory in place indefinitely, so F4 on a later, unrelated
    /// selection silently reapplied the old style instead of doing nothing (Excel repeats the true
    /// LAST action).
    /// </summary>
    [Fact]
    public void RepeatLastAction_AfterPlainEditFollowingStyleChange_DoesNotReapplyStaleStyle()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        session.SelectCell(a1);
        session.SetSelectedRangeBold(true).Success.Should().BeTrue();
        session.CanRepeatLastAction.Should().BeTrue();

        // An ordinary, non-repeatable edit is now "the last thing the user did" and must invalidate
        // the stale Bold factory rather than leaving it in place.
        session.SelectCell(c1);
        session.CommitCellText("42").Success.Should().BeTrue();
        session.CanRepeatLastAction.Should().BeFalse(
            "a plain edit after a style change must invalidate F4/Repeat Last Action");

        session.SelectRange(new GridRange(b5, b5));
        var repeat = session.RepeatLastAction();

        repeat.Success.Should().BeFalse("nothing is currently repeatable");
        session.IsSelectedRangeStartBold.Should().BeFalse(
            "F4 must not silently reapply a stale, unrelated style change to a newly selected range");
    }

    /// <summary>
    /// R14-clipboard-formats-deep-1: TryCopySelectedRangeText/TryCutSelectedRangeText serialized
    /// straight off the on-screen Viewport (sized to the current scroll position), so any part of a
    /// selection taller than the visible area was blanked out in the clipboard text -- both for
    /// external copy/paste and CF_HTML. Excel always copies the full selection regardless of scroll.
    /// </summary>
    [Fact]
    public void CopySelectedRangeText_RangeTallerThanViewport_IncludesOffScreenCells()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        for (uint row = 1; row <= 200; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        // A small on-screen viewport (matching the shell's real scroll window) that cannot possibly
        // materialize 200 rows at once.
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a200 = new CellAddress(sheet.Id, 200, 1);
        session.SelectRange(new GridRange(a1, a200));

        var text = session.CopySelectedRangeText();

        var lines = text.Split("\r\n");
        lines.Should().HaveCount(200);
        lines[0].Should().Be("1");
        lines[199].Should().Be("200",
            "a row scrolled outside the on-screen viewport must not be blanked out of the clipboard payload");
    }

    private static WorkbookSession CreateSession(StartupWorkbookLoadResult source) =>
        new WorkbookSessionFactory().Create(source, viewportHeight: 240, viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
