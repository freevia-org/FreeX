using System.IO;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for the R138 finding (src/FreeX.App.Services/WorkbookSession.cs:476):
/// unlike the sibling per-window fields <see cref="WorkbookSession.ReconcileViewStateForSave"/>
/// already reconciles (zoom, view mode, gridlines, headings, show-formulas, freeze panes, split),
/// <see cref="WorkbookSession.ActiveCell"/> was never reconciled onto the shared <see cref="Sheet"/>
/// before save. <see cref="WorkbookSession.SelectCell"/> and its siblings write the active cell
/// straight onto <see cref="Sheet.ActiveRow"/>/<see cref="Sheet.ActiveCol"/> the instant the
/// selection changes -- and every "New Window" sibling (<see cref="WorkbookSession.CreateSiblingView"/>)
/// shares the very same <see cref="Sheet"/> object, so whichever sibling's selection changed MOST
/// RECENTLY owned the persisted active cell, regardless of which sibling actually performed the
/// save. Worse, a freshly opened sibling window (<c>InitializeSiblingView</c>) reset its own
/// displayed active cell to A1 purely locally, without ever touching the shared fields, so saving
/// from a brand-new sibling before it made its own selection persisted whatever an older sibling
/// had left there instead of the new sibling's own displayed A1.
///
/// These tests exercise the REAL production session object (<see cref="WorkbookSession"/>,
/// constructed via <see cref="WorkbookSessionFactory"/>) and the real user-gesture entry points
/// (<see cref="WorkbookSession.SelectCell"/>, <see cref="WorkbookSession.SelectSheet"/> -- the same
/// methods the Avalonia shell's cell-click and sheet-tab handlers call), per the existing
/// R120 sibling-view test convention in this file's neighbors.
/// </summary>
public sealed class R138_ActiveCellReconcileForSaveTests
{
    /// <summary>
    /// Two "New Window" siblings on the SAME sheet: the sibling's later selection change must not
    /// steal what the saving window persists.
    /// </summary>
    [Fact]
    public void ReconcileViewStateForSave_TwoWindowsSameSheet_PersistsSavingWindowsOwnActiveCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.GetSheetAt(0);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var sibling = session.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        // "session" (the view about to save) moves its own active cell to B5.
        var ownCell = new CellAddress(sheet.Id, 5, 2);
        session.SelectCell(ownCell);
        session.ActiveCell.Should().Be(ownCell);
        sheet.ActiveRow.Should().Be(5u);
        sheet.ActiveCol.Should().Be(2u);

        // The sibling then independently moves ITS OWN active cell on the SAME shared Sheet --
        // mutating the shared fields "session" is about to serialize, exactly like a real second
        // window clicking a different cell after "session" last moved.
        var siblingCell = new CellAddress(sheet.Id, 26, 10);
        sibling.SelectCell(siblingCell);
        sibling.ActiveCell.Should().Be(siblingCell);

        sheet.ActiveRow.Should().Be(26u, "the sibling's own selection last mutated the shared field");
        sheet.ActiveCol.Should().Be(10u);

        // "session" still displays its own active cell, unaffected by the sibling's later move.
        session.ActiveCell.Should().Be(ownCell);

        // *** The R138 fix under test ***: reconciling before save must push "session"'s own
        // active cell back onto the shared Sheet fields, not leave the sibling's later overwrite.
        session.ReconcileViewStateForSave();

        sheet.ActiveRow.Should().Be(5u,
            "Ctrl+S from the saving window must persist what THAT window's active cell is, not " +
            "whichever sibling window last touched the shared fields");
        sheet.ActiveCol.Should().Be(2u);

