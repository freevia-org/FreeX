using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for the R147 panes-and-scroll F1/F2 findings
/// (src/FreeX.App.Services/WorkbookSession.cs:6899 / :516):
/// <c>RowColumnShiftHelpers.ShiftFreezeAndSplitPanes</c> correctly re-anchors the SHARED
/// <see cref="Sheet.FrozenRows"/>/<see cref="Sheet.FrozenCols"/>/<see cref="Sheet.SplitRow"/>/
/// <see cref="Sheet.SplitColumn"/> fields whenever a window inserts or deletes whole rows/columns,
/// but the per-view Freeze/Split override caches (<c>_viewFrozenRowsOverrides</c>/etc.) that a
/// <see cref="WorkbookSession.CreateSiblingView"/> sibling reads from were only ever invalidated on
/// the ACTING session -- a sibling window's cache kept the pre-shift boundary (F1), and saving from
/// that stale sibling (<see cref="WorkbookSession.ReconcileViewStateForSave"/>) would then persist
/// that stale boundary back onto the shared, already-corrected fields, corrupting the saved file
/// (F2). The fix threads a shared per-sheet structure-revision counter (bumped only by whole-row/
/// whole-column insert/delete) through every Freeze/Split accessor and through
/// <see cref="WorkbookSession.ReconcileViewStateForSave"/>, so a sibling whose snapshot predates the
/// shift drops it and falls back to the shared (already-corrected) field.
/// </summary>
public sealed class R147_PanesAndScrollSiblingStructuralShiftTests
{
    /// <summary>
    /// F1: fails before the fix -- a sibling window's <c>GetEffectiveFrozenRows()</c> kept returning
    /// the pre-insert frozen-row count after another window inserted rows above the frozen band,
    /// instead of following the shift like the shared <see cref="Sheet.FrozenRows"/> field (and the
    /// acting window) already do.
    /// </summary>
    [Fact]
    public void SiblingWindow_FrozenRowsAndCols_FollowStructuralRowAndColumnInsertShift()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var windowA = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        var c3 = new CellAddress(sheet.Id, 3, 3);
        windowA.SelectCell(c3);
        windowA.FreezePanesAtActiveCell().Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(2u);
        sheet.FrozenCols.Should().Be(2u);

