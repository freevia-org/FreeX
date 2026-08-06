using System.Reflection;
using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R112-app-keyboard-nav-multiarea-tab-1
/// (src/FreeX.App.Host/MainWindow.Selection.cs).
///
/// Before this fix, Tab/Enter on a Ctrl+click multi-area selection (SheetGrid.SelectedRanges has
/// more than one entry) fell through to the plain SetActiveCell path, which unconditionally
/// collapses the whole multi-area selection down to a single cell
/// (SetSelectedRangesIfChanged(null) + SheetGrid.SelectedRange = new GridRange(addr, addr)) instead
/// of cycling the active cell through each area the way real Excel does. The within-range
/// Tab/Enter-cycling branch was explicitly gated on `SheetGrid.SelectedRanges is null`
/// (single-area only), excluding the multi-area case entirely.
///
/// Traversal and wrap decisions are covered directly against GridSelectionNavigationPlanner;
/// these tests retain WPF key dispatch and selection-preservation coverage.
/// </summary>
public sealed class R112_MultiAreaTabEnterCycleTests
{
    [Fact]
    public void Tab_AcrossMultiAreaSelection_CyclesActiveCellThroughEachArea_PreservesWholeSelection()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;
                var area1 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 2)); // A1:B1
                var area2 = new GridRange(new CellAddress(sheetId, 1, 4), new CellAddress(sheetId, 1, 4)); // D1:D1

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheetId, 1, 1));
                SetMultiAreaSelection(window, area1, area2);

                window.SheetGrid.SelectedRanges.Should().NotBeNull();
                window.SheetGrid.SelectedRanges!.Should().BeEquivalentTo(new[] { area1, area2 }, o => o.WithStrictOrdering());
                window.SheetGrid.SelectedRange.Should().Be(area2);
                window.SheetGrid.ActiveCell.Should().Be(new CellAddress(sheetId, 1, 4)); // D1, the just-clicked cell

                // Tab 1: D1:D1 is a 1-cell area, already at its own end -- must jump straight to the
                // START of the NEXT area in click order (wrapping from the last area back to the
                // first), landing on A1, while BOTH areas stay selected.
                PressKey(window, Key.Tab);
                window.SheetGrid.ActiveCell.Should().Be(new CellAddress(sheetId, 1, 1), "Tab from the lone D1 area must wrap to the first area's first cell (A1)");
                window.SheetGrid.SelectedRanges.Should().BeEquivalentTo(new[] { area1, area2 }, o => o.WithStrictOrdering(),
                    "the whole multi-area selection must stay intact -- Tab must never collapse it");

                // Tab 2: still inside area1 (A1:B1) -- must advance WITHIN it to B1, not jump areas.
                PressKey(window, Key.Tab);
                window.SheetGrid.ActiveCell.Should().Be(new CellAddress(sheetId, 1, 2), "Tab from A1 must move within area1 to B1 before leaving it");
                window.SheetGrid.SelectedRanges.Should().BeEquivalentTo(new[] { area1, area2 }, o => o.WithStrictOrdering());

                // Tab 3: B1 is area1's last cell in travel direction -- must jump to area2's first
                // cell (D1), completing the full A1 -> B1 -> D1 -> (back to A1) cycle.
                PressKey(window, Key.Tab);
                window.SheetGrid.ActiveCell.Should().Be(new CellAddress(sheetId, 1, 4), "Tab from B1 (end of area1) must continue on to area2's first cell (D1)");
                window.SheetGrid.SelectedRanges.Should().BeEquivalentTo(new[] { area1, area2 }, o => o.WithStrictOrdering());
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling coverage for the OTHER moveOnly key (Enter, not just Tab) across a multi-area
    // selection -- both keys share the same guard/branch in MainWindow_KeyDown.
    [Fact]
    public void Enter_AcrossMultiAreaSelection_CyclesActiveCellThroughEachArea_PreservesWholeSelection()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;
                var area1 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 1)); // A1:A2
                var area2 = new GridRange(new CellAddress(sheetId, 4, 1), new CellAddress(sheetId, 4, 1)); // A4:A4

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheetId, 1, 1));
                SetMultiAreaSelection(window, area1, area2);

                window.SheetGrid.ActiveCell.Should().Be(new CellAddress(sheetId, 4, 1)); // A4

                // Enter 1: A4:A4 is a 1-cell area -- wraps to the first area's first cell (A1).
                PressKey(window, Key.Enter);
                window.SheetGrid.ActiveCell.Should().Be(new CellAddress(sheetId, 1, 1));
                window.SheetGrid.SelectedRanges.Should().BeEquivalentTo(new[] { area1, area2 }, o => o.WithStrictOrdering());

                // Enter 2: still inside area1 (A1:A2) -- advances within it to A2.
                PressKey(window, Key.Enter);
                window.SheetGrid.ActiveCell.Should().Be(new CellAddress(sheetId, 2, 1));
                window.SheetGrid.SelectedRanges.Should().BeEquivalentTo(new[] { area1, area2 }, o => o.WithStrictOrdering());

                // Enter 3: A2 is area1's last cell -- jumps to area2's first cell (A4).
                PressKey(window, Key.Enter);
                window.SheetGrid.ActiveCell.Should().Be(new CellAddress(sheetId, 4, 1));
                window.SheetGrid.SelectedRanges.Should().BeEquivalentTo(new[] { area1, area2 }, o => o.WithStrictOrdering());
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: Tab on a SINGLE multi-cell range (no Ctrl-added areas,
    // SheetGrid.SelectedRanges is null) must still cycle within that lone range exactly as before,
    // unaffected by extending the guard to also accept the multi-area case.
    [Fact]
    public void Tab_OnSingleAreaMultiCellRange_StillCyclesWithinRange_NotTreatedAsMultiArea()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;
                var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)); // A1:C3

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheetId, 1, 1));
                R49MainWindowTestHarness.Invoke(
                    window, "ExtendSelection", new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3));

                window.SheetGrid.SelectedRanges.Should().BeNull();
                window.SheetGrid.SelectedRange.Should().Be(range);

                PressKey(window, Key.Tab);

                window.SheetGrid.SelectedRange.Should().Be(range,
                    "a single-area multi-cell selection must keep Tab-cycling within it, unaffected by " +
                    "extending the guard to also handle multi-area (Ctrl+click) selections");
                window.SheetGrid.SelectedRanges.Should().BeNull();
                window.SheetGrid.ActiveCell.Should().Be(new CellAddress(sheetId, 1, 2)); // B1
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Backward traversal is covered directly against the shared portable owner because headless
    // WPF cannot reliably synthesize the physical Keyboard.Modifiers Shift state.
    [Fact]
    public void SharedPlanner_ForwardAndBackward_ReportWrapAtOppositeCorners()
    {
        var sheet = new Workbook("Book1").AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));

        var forward = GridSelectionNavigationPlanner.PlanCycle(
            sheet,
            range,
            null,
            range.End,
            GridSelectionCycleKey.Tab,
            forward: true);
        forward.Should().NotBeNull();
        forward!.Value.Target.Should().Be(range.Start);
        forward.Value.WrappedWithinArea.Should().BeTrue();

        var backward = GridSelectionNavigationPlanner.PlanCycle(
            sheet,
            range,
            null,
            range.Start,
            GridSelectionCycleKey.Tab,
            forward: false);
        backward.Should().NotBeNull();
        backward!.Value.Target.Should().Be(range.End);
        backward.Value.WrappedWithinArea.Should().BeTrue();
    }

    // Directly assigns a finished two-area multi-selection so these tests stay focused on keyboard
    // dispatch rather than pointer gesture setup.
    private static void SetMultiAreaSelection(MainWindow window, GridRange firstArea, GridRange activeArea)
    {
        window.SheetGrid.SelectedRanges = new[] { firstArea, activeArea };
        window.SheetGrid.SelectedRange = activeArea;
        var anchorProperty = typeof(MainWindow).GetProperty("_selectionAnchor", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMemberException(nameof(MainWindow), "_selectionAnchor");
        anchorProperty.SetValue(window, (CellAddress?)activeArea.Start);
    }

    private static void PressKey(MainWindow window, Key key)
    {
        var source = PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        R49MainWindowTestHarness.Invoke(window, "MainWindow_KeyDown", window, args);
        R49MainWindowTestHarness.PumpDispatcher();
    }
}
