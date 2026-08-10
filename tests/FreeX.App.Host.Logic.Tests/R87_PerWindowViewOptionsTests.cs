using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R87-order-guard-window-state-sweep-1: the WPF host never got the R83/
/// R86 per-window-independence fix for View tab display toggles -- Gridlines/Headings/Rulers were
/// still read straight off the shared <see cref="Sheet"/> (MainWindow.Viewport.cs's
/// <c>UpdateViewport</c>) and written with the "other two" fields taken straight off the shared
/// sheet too (MainWindow.ViewCommands.cs), so toggling any of them in one Excel "View &gt; New
/// Window" sibling instantly leaked into every other window viewing the same document, unlike
/// ViewMode/Zoom (R83) which already went through the per-window <c>_worksheetViewStates</c>
/// store via <c>GetEffectiveViewState</c>/<c>SyncWindowViewState</c>.
///
/// These tests simulate two "New Window" siblings viewing the very same <see cref="Workbook"/>/
/// <see cref="Sheet"/> object graph (the actual bug precondition -- see
/// MainWindow.MultiWindow.cs's <c>AdoptSharedWorkbook</c>/<c>_workbookRef</c>) by constructing two
/// independent <see cref="MainWindow"/> instances via <see cref="R49MainWindowTestHarness"/> and
/// then replacing the second window's authoritative <see cref="WorkbookSession"/> with one over
/// the first window's actual (post-Loaded) workbook/sheet -- the lightweight equivalent of the
/// real DI-driven "New Window" wiring, which needs a live <c>App.Services</c> container this test
/// project does not stand up.
/// </summary>
public sealed class R87_PerWindowViewOptionsTests
{
    [Fact]
    public void ViewGridlinesChk_Changed_TurnedOffInOneWindow_DoesNotLeakIntoSiblingWindow()
    {
        StaTestRunner.Run(() =>
        {
            var (window1, workbook) = R49MainWindowTestHarness.CreateWindow();
            var (window2, _) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                AdoptSameDocument(window2, workbook, GetCurrentSheetId(window1));

                // Window 2 renders first, seeding its own per-window store from the (still true)
                // shared ShowGridlines.
                R49MainWindowTestHarness.Invoke(window2, "UpdateViewport");
                window2.SheetGrid.ShowGridLines.Should().BeTrue("gridlines are on by default");

                // Window 1 turns Gridlines off via the View tab checkbox handler.
                InvokeGridlinesChanged(window1, isChecked: false);

                window1.SheetGrid.ShowGridLines.Should().BeFalse(
                    "the window that unchecked View > Gridlines must stop showing them");

                // Re-render window 2 (e.g. a later, unrelated redraw) -- before the fix this would
                // read the shared Sheet.ShowGridlines field window 1 just flipped to false.
                R49MainWindowTestHarness.Invoke(window2, "UpdateViewport");

                window2.SheetGrid.ShowGridLines.Should().BeTrue(
                    "a sibling New Window that never touched Gridlines itself must keep showing them, " +
                    "exactly like Excel keeps View tab toggles per-window");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window1);
                R49MainWindowTestHarness.Close(window2);
            }
        });
    }

    [Fact]
    public void ViewHeadersChk_Changed_TurnedOffInOneWindow_DoesNotLeakIntoSiblingWindow()
    {
        StaTestRunner.Run(() =>
        {
            var (window1, workbook) = R49MainWindowTestHarness.CreateWindow();
            var (window2, _) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                AdoptSameDocument(window2, workbook, GetCurrentSheetId(window1));
                R49MainWindowTestHarness.Invoke(window2, "UpdateViewport");
                window2.SheetGrid.ShowHeaders.Should().BeTrue("headings are on by default");

                InvokeHeadersChanged(window1, isChecked: false);
                window1.SheetGrid.ShowHeaders.Should().BeFalse(
                    "the window that unchecked View > Headings must stop showing them");

                R49MainWindowTestHarness.Invoke(window2, "UpdateViewport");

                window2.SheetGrid.ShowHeaders.Should().BeTrue(
                    "a sibling New Window that never touched Headings itself must keep showing them");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window1);
                R49MainWindowTestHarness.Close(window2);
            }
        });
    }

    [Fact]
    public void ViewGridlinesChk_Changed_SingleWindow_StillTogglesItsOwnRendering()
    {
        // Sibling/no-regression: a single window (no sibling in play) must still see its own
        // Gridlines toggle take effect immediately, exactly as before this fix.
        StaTestRunner.Run(() =>
        {
            var (window, _) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                window.SheetGrid.ShowGridLines.Should().BeTrue();

                InvokeGridlinesChanged(window, isChecked: false);
                window.SheetGrid.ShowGridLines.Should().BeFalse("unchecking Gridlines must still turn them off locally");

                InvokeGridlinesChanged(window, isChecked: true);
                window.SheetGrid.ShowGridLines.Should().BeTrue("re-checking Gridlines must still turn them back on locally");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void ViewGridlinesChk_Changed_PreservesThisWindowsOwnHeadingsAcrossToggle()
    {
        // Sibling/no-regression: toggling Gridlines while a sibling window has changed the shared
        // Sheet's Headings must NOT adopt that sibling's Headings value as the "preserved" other
        // field -- it must keep reading THIS window's own effective Headings (still true here,
        // since this window never touched Headings), not whatever the raw shared Sheet field says.
        StaTestRunner.Run(() =>
        {
            var (window1, workbook) = R49MainWindowTestHarness.CreateWindow();
            var (window2, _) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                AdoptSameDocument(window2, workbook, GetCurrentSheetId(window1));
                R49MainWindowTestHarness.Invoke(window1, "UpdateViewport");

                // Window 2 turns Headings off on the shared sheet.
                InvokeHeadersChanged(window2, isChecked: false);

                // Window 1 (which never touched Headings) now toggles Gridlines off.
                InvokeGridlinesChanged(window1, isChecked: false);

                window1.SheetGrid.ShowGridLines.Should().BeFalse();
                window1.SheetGrid.ShowHeaders.Should().BeTrue(
                    "window 1's own Headings must remain on -- toggling Gridlines must not adopt " +
                    "window 2's Headings change via the shared Sheet field");
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
        typeof(MainWindow).GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, sheetId);
    }

    private static SheetId GetCurrentSheetId(MainWindow window) =>
        (SheetId)typeof(MainWindow).GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;

    private static void InvokeGridlinesChanged(MainWindow window, bool isChecked)
    {
        var chk = new CheckBox { IsChecked = isChecked };
        R49MainWindowTestHarness.Invoke(window, "ViewGridlinesChk_Changed", chk, new RoutedEventArgs());
    }

    private static void InvokeHeadersChanged(MainWindow window, bool isChecked)
    {
        var chk = new CheckBox { IsChecked = isChecked };
        R49MainWindowTestHarness.Invoke(window, "ViewHeadersChk_Changed", chk, new RoutedEventArgs());
    }
}