        // Window B opens AFTER the freeze, inheriting it exactly like Excel's own New Window --
        // matching the finding's user gesture (both windows already on the same sheet before the
        // structural edit).
        var windowB = windowA.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);
        windowB.GetEffectiveFrozenRows().Should().Be(2u);
        windowB.GetEffectiveFrozenCols().Should().Be(2u);

        // Window A inserts 2 rows above the frozen band -- an ordinary structural edit.
        windowA.InsertRows(beforeRow: 1, count: 2).Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(4u,
            "RowColumnShiftHelpers.ShiftFreezeAndSplitPanes must grow the shared frozen band");

        // *** The F1 fix under test ***: window B, untouched since it was opened, must see the
        // shifted boundary too -- not the stale pre-insert value.
        windowB.GetEffectiveFrozenRows().Should().Be(4u,
            "a sibling window's Freeze Panes boundary must follow a row insert made in another window, matching the now-corrected shared Sheet.FrozenRows");

        // Same story on the column axis.
        windowA.InsertColumns(beforeColumn: 1, count: 3).Success.Should().BeTrue();
        sheet.FrozenCols.Should().Be(5u);
        windowB.GetEffectiveFrozenCols().Should().Be(5u,
            "a sibling window's Freeze Panes column boundary must follow a column insert made in another window");
    }

    /// <summary>
    /// F1, Split-pane sibling: same defect, exercised through Window ▸ Split instead of Freeze Panes.
    /// </summary>
    [Fact]
    public void SiblingWindow_SplitRowAndCol_FollowStructuralDeleteShift()
    {
        var workbook = CreateWorkbook(rows: 20, cols: 20);
        var sheet = workbook.Sheets.Single();
        var windowA = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        windowA.SelectCell(new CellAddress(sheet.Id, 10, 10));
        windowA.ToggleSplitPanesAtActiveCell().Success.Should().BeTrue();
        var splitRowBefore = sheet.SplitRow;
        var splitColBefore = sheet.SplitColumn;
        splitRowBefore.Should().NotBeNull();
        splitColBefore.Should().NotBeNull();

        var windowB = windowA.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);
        windowB.GetEffectiveSplitRow().Should().Be(splitRowBefore);
        windowB.GetEffectiveSplitCol().Should().Be(splitColBefore);

        // Delete 2 rows above the split boundary in window A.
        windowA.DeleteRows(startRow: 1, count: 2).Success.Should().BeTrue();
        sheet.SplitRow.Should().Be(splitRowBefore!.Value - 2);

        windowB.GetEffectiveSplitRow().Should().Be(splitRowBefore.Value - 2,
            "a sibling window's Split row boundary must follow a row delete made in another window");
        windowB.GetEffectiveSplitCol().Should().Be(splitColBefore,
            "deleting rows must not disturb the column split boundary");
    }

    /// <summary>
    /// F2: fails before the fix -- saving from a sibling window whose Freeze cache predates a
    /// structural shift in another window overwrote the shared, already-corrected
    /// <see cref="Sheet.FrozenRows"/> with the sibling's stale pre-shift value.
    /// </summary>
    [Fact]
    public void ReconcileViewStateForSave_FromStaleSiblingWindow_DoesNotRegressAlreadyShiftedSharedFrozenRows()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var windowA = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        windowA.SelectCell(new CellAddress(sheet.Id, 3, 1));
        windowA.FreezePanesAtActiveCell().Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(2u);

        var windowB = windowA.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        windowA.InsertRows(beforeRow: 1, count: 2).Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(4u);

        // Window B never reads or touches its Freeze/Split/Zoom -- exactly the finding's user
        // gesture -- before saving.
        windowB.ReconcileViewStateForSave();

        sheet.FrozenRows.Should().Be(4u,
            "saving from a sibling window whose cache predates a structural shift must not regress the shared, already-corrected FrozenRows back to the pre-insert value");
    }

    /// <summary>
    /// No-regression sibling (F1/F2-adjacent): a genuinely UNRELATED preserved-selection command in
    /// window A (Row Height, which shares the same <c>ExecuteRepeatableCommandPreservingSelection</c>
    /// choke point as Insert/Delete Rows/Columns but never touches row/column counts or
    /// Freeze/Split) must NOT bump the shared structure revision, so window B's own independently
    /// diverged Freeze Panes setting survives untouched -- proving the fix is scoped to genuine
    /// row/column insert/delete and does not resurrect the R86/R87 cross-window leak for every
    /// preserved-selection command.
    /// </summary>
    [Fact]
    public void SiblingWindow_IndependentFreezePanes_UnaffectedByUnrelatedRowHeightCommandInOtherWindow()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var windowA = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        var windowB = windowA.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        // Window B independently freezes its own top row -- diverging from window A (still
        // unfrozen), exactly the per-view independence R86/R87 exist to protect.
        windowB.SelectCell(new CellAddress(sheet.Id, 2, 1));
        windowB.FreezePanesAtActiveCell().Success.Should().BeTrue();
        windowB.GetEffectiveFrozenRows().Should().Be(1u);
        windowA.GetEffectiveFrozenRows().Should().Be(0u);

        // Window A runs an unrelated preserved-selection structural command that never shifts
        // row/column counts.
        windowA.SelectRange(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)));
        windowA.SetSelectedRowsHeight(30).Success.Should().BeTrue();

        windowB.GetEffectiveFrozenRows().Should().Be(1u,
            "an unrelated Row Height command in another window must not reset a sibling's own independently-set Freeze Panes");
    }

    private static WorkbookSession CreateSession(StartupWorkbookLoadResult source) =>
        new WorkbookSessionFactory().Create(source, viewportHeight: 240, viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book", int rows = 6, int cols = 6)
    {
        var workbook = new Workbook(name);
        var sheet = workbook.AddSheet("Sheet1");
        for (var row = 1; row <= rows; row++)
        {
            for (var col = 1; col <= cols; col++)
            {
                sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), new NumberValue(row * 100 + col));
            }
        }
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