        // The sibling's own display must be completely unaffected by "session" reconciling for its
        // own save -- reconciliation is a one-way push from the saving view, never a broadcast.
        sibling.ActiveCell.Should().Be(siblingCell);
    }

    /// <summary>
    /// Two "New Window" siblings on DIFFERENT sheets of the same workbook: reconciling from the
    /// saving window must persist that window's own active cell for the sheet it is showing, and
    /// must not disturb the other sheet the sibling is showing (which this window never visited).
    /// </summary>
    [Fact]
    public void ReconcileViewStateForSave_TwoWindowsDifferentSheets_EachPersistsItsOwnSheetsActiveCell()
    {
        var workbook = CreateWorkbook();
        workbook.AddSheet("Sheet2");
        var sheet1 = workbook.GetSheetAt(0);
        var sheet2 = workbook.GetSheetAt(1);

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var sibling = session.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        // "session" stays on Sheet1 and moves its own active cell there.
        var sheet1Cell = new CellAddress(sheet1.Id, 3, 3);
        session.SelectCell(sheet1Cell);

        // "sibling" switches to Sheet2 (a real sheet-tab gesture) and moves ITS OWN active cell
        // there. This also happens to leave Sheet2's shared ActiveRow/ActiveCol at the sibling's
        // cell, since nothing else has ever touched Sheet2.
        sibling.SelectSheet(sheet2.Id).Should().BeTrue();
        var sheet2Cell = new CellAddress(sheet2.Id, 8, 4);
        sibling.SelectCell(sheet2Cell);

        sheet1.ActiveRow.Should().Be(3u);
        sheet1.ActiveCol.Should().Be(3u);
        sheet2.ActiveRow.Should().Be(8u);
        sheet2.ActiveCol.Should().Be(4u);

        // "session" saves from Sheet1: its own sheet must reflect its own active cell, and Sheet2
        // (which "session" never visited) must be left exactly as the sibling set it.
        session.ReconcileViewStateForSave();

        sheet1.ActiveRow.Should().Be(3u, "session's own active cell on the sheet it is showing");
        sheet1.ActiveCol.Should().Be(3u);
        sheet2.ActiveRow.Should().Be(8u, "session never visited Sheet2, so reconciling must not touch it");
        sheet2.ActiveCol.Should().Be(4u);
    }

    /// <summary>
    /// No-regression sibling: a session with no sibling in play (the ordinary single-window case)
    /// must still reconcile its own active cell as a no-op onto its own already-current shared
    /// fields, and must never touch cell data.
    /// </summary>
    [Fact]
    public void ReconcileViewStateForSave_SingleSession_PersistsOwnActiveCellAndLeavesCellDataUntouched()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.GetSheetAt(0);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var cell = new CellAddress(sheet.Id, 9, 4);
        session.SelectCell(cell);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));

        session.ReconcileViewStateForSave();

        sheet.ActiveRow.Should().Be(9u, "reconciling a session with no diverged sibling must keep its own active cell");
        sheet.ActiveCol.Should().Be(4u);
        sheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(42),
            "reconciliation must never touch cell data");
    }

    /// <summary>
    /// End-to-end reload: after two siblings diverge and "session" reconciles + saves through the
    /// real <see cref="XlsxFileAdapter"/>, reopening the file must land on the SAVING window's own
    /// active cell, not the sibling's later overwrite of the shared fields.
    /// </summary>
    [Fact]
    public void ReconcileViewStateForSave_ThenXlsxRoundTrip_ReopensAtSavingWindowsActiveCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.GetSheetAt(0);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var sibling = session.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        var ownCell = new CellAddress(sheet.Id, 12, 6);
        session.SelectCell(ownCell);

        var siblingCell = new CellAddress(sheet.Id, 40, 15);
        sibling.SelectCell(siblingCell);

        session.ReconcileViewStateForSave();

        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var reopened = new MemoryStream(saved.ToArray(), writable: false);
        var reopenedWorkbook = adapter.Load(reopened);
        var reopenedSheet = reopenedWorkbook.GetSheetAt(0);

        reopenedSheet.ActiveRow.Should().Be(12u,
            "reopening the saved file must land on the saving window's own active cell");
        reopenedSheet.ActiveCol.Should().Be(6u);
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
