using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R127 (round 127 review wave): <see cref="WorkbookSession"/>'s shared
/// <c>CancelPendingCutAfterMutatingEdit</c> choke point (called from
/// <see cref="WorkbookSession.CommitCellText"/>, <see cref="WorkbookSession.ClearSelectedRangeContents"/>,
/// <see cref="WorkbookSession.ClearActiveCellContents"/>, <see cref="WorkbookSession.UndoLastEdit"/> and
/// <see cref="WorkbookSession.RedoLastEdit"/>) used to only clear <c>_internalClipboard</c> when it held
/// a pending CUT (<c>IsCut: true</c>), even though its own doc comment claimed to mirror the WPF host's
/// <c>MainWindow.CommandExecution.TryExecuteEditCells</c> -- which actually clears
/// <c>_internalClipboard</c> unconditionally, with no <c>IsCut</c> check
/// (R127-services-clipboard-formats-copy-cancel-1). That left a plain Copy's snapshot alive across an
/// unrelated edit or Undo/Redo on the Avalonia shell (and FreeW/FreeP, which share this tier), so a
/// later Paste could still reuse a stale internal-clipboard snapshot -- including its captured
/// formatting -- instead of falling back to the live OS-clipboard text the way Excel and the WPF host
/// both do once the marquee is cancelled.
/// <para>
/// These tests drive the REAL <c>WorkbookSession</c> entry points (SetSelectedRangeBold,
/// CopySelectedRangeText, CommitCellText, UndoLastEdit, RedoLastEdit, PasteClipboardTextAtActiveCell),
/// never constructing InternalClipboard by hand, and use the presence/absence of the copied cell's Bold
/// formatting on the pasted cell as the observable signal for "did the paste reuse the internal
/// clipboard snapshot (Bold survives) or fall back to a plain external-text paste (Bold does not)".
/// </para>
/// </summary>
public sealed class R127_CopyClipboardCancelledByMutatingEditTests
{
    [Fact]
    public void CommitCellText_AfterCopyOfABoldCell_CancelsThePendingCopy_SoALaterPasteDoesNotReuseItsFormatting()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(5));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));

        // A1 is bold, then Copied -- captures the value AND the bold style into the internal clipboard
        // as a pending (non-cut) Copy.
        session.SelectCell(a1);
        session.SetSelectedRangeBold(true).Success.Should().BeTrue();
        var clipboardText = session.CopySelectedRangeText();

        // An ordinary, unrelated mutating edit elsewhere on the sheet -- Excel cancels the pending
        // Copy's marching ants on exactly this trigger.
        session.SelectCell(b1);
        session.CommitCellText("99").Success.Should().BeTrue();

        session.SelectCell(c1);
        var paste = session.PasteClipboardTextAtActiveCell(clipboardText);

        paste.Success.Should().BeTrue();
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(5));
        workbook.GetStyle(sheet.GetCell(c1)!.StyleId).Bold.Should().BeFalse(
            "the unrelated edit must cancel the pending copy, so the paste falls back to plain external " +
            "text instead of reusing the stale internal-clipboard snapshot's bold formatting");
    }

    [Fact]
    public void UndoLastEdit_AfterCopyOfABoldCell_CancelsThePendingCopy_SoALaterPasteDoesNotReuseItsFormatting()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(1));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));

        // A1 made bold first (its own undoable edit), THEN 1 -> 2 (a second, real undoable edit) --
        // bold must be set before the value edit so Undo below reverts the value change and leaves
        // bold untouched, then Copied while both are in effect.
        session.SelectCell(a1);
        session.SetSelectedRangeBold(true).Success.Should().BeTrue();
        session.CommitCellText("2").Success.Should().BeTrue();
        var clipboardText = session.CopySelectedRangeText();

        // Ctrl+Z reverts the 1 -> 2 edit (the most recent command). This is exactly the kind of
        // mutating edit that must cancel a pending Copy (it changed a cell's committed content), even
        // though it left the earlier bold style command alone.
        var undoResult = session.UndoLastEdit();
        undoResult.Success.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(1), "sanity: undo must have reverted A1");

        session.SelectCell(c1);
        var paste = session.PasteClipboardTextAtActiveCell(clipboardText);

        paste.Success.Should().BeTrue();
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(2));
        workbook.GetStyle(sheet.GetCell(c1)!.StyleId).Bold.Should().BeFalse(
            "the undo must cancel the pending copy, so the paste falls back to plain external text " +
            "instead of reusing the stale internal-clipboard snapshot's bold formatting");
    }

    [Fact]
    public void RedoLastEdit_AfterCopyOfABoldCell_CancelsThePendingCopy_SoALaterPasteDoesNotReuseItsFormatting()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(1));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));

        // A1 made bold first (its own undoable edit) so it survives the Undo/Redo dance below without
        // itself ever landing on the redo stack (a NEW command committed after an Undo, such as
        // SetSelectedRangeBold, would otherwise clear the pending redo of the 1 -> 2 edit).
        session.SelectCell(a1);
        session.SetSelectedRangeBold(true).Success.Should().BeTrue();
        session.CommitCellText("2").Success.Should().BeTrue();
        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(1));

        // Copy AFTER the undo -- the snapshot legitimately captures the reverted (bold) value 1. Copy
        // itself never touches the command bus, so it does not disturb the pending redo.
        session.SelectCell(a1);
        var clipboardText = session.CopySelectedRangeText();

        // Redo re-applies the 1 -> 2 edit -- the just-taken Copy snapshot (holding 1) is now stale.
        var redoResult = session.RedoLastEdit();
        redoResult.Success.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(2), "sanity: redo must have re-applied the edit");

        session.SelectCell(c1);
        var paste = session.PasteClipboardTextAtActiveCell(clipboardText);

        paste.Success.Should().BeTrue();
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(1));
        workbook.GetStyle(sheet.GetCell(c1)!.StyleId).Bold.Should().BeFalse(
            "the redo must cancel the pending copy, so the paste falls back to plain external text " +
            "instead of reusing the stale internal-clipboard snapshot's bold formatting");
    }

    // No-regression sibling: an ordinary Copy immediately followed by a Paste (nothing mutating in
    // between) must still reuse the internal clipboard snapshot -- including its formatting -- exactly
    // as before. A fix that cancelled the pending clipboard too aggressively (e.g. on every paste, or
    // on selection changes) would spuriously break the ordinary copy-then-paste-elsewhere flow.
    [Fact]
    public void CopyThenPasteWithNoInterveningEdit_StillReusesTheInternalClipboardSnapshotAndItsFormatting()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(7));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));

        session.SelectCell(a1);
        session.SetSelectedRangeBold(true).Success.Should().BeTrue();
        var clipboardText = session.CopySelectedRangeText();

        session.SelectCell(c1);
        var paste = session.PasteClipboardTextAtActiveCell(clipboardText);

        paste.Success.Should().BeTrue();
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(7));
        workbook.GetStyle(sheet.GetCell(c1)!.StyleId).Bold.Should().BeTrue(
            "an ordinary copy-then-paste with no intervening edit must still reuse the internal " +
            "clipboard snapshot's formatting");
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
