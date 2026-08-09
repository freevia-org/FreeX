using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R126 (round 126 review wave): <see cref="WorkbookSession.CommitCellText"/>,
/// <see cref="WorkbookSession.ClearSelectedRangeContents"/> and
/// <see cref="WorkbookSession.ClearActiveCellContents"/> already cancel a pending Cut the instant they
/// commit (R66-services-clipboard-formats-6-2, <c>CancelPendingCutAfterMutatingEdit</c>) -- Excel
/// cancels a Cut's marching-ants/move semantics as soon as an ordinary edit changes a cell, so a
/// subsequent Paste never silently MOVES a cut source range using content the user has since changed.
/// <see cref="WorkbookSession.UndoLastEdit"/>/<see cref="WorkbookSession.RedoLastEdit"/> are exactly
/// this kind of mutating edit (they change what a cell actually contains) but never called
/// <c>CancelPendingCutAfterMutatingEdit</c> at all -- an Undo/Redo that changed a cell inside a
/// still-pending Cut's source range left the cut believing its source was untouched, so a later Paste
/// would MOVE (and blank out) a source range whose real content no longer matched what was cut.
/// <para>
/// Mirrors <see cref="R66_CutCancelledByMutatingEditTests"/> exactly, substituting an Undo/Redo for the
/// "unrelated mutating edit" -- these tests drive the REAL <c>WorkbookSession</c> entry points
/// (CommitCellText, CutSelectedRangeText, UndoLastEdit, RedoLastEdit, PasteClipboardTextAtActiveCell),
/// never constructing InternalClipboard by hand.
/// </para>
/// </summary>
public sealed class R126_UndoRedoCancelsPendingCutTests
{
    [Fact]
    public void UndoLastEdit_AfterCutOfACellItReverts_CancelsTheCut_SoALaterPasteDoesNotMoveTheSource()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(1));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));

        // A1: 1 -> 2, a real undoable edit.
        session.SelectCell(a1);
        var editResult = session.CommitCellText("2");
        editResult.Success.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(2));

        // Cut A1 -- captures the CURRENT value (2) and marks the internal clipboard as a pending Cut.
        session.SelectCell(a1);
        var clipboardText = session.CutSelectedRangeText();

        // Ctrl+Z reverts A1 back to 1 -- the pending cut's source no longer matches what was captured.
        var undoResult = session.UndoLastEdit();
        undoResult.Success.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(1), "sanity: undo must have reverted A1");

        session.SelectCell(c1);
        var paste = session.PasteClipboardTextAtActiveCell(clipboardText);

        paste.Success.Should().BeTrue();
        // The cut must have been cancelled by the undo: A1 keeps its (reverted) value instead of being
        // blanked out by a move, and the paste behaved as an ordinary (non-moving) paste of the
        // captured text.
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(1),
            "the undo must cancel the pending cut, so its source is never blanked by a later paste");
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(2));
    }

    [Fact]
    public void RedoLastEdit_ReapplyingAnEditInsideTheCutRange_CancelsTheCut_SoALaterPasteDoesNotMoveTheSource()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(1));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));

        session.SelectCell(a1);
        session.CommitCellText("2").Success.Should().BeTrue();
        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(1));

        // Cut AFTER the undo -- the snapshot legitimately captures the reverted value 1.
        session.SelectCell(a1);
        var clipboardText = session.CutSelectedRangeText();

        // Redo re-applies the 1 -> 2 edit -- the just-taken Cut snapshot (holding 1) is now stale.
        var redoResult = session.RedoLastEdit();
        redoResult.Success.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(2), "sanity: redo must have re-applied the edit");

        session.SelectCell(c1);
        var paste = session.PasteClipboardTextAtActiveCell(clipboardText);

        paste.Success.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(2),
            "the redo must cancel the pending cut, so its source is never blanked by a later paste");
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(1));
    }

    // No-regression sibling: an Undo with NOTHING to undo (the stack is empty) must leave an unrelated,
    // still-pending Cut completely untouched -- a fix that cancelled the cut unconditionally
    // (regardless of WorkbookCellEditResult.Success) would spuriously break the ordinary
    // cut-then-paste-elsewhere flow the instant the user pressed Ctrl+Z with nothing to undo.
    [Fact]
    public void UndoLastEdit_WithNothingToUndo_LeavesAPendingCutIntact_SoALaterPasteStillMovesTheSource()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(7));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));

        session.SelectCell(a1);
        var clipboardText = session.CutSelectedRangeText();

        // The undo stack is empty (no command has ever gone through the command bus in this test).
        var undoResult = session.UndoLastEdit();
        undoResult.Success.Should().BeFalse("sanity: there is nothing to undo");

        session.SelectCell(c1);
        var paste = session.PasteClipboardTextAtActiveCell(clipboardText);

        paste.Success.Should().BeTrue();
        sheet.GetCell(a1).Should().BeNull(
            "an Undo that had nothing to undo must not cancel an unrelated pending cut -- it must still MOVE the source");
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(7));
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
