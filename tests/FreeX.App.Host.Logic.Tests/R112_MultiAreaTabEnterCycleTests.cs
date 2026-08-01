using System.Reflection;
using System.Windows;
using System.Windows.Input;
using FluentAssertions;
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
/// Backward (Shift+Tab) cycling is exercised at the unit level via
/// <see cref="AdvanceActiveCellWithinRange_ForwardAndBackward_ReportWrapAtOppositeCorners"/>
/// instead of through the full MainWindow_KeyDown dispatch: unlike the existing F8
/// "ExcelSelectionMode.Extend" precedent (see R87_ProtectionExtendSelectionTests /
/// R68_F8ExtendSelectionMouseClickTests) that stands in for Shift when the code being tested reads
/// `_selectionMode`/`ShouldExtendSelection`, this Tab/Enter branch reads the raw
/// `Keyboard.Modifiers` Shift bit directly (Shift+Tab reverses cycling DIRECTION, it does not enter
/// "extend selection" mode), and there is no equivalent non-physical stand-in for that in a
/// headless test run.
///
/// The multi-area PRECONDITION below is built by assigning SheetGrid.SelectedRanges/SelectedRange
/// directly instead of driving two separate AddOrMoveAdditionalSelection Ctrl+click calls: while
/// writing this test it surfaced that CreateAdditionalSelectionRanges (a few hundred lines below in
/// the same file) currently can NEVER append a genuinely new disjoint cell area -- its
/// "ranges[last] == currentActive" heuristic for distinguishing "still dragging out the
/// currently-active area" from "a fresh click elsewhere" is always true (every call leaves
/// SheetGrid.SelectedRange equal to the accumulated list's last entry, exactly the same
/// self-defeating check already called out in the comment above AddAdditionalColumnSelection for
/// why THAT header path had to avoid reusing this helper). So two sequential Ctrl+clicks on
/// different cells silently REPLACE rather than accumulate: SheetGrid.SelectedRanges never exceeds
/// one entry via the real cell Ctrl+click gesture today. That is a separate, pre-existing defect
/// from the one this file fixes (see siblingLeads in the round report) -- constructing the
/// precondition state directly here keeps this test's assertions scoped to what it actually owns:
/// MainWindow_KeyDown's Tab/Enter handling of an (however-constructed) existing multi-area
/// selection, which is the real entry point for the R112 defect.
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

    // Unit-level coverage for the backward (Shift+Tab / Shift+Enter) direction, which reverses
    // which corner counts as "finished" -- exercised directly against the actual private static
    // helper the fix extended (rather than through the full KeyDown dispatch, since faking a
    // physical Shift modifier is not reliably possible in a headless WPF test: see the class-level
    // remarks). Confirms `wrappedPastEnd` fires at the OPPOSITE corner when going backward, which is
    // the piece of logic the multi-area area-switch decision in MainWindow_KeyDown depends on.
    [Fact]
    public void AdvanceActiveCellWithinRange_ForwardAndBackward_ReportWrapAtOppositeCorners()
    {
        var sheetId = new Workbook("Book1").AddSheet("Sheet1").Id;
        var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)); // A1:B2
        var method = typeof(MainWindow).GetMethod(
            "AdvanceActiveCellWithinRange", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(MainWindow), "AdvanceActiveCellWithinRange");

        // Forward Tab from the range's bottom-right corner (B2) must report wrappedPastEnd = true.
        var forwardArgs = new object?[] { range, new CellAddress(sheetId, 2, 2), true, true, null };
        var forwardResult = (CellAddress)method.Invoke(null, forwardArgs)!;
        forwardResult.Should().Be(new CellAddress(sheetId, 1, 1));
        ((bool)forwardArgs[4]!).Should().BeTrue("B2 is the range's last cell going forward");

        // Forward Tab from a non-corner cell (A1) must NOT report a wrap.
        var forwardMidArgs = new object?[] { range, new CellAddress(sheetId, 1, 1), true, true, null };
        method.Invoke(null, forwardMidArgs);
        ((bool)forwardMidArgs[4]!).Should().BeFalse("A1 is not yet the range's last cell going forward");

        // Backward (Shift+Tab) from the range's TOP-LEFT corner (A1) must report wrappedPastEnd =
        // true -- the opposite corner from the forward case.
        var backwardArgs = new object?[] { range, new CellAddress(sheetId, 1, 1), true, false, null };
        var backwardResult = (CellAddress)method.Invoke(null, backwardArgs)!;
        backwardResult.Should().Be(new CellAddress(sheetId, 2, 2));
        ((bool)backwardArgs[4]!).Should().BeTrue("A1 is the range's last cell going backward");

        // Backward Tab from a non-corner cell (B2) must NOT report a wrap.
        var backwardMidArgs = new object?[] { range, new CellAddress(sheetId, 2, 2), true, false, null };
        method.Invoke(null, backwardMidArgs);
        ((bool)backwardMidArgs[4]!).Should().BeFalse("B2 is not yet the range's last cell going backward");
    }

    // Directly assigns a finished two-area multi-selection (SheetGrid.SelectedRanges = both areas,
    // SheetGrid.SelectedRange = the last/active one, active cell = its Start) -- see the
    // class-level remarks for why this bypasses AddOrMoveAdditionalSelection/
    // CreateAdditionalSelectionRanges rather than driving it through two Ctrl+clicks.
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
