using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R127B (round 127 ScopeAudit follow-up to R127-services-clipboard-formats-copy-cancel-1):
/// <see cref="WorkbookSession.CancelPendingCutAfterMutatingEdit"/> was wired into
/// <see cref="WorkbookSession.CommitCellText"/>, <see cref="WorkbookSession.ClearSelectedRangeContents"/>,
/// <see cref="WorkbookSession.ClearActiveCellContents"/>, <see cref="WorkbookSession.UndoLastEdit"/> and
/// <see cref="WorkbookSession.RedoLastEdit"/>, but NOT into <see cref="WorkbookSession.ExecuteReviewCommand"/>
/// -- the generic executor the Avalonia shell's Insert/Delete Rows/Columns/Cells handlers
/// (MainWindow.InsertDeleteCells.cs, MainWindow.ContextMenuGridActions.cs, MainWindow.RibbonMenuWires.cs)
/// all route through. The WPF host cancels the clipboard unconditionally after every one of those
/// structural edits (MainWindow.CellsCommands.ClearClipboardMarqueeAfterStructuralEdit, called from 8
/// call sites), so a plain Copy or Cut followed by an Insert/Delete Row/Column/Cells left a stale
/// internal-clipboard snapshot alive on Avalonia where WPF retires it. Fixed by teaching
/// ExecuteReviewCommand to recognise the structural Insert/Delete Rows/Columns/Cells command family
/// (including a CompositeWorkbookCommand made entirely of them, for the Ribbon's multi-area Insert/
/// Delete Sheet Rows/Columns) and cancel the pending clipboard on success, the same choke point every
/// UI call site already shares -- rather than one call at each of the (more numerous than originally
/// audited) UI call sites.
/// <para>
/// These tests drive the REAL <see cref="WorkbookSession.ExecuteReviewCommand"/> entry point (the exact
/// path the Avalonia shell's Insert/Delete Rows/Columns/Cells handlers use), never constructing
/// InternalClipboard by hand, and use the presence/absence of the copied cell's Bold formatting on the
/// pasted cell as the observable signal for "did the paste reuse the internal clipboard snapshot (Bold
/// survives) or fall back to a plain external-text paste (Bold does not)".
/// </para>
/// </summary>
public sealed class R127B_StructuralEditCancelsClipboardTests
{
    [Fact]
    public void ExecuteReviewCommand_InsertRows_AfterCopyOfABoldCell_CancelsThePendingCopy()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var e1 = new CellAddress(sheet.Id, 1, 5);
        sheet.SetCell(a1, new NumberValue(5));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));

        session.SelectCell(a1);
        session.SetSelectedRangeBold(true).Success.Should().BeTrue();
        var clipboardText = session.CopySelectedRangeText();

        // Insert Rows is a structural edit reached exclusively through ExecuteReviewCommand on the
        // Avalonia shell (MainWindow.InsertDeleteCells.cs / ContextMenuGridActions.cs / RibbonMenuWires.cs).
        var insertResult = session.ExecuteReviewCommand(new InsertRowsCommand(sheet.Id, beforeRow: 10, count: 1));
        insertResult.Success.Should().BeTrue();

        session.SelectCell(e1);
        var paste = session.PasteClipboardTextAtActiveCell(clipboardText);

        paste.Success.Should().BeTrue();
        sheet.GetCell(e1)!.Value.Should().Be(new NumberValue(5));
        workbook.GetStyle(sheet.GetCell(e1)!.StyleId).Bold.Should().BeFalse(
            "Insert Rows must cancel the pending copy (matching the WPF host's " +
            "ClearClipboardMarqueeAfterStructuralEdit), so the paste falls back to plain external text " +
            "instead of reusing the stale internal-clipboard snapshot's bold formatting");
    }

    [Fact]
    public void ExecuteReviewCommand_DeleteColumns_AfterCutOfACell_CancelsThePendingCut()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var e1 = new CellAddress(sheet.Id, 1, 5);
        sheet.SetCell(a1, new NumberValue(42));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));

        session.SelectCell(a1);
        var clipboardText = session.CutSelectedRangeText();

        // Delete Columns is a structural edit reached exclusively through ExecuteReviewCommand.
        var deleteResult = session.ExecuteReviewCommand(new DeleteColumnsCommand(sheet.Id, startCol: 10, count: 1));
        deleteResult.Success.Should().BeTrue();

        // A1's own value survives (the delete targeted an unrelated column), but the pending Cut
        // snapshot must no longer be honored by a later Paste -- Excel does not silently move the
        // source range once a structural edit has landed elsewhere on the sheet.
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(42), "sanity: the delete targeted a different column");

        session.SelectCell(e1);
        var paste = session.PasteClipboardTextAtActiveCell(clipboardText);

        paste.Success.Should().BeTrue();
        sheet.GetCell(e1)!.Value.Should().Be(new NumberValue(42), "the plain external clipboard text still pastes");
        sheet.GetCell(a1).Should().NotBeNull(
            "Delete Columns must cancel the pending cut, so the paste must NOT also blank out the original " +
            "source cell the way completing a real Cut-Paste move would (a still-honored cut clears the " +
            "source cell entirely, not merely its value)");
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(42),
            "Delete Columns must cancel the pending cut, so the paste must NOT also blank out the original " +
            "source cell the way completing a real Cut-Paste move would");
    }

    // Covers the Ribbon's multi-area Insert Sheet Rows (MainWindow.RibbonMenuWires.InsertSheetRows), which
    // wraps one InsertRowsCommand per disjoint selected area in a CompositeWorkbookCommand instead of
    // passing a single structural command directly -- the composite-unwrapping branch of
    // IsStructuralCellShiftCommand.
    [Fact]
    public void ExecuteReviewCommand_MultiAreaCompositeOfInsertRows_AfterCopyOfABoldCell_CancelsThePendingCopy()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var e1 = new CellAddress(sheet.Id, 1, 5);
        sheet.SetCell(a1, new NumberValue(5));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));

        session.SelectCell(a1);
        session.SetSelectedRangeBold(true).Success.Should().BeTrue();
        var clipboardText = session.CopySelectedRangeText();

        IWorkbookCommand composite = new CompositeWorkbookCommand("Insert Sheet Rows",
        [
            new InsertRowsCommand(sheet.Id, beforeRow: 20, count: 1),
            new InsertRowsCommand(sheet.Id, beforeRow: 10, count: 1),
        ]);
        var insertResult = session.ExecuteReviewCommand(composite);
        insertResult.Success.Should().BeTrue();

        session.SelectCell(e1);
        var paste = session.PasteClipboardTextAtActiveCell(clipboardText);

        paste.Success.Should().BeTrue();
        workbook.GetStyle(sheet.GetCell(e1)!.StyleId).Bold.Should().BeFalse(
            "a multi-area Insert Sheet Rows composite (as the Ribbon's InsertSheetRows builds for a " +
            "Ctrl+click multi-area row selection) must cancel the pending copy exactly like a single-area " +
            "InsertRowsCommand does");
    }

    // No-regression sibling: an ExecuteReviewCommand call for a NON-structural command (e.g. changing the
    // workbook calculation mode, which many unrelated Avalonia handlers also route through
    // ExecuteReviewCommand for) must NOT cancel a pending Copy. A fix that cancelled the clipboard for
    // every ExecuteReviewCommand call -- rather than only the Insert/Delete Rows/Columns/Cells family --
    // would spuriously break Copy/Paste around any of the ~150 other command kinds this same executor runs
    // (formatting, comments, charts, pivot, protection, ...), none of which Excel cancels the clipboard for.
    [Fact]
    public void ExecuteReviewCommand_NonStructuralCommand_AfterCopyOfABoldCell_DoesNotCancelThePendingCopy()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var e1 = new CellAddress(sheet.Id, 1, 5);
        sheet.SetCell(a1, new NumberValue(5));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));

        session.SelectCell(a1);
        session.SetSelectedRangeBold(true).Success.Should().BeTrue();
        var clipboardText = session.CopySelectedRangeText();

        var modeResult = session.ExecuteReviewCommand(
            new SetCalculationModeCommand(WorkbookCalculationMode.Manual));
        modeResult.Success.Should().BeTrue();

        session.SelectCell(e1);
        var paste = session.PasteClipboardTextAtActiveCell(clipboardText);

        paste.Success.Should().BeTrue();
        sheet.GetCell(e1)!.Value.Should().Be(new NumberValue(5));
        workbook.GetStyle(sheet.GetCell(e1)!.StyleId).Bold.Should().BeTrue(
            "a non-structural command (e.g. toggling calculation mode) must NOT cancel the pending copy -- " +
            "only Insert/Delete Rows/Columns/Cells does, matching the WPF host's scoping of " +
            "ClearClipboardMarqueeAfterStructuralEdit to those specific handlers rather than its generic " +
            "TryExecuteCommand executor");
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
