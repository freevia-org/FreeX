using System.Reflection;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R89-freeze-split-per-window-1: the WPF host's Freeze Panes and Window
/// &gt; Split were the last round-87 PARTIAL -- unlike ViewMode/Zoom (R83) and Gridlines/Headings/
/// Rulers (R87), they still read/wrote the shared <see cref="Sheet.FrozenRows"/>/
/// <see cref="Sheet.FrozenCols"/>/<see cref="Sheet.SplitRow"/>/<see cref="Sheet.SplitColumn"/>
/// fields directly (MainWindow.Viewport.cs/MainWindow.ViewCommands.cs), so toggling either in one
/// Excel "View &gt; New Window" sibling instantly leaked into every other window viewing the same
/// document.
///
/// The fix extends the existing <c>WorksheetViewStateStore</c>/<c>GetEffectiveViewState</c>/
/// <c>SyncWindowViewState</c> per-window mechanism (src/FreeX.Core.Commands/
/// WorksheetViewStateStore.cs, src/FreeX.App.Host/MainWindow.Viewport.cs) to also cover
/// FrozenRows/FrozenCols/SplitRow/SplitColumn, and routes the WPF host's viewport rendering
/// through the <c>FrozenRowsOverride</c>/<c>FrozenColsOverride</c>/<c>SplitOverride</c> fields the
/// shared <c>ViewportRequest</c>/<c>ViewportService</c> already accept (added for the Avalonia
/// shell's own per-view overrides in <c>WorkbookSession</c>).
///
/// These tests simulate two "New Window" siblings viewing the very same <see cref="Workbook"/>/
/// <see cref="Sheet"/> object graph, the same way R87_PerWindowViewOptionsTests does: construct two
/// independent <see cref="MainWindow"/> instances via <see cref="R49MainWindowTestHarness"/> and
/// replace the second window's authoritative <c>WorkbookSession</c> with one over the first
/// window's actual (post-Loaded) workbook/sheet.
/// </summary>
public sealed class R89_FreezeSplitPerWindowTests
{
    [Fact]
    public void SetFreezePanes_ChangedInOneWindow_DoesNotLeakIntoSiblingWindow()
    {
        StaTestRunner.Run(() =>
        {
            var (window1, workbook) = R49MainWindowTestHarness.CreateWindow();
            var (window2, _) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                AdoptSameDocument(window2, workbook, GetCurrentSheetId(window1));

                // Window 2 renders first, seeding its own per-window store from the (still
                // unfrozen) shared FrozenRows/FrozenCols.
                R49MainWindowTestHarness.Invoke(window2, "UpdateViewport");
                window2.SheetGrid.Viewport?.FrozenPanes.Should().BeNull("no window has frozen panes yet");

                // Window 1 freezes the top 2 rows and first column.
                InvokeSetFreezePanes(window1, frozenRows: 2, frozenCols: 1);

                window1.SheetGrid.Viewport?.FrozenPanes.Should().NotBeNull(
                    "the window that froze panes must render the frozen band");
                window1.SheetGrid.Viewport!.FrozenPanes!.Rows.Should().Be(2u);
                window1.SheetGrid.Viewport!.FrozenPanes!.Cols.Should().Be(1u);

                // Re-render window 2 (e.g. a later, unrelated redraw) -- before the fix this would
                // read the shared Sheet.FrozenRows/FrozenCols window 1 just changed.
                R49MainWindowTestHarness.Invoke(window2, "UpdateViewport");

                window2.SheetGrid.Viewport?.FrozenPanes.Should().BeNull(
                    "a sibling New Window that never froze panes itself must keep showing none, " +
                    "exactly like Excel keeps Freeze Panes per-window");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window1);
                R49MainWindowTestHarness.Close(window2);
            }
        });
    }

    [Fact]
    public void OnSplitDividerMoved_ChangedInOneWindow_DoesNotLeakIntoSiblingWindow()
    {
        StaTestRunner.Run(() =>
        {
            var (window1, workbook) = R49MainWindowTestHarness.CreateWindow();
            var (window2, _) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                AdoptSameDocument(window2, workbook, GetCurrentSheetId(window1));

                R49MainWindowTestHarness.Invoke(window2, "UpdateViewport");
                window2.SheetGrid.SplitRow.Should().BeNull("no window has split panes yet");
                window2.SheetGrid.SplitColumn.Should().BeNull();

                // Window 1 splits at row 10 / column 3.
                InvokeSplitDividerMoved(window1, splitRow: 10, splitColumn: 3);

                window1.SheetGrid.SplitRow.Should().Be(10u,
                    "the window that split panes must render the split");
                window1.SheetGrid.SplitColumn.Should().Be(3u);

                // Re-render window 2 -- before the fix this would read the shared
                // Sheet.SplitRow/SplitColumn window 1 just set.
                R49MainWindowTestHarness.Invoke(window2, "UpdateViewport");

                window2.SheetGrid.SplitRow.Should().BeNull(
                    "a sibling New Window that never split panes itself must keep showing no split, " +
                    "exactly like Excel keeps Window > Split per-window");
                window2.SheetGrid.SplitColumn.Should().BeNull();
            }
            finally
            {
                R49MainWindowTestHarness.Close(window1);
                R49MainWindowTestHarness.Close(window2);
            }
        });
    }

    [Fact]
    public void SetFreezePanes_SingleWindow_StillTogglesItsOwnRenderingAndUpdatesTheSharedSheet()
    {
        // Sibling/no-regression: a single window (no sibling in play) must still see its own
        // Freeze Panes toggle take effect immediately, AND the shared Sheet field must still be
        // updated so the change round-trips through save (the per-window store is a display
        // override layered on top, never a replacement for the persisted field).
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);

                InvokeSetFreezePanes(window, frozenRows: 3, frozenCols: 2);

                window.SheetGrid.Viewport?.FrozenPanes.Should().NotBeNull();
                window.SheetGrid.Viewport!.FrozenPanes!.Rows.Should().Be(3u);
                window.SheetGrid.Viewport!.FrozenPanes!.Cols.Should().Be(2u);
                sheet.FrozenRows.Should().Be(3u,
                    "the shared Sheet field must still be updated so Freeze Panes round-trips through save");
                sheet.FrozenCols.Should().Be(2u);

                InvokeSetFreezePanes(window, frozenRows: 0, frozenCols: 0);

                window.SheetGrid.Viewport?.FrozenPanes.Should().BeNull(
                    "unfreezing must still turn frozen panes off locally");
                sheet.FrozenRows.Should().Be(0u);
                sheet.FrozenCols.Should().Be(0u);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void OnSplitDividerMoved_SingleWindow_StillTogglesItsOwnRenderingAndUpdatesTheSharedSheet()
    {
        // Sibling/no-regression counterpart of the Freeze Panes test above, for Window > Split.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);

                InvokeSplitDividerMoved(window, splitRow: 12, splitColumn: 5);

                window.SheetGrid.SplitRow.Should().Be(12u);
                window.SheetGrid.SplitColumn.Should().Be(5u);
                sheet.SplitRow.Should().Be(12u,
                    "the shared Sheet field must still be updated so Split round-trips through save");
                sheet.SplitColumn.Should().Be(5u);

                // OnSplitDividerMoved's null args mean "this axis didn't move" (drag-resize
                // semantics: `splitRow ?? viewState.SplitRow`), not "clear" -- clearing an
                // existing split is SplitViewBtn_Click's job (toggle off), so exercise that
                // instead, exactly like the ribbon Split button would.
                InvokeSplitViewBtnClick(window);

                window.SheetGrid.SplitRow.Should().BeNull("toggling Split off must still take effect locally");
                window.SheetGrid.SplitColumn.Should().BeNull();
                sheet.SplitRow.Should().BeNull();
                sheet.SplitColumn.Should().BeNull();
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void SetFreezePanes_ChangedInOneWindow_CellDataIsStillSharedWithSiblingWindow()
    {
        // No-regression: Freeze Panes is a per-window DISPLAY override layered on top of the
        // shared document -- it must never make the two windows look like they have divergent
        // documents. A cell value written in window 1 must still be visible from window 2's
        // workbook reference, exactly as before this fix.
        StaTestRunner.Run(() =>
        {
            var (window1, workbook) = R49MainWindowTestHarness.CreateWindow();
            var (window2, _) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = GetCurrentSheetId(window1);
                AdoptSameDocument(window2, workbook, sheetId);

                InvokeSetFreezePanes(window1, frozenRows: 2, frozenCols: 1);

                var sheet = workbook.GetSheet(sheetId)!;
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(42));

                workbook.GetSheet(sheetId)!.GetCell(1, 1)!.Value.Should().Be(new NumberValue(42),
                    "the two windows share the same Workbook/Sheet object graph for cell data " +
                    "even though their Freeze Panes display state now differs");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window1);
                R49MainWindowTestHarness.Close(window2);
            }
        });
    }

    private static void AdoptSameDocument(MainWindow window, Workbook workbook, SheetId sheetId)
    {
        R49MainWindowTestHarness.Invoke(
            window,
            "ReplaceWorkbookSession",
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        window.Session.SelectSheet(sheetId);
        window.Session.ActiveSheet.Id.Should().Be(sheetId);
        typeof(MainWindow).GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, sheetId);
    }

    private static SheetId GetCurrentSheetId(MainWindow window) =>
        (SheetId)typeof(MainWindow).GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;

    private static void InvokeSetFreezePanes(MainWindow window, uint frozenRows, uint frozenCols) =>
        R49MainWindowTestHarness.Invoke(window, "SetFreezePanes", frozenRows, frozenCols);

    private static void InvokeSplitDividerMoved(MainWindow window, uint? splitRow, uint? splitColumn) =>
        R49MainWindowTestHarness.Invoke(window, "OnSplitDividerMoved", splitRow, splitColumn);

    private static void InvokeSplitViewBtnClick(MainWindow window) =>
        R49MainWindowTestHarness.Invoke(window, "SplitViewBtn_Click", null, new System.Windows.RoutedEventArgs());
}
