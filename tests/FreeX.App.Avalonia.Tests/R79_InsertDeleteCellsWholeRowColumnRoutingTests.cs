using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for R79-commands-insert-delete-shift-5-2 (src/FreeX.App.Avalonia/
/// MainWindow.InsertDeleteCells.cs). The Avalonia Home ▸ Cells ▸ Insert Cells / Delete Cells ribbon
/// handlers passed <c>_session.SelectedRange</c> straight into <c>InsertCellsCommand</c>/
/// <c>DeleteCellsCommand</c> regardless of its shape -- unlike the WPF host's keyboard-shortcut path,
/// which first runs <c>KeyboardInsertDeletePlanner.PlanInsert/PlanDelete</c> and redirects a
/// whole-row/whole-column selection to <c>InsertRowsCommand</c>/<c>InsertColumnsCommand</c> (and their
/// Delete- counterparts) instead.
///
/// For a whole-column selection, <c>InsertCellsCommand</c>'s <c>CellShiftRegion.Rightward</c> builds a
/// shift band spanning the selection's full row span (1..MaxRow for a whole-column selection) x
/// [selection.Start.Col..MaxCol] -- i.e. nearly the entire worksheet. Any AutoFilter/structured table
/// anywhere in that huge band trips <c>AutoFilterOverlapsBand</c> and the whole operation is spuriously
/// rejected, even though a real whole-column insert only needs to redirect around the SAME column the
/// user selected. Even without a colliding table, the operation would only shift cells inside the band
/// and skip whole-column-only state (AutoFilter.Reference, watched cells, etc.) that only
/// InsertColumnsCommand/InsertRowsCommand shift via RowColumnShiftHelpers.CaptureAddressBearingState.
///
/// Fixed by having ShowInsertCellsDialogAsync/ShowDeleteCellsDialogAsync check
/// SelectionRangeService.IsWholeRowSelection/IsWholeColumnSelection first and route straight to
/// InsertRowsCommand/InsertColumnsCommand/DeleteRowsCommand/DeleteColumnsCommand -- skipping the
/// shift-direction dialog entirely, exactly as Excel does not prompt for a shift direction when
/// inserting/deleting whole rows or columns.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R79_InsertDeleteCellsWholeRowColumnRoutingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── Insert Cells: whole-column selection (the finding's exact repro) ──────────────────────

    [Fact]
    public async Task ShowInsertCellsDialogAsync_WholeColumnSelection_RoutesToInsertColumnsCommand_NotSpuriouslyRejectedByAutoFilterGuard()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("WholeColumnInsertFixture");
            window.Session.SelectSheet(sheet.Id);

            // A worksheet AutoFilter table living at columns H..H (to the right of the selected column
            // E), spanning only rows 1-10. Pre-fix, the whole-column selection's full row span
            // (1..MaxRow) made InsertCellsCommand's shift band cover virtually the whole sheet height
            // at columns [5..MaxCol], so this unrelated table -- nowhere near the user's actual
            // column-E selection in any practical sense -- would still spuriously trip the
            // table/AutoFilter overlap guard and reject the insert outright.
            sheet.AutoFilter = new WorksheetAutoFilterModel("H1:H10", null);

            // Select the ENTIRE column E (row span 1..MaxRow), exactly as clicking the column-E header
            // does.
            var wholeColumnE = new GridRange(
                new CellAddress(sheet.Id, 1, 5),
                new CellAddress(sheet.Id, CellAddress.MaxRow, 5));
            window.Session.SelectRange(wholeColumnE);

            await InvokePrivateAsync(window, "ShowInsertCellsDialogAsync");

            window.StatusTextForTest.Text.Should().Be("Inserted columns",
                "a whole-column selection must route to InsertColumnsCommand and succeed, not be " +
                "rejected by the band-scoped InsertCellsCommand's AutoFilter/table overlap guard");
            window.StatusTextForTest.Text.Should().NotContain("not allowed",
                "the fix must never surface the band-scoped 'table or AutoFilter range' rejection for a " +
                "whole-column selection");

            // Whole-column-only state (AutoFilter.Reference) must have shifted with the inserted
            // column, proving the real InsertColumnsCommand ran (RowColumnShiftHelpers.
            // CaptureAddressBearingState / ShiftAddressBearingColumnsUp) rather than a band-scoped shift
            // that has no way to move an axis-wide reference at all.
            sheet.AutoFilter.Should().NotBeNull();
            sheet.AutoFilter!.Reference.Should().Be("I1:I10",
                "inserting a column before column E must push the AutoFilter table at H (now right of " +
                "the insertion point) one column right, to I");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── Delete Cells: whole-column selection (sibling direction) ─────────────────────────────

    [Fact]
    public async Task ShowDeleteCellsDialogAsync_WholeColumnSelection_RoutesToDeleteColumnsCommand_NotSpuriouslyRejectedByAutoFilterGuard()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("WholeColumnDeleteFixture");
            window.Session.SelectSheet(sheet.Id);

            sheet.AutoFilter = new WorksheetAutoFilterModel("H1:H10", null);

            var wholeColumnE = new GridRange(
                new CellAddress(sheet.Id, 1, 5),
                new CellAddress(sheet.Id, CellAddress.MaxRow, 5));
            window.Session.SelectRange(wholeColumnE);

            await InvokePrivateAsync(window, "ShowDeleteCellsDialogAsync");

            window.StatusTextForTest.Text.Should().Be("Deleted columns",
                "a whole-column selection must route to DeleteColumnsCommand and succeed, not be " +
                "rejected by the band-scoped DeleteCellsCommand's AutoFilter/table overlap guard");

            sheet.AutoFilter.Should().NotBeNull();
            sheet.AutoFilter!.Reference.Should().Be("G1:G10",
                "deleting column E must pull the AutoFilter table at H (right of the deleted column) " +
                "one column left, to G");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── Insert/Delete Cells: whole-ROW selection (no-regression sibling for the other axis) ───

    [Fact]
    public async Task ShowInsertCellsDialogAsync_WholeRowSelection_RoutesToInsertRowsCommand_NoRegression()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("WholeRowInsertFixture");
            window.Session.SelectSheet(sheet.Id);

            // AutoFilter table on rows 8-10 (below the selected row 5), which a band-scoped shift
            // whose column span is the whole-row selection's full width (1..MaxCol) would have
            // spuriously overlapped exactly as in the whole-column case above.
            sheet.AutoFilter = new WorksheetAutoFilterModel("A8:C10", null);

            var wholeRow5 = new GridRange(
                new CellAddress(sheet.Id, 5, 1),
                new CellAddress(sheet.Id, 5, CellAddress.MaxCol));
            window.Session.SelectRange(wholeRow5);

            await InvokePrivateAsync(window, "ShowInsertCellsDialogAsync");

            window.StatusTextForTest.Text.Should().Be("Inserted rows",
                "a whole-row selection must route to InsertRowsCommand and succeed");
            sheet.AutoFilter.Should().NotBeNull();
            sheet.AutoFilter!.Reference.Should().Be("A9:C11",
                "inserting a row before row 5 must push the AutoFilter table at row 8 down by one, to row 9");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ShowDeleteCellsDialogAsync_WholeRowSelection_RoutesToDeleteRowsCommand_NoRegression()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("WholeRowDeleteFixture");
            window.Session.SelectSheet(sheet.Id);

            sheet.AutoFilter = new WorksheetAutoFilterModel("A8:C10", null);

            var wholeRow5 = new GridRange(
                new CellAddress(sheet.Id, 5, 1),
                new CellAddress(sheet.Id, 5, CellAddress.MaxCol));
            window.Session.SelectRange(wholeRow5);

            await InvokePrivateAsync(window, "ShowDeleteCellsDialogAsync");

            window.StatusTextForTest.Text.Should().Be("Deleted rows",
                "a whole-row selection must route to DeleteRowsCommand and succeed");
            sheet.AutoFilter.Should().NotBeNull();
            sheet.AutoFilter!.Reference.Should().Be("A7:C9",
                "deleting row 5 must pull the AutoFilter table at row 8 up by one, to row 7");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static async Task InvokePrivateAsync(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new System.MissingMethodException(nameof(MainWindow), methodName);
        var task = (Task)method.Invoke(window, null)!;
        await task;
    }
}
