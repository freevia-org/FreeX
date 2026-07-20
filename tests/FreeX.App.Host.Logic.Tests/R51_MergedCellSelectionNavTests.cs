using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R51-render-merged-cell-edit-nav-3-1/3-2/3-3/3-4
/// (src/FreeX.App.Host/MainWindow.Selection.cs).
///
/// 3-1: plain arrow-key navigation off a merged cell wider/taller than 1 unit in the travel
/// direction must step past the merge's far edge, not be silently re-absorbed back into it.
/// 3-2: Tab/Enter on a LONE selected merged cell must skip straight past it (like 3-1), not be
/// mistaken for a genuine multi-cell range selection to Tab-cycle within.
/// 3-3: Ctrl+clicking anywhere inside a merged cell (not just its anchor) must add the WHOLE
/// merge as the new selection area, not a degenerate sub-cell sliver.
/// 3-4: Shift+click/drag-extend that only partially overlaps a merged cell must snap the
/// selection rectangle to fully contain the whole merge.
/// </summary>
public sealed class R51_MergedCellSelectionNavTests
{
    [Fact]
    public void ArrowKey_OnWideMergedCell_MovesPastFarEdge_NotAbsorbedByReSnap()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var merge = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 4)); // B2:D2
                sheet.AddMergedRegion(merge);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheetId, 2, 2));
                window.SheetGrid.SelectedRange.Should().Be(merge);

                PressKey(window, Key.Right);

                var expected = new CellAddress(sheetId, 2, 5); // E2
                window.SheetGrid.SelectedRange.Should().Be(new GridRange(expected, expected),
                    "Right must step past the whole merge's far edge (to E2), not be re-absorbed back into B2:D2");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void ArrowKey_OnTallMergedCell_MovesPastFarEdge()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var merge = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 4, 2)); // B2:B4
                sheet.AddMergedRegion(merge);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheetId, 2, 2));
                window.SheetGrid.SelectedRange.Should().Be(merge);

                PressKey(window, Key.Down);

                var expected = new CellAddress(sheetId, 5, 2); // B5
                window.SheetGrid.SelectedRange.Should().Be(new GridRange(expected, expected),
                    "Down must step past the whole merge's far edge (to B5), not be re-absorbed back into B2:B4");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: arrow-key navigation off a plain (unmerged) cell is unaffected.
    [Fact]
    public void ArrowKey_OnPlainCell_StillMovesOneCell()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheetId, 2, 2)); // B2

                PressKey(window, Key.Right);

                var expected = new CellAddress(sheetId, 2, 3); // C2
                window.SheetGrid.SelectedRange.Should().Be(new GridRange(expected, expected));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void Tab_OnLoneSelectedMergedCell_SkipsPastMerge_NotTabCycleWithinItsInterior()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var merge = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 4)); // B2:D2
                sheet.AddMergedRegion(merge);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheetId, 2, 2));
                window.SheetGrid.SelectedRange.Should().Be(merge);

                PressKey(window, Key.Tab);

                var expected = new CellAddress(sheetId, 2, 5); // E2
                window.SheetGrid.SelectedRange.Should().Be(new GridRange(expected, expected),
                    "Tab on a lone selected merged cell must skip straight to the next unmerged cell (E2), " +
                    "not Tab-cycle through the merge's own (blank) interior sub-cells");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: Tab on a GENUINE multi-cell range (no merge involved) must still
    // cycle the active cell within the range instead of collapsing/escaping it.
    [Fact]
    public void Tab_OnGenuineMultiCellRange_StillCyclesWithinRange_NotTreatedAsMerge()
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
                window.SheetGrid.SelectedRange.Should().Be(range);

                PressKey(window, Key.Tab);

                window.SheetGrid.SelectedRange.Should().Be(range,
                    "a real multi-cell range selection must keep Tab-cycling within it, unaffected by the " +
                    "merge-vs-real-range distinction added for the merge fix");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void CtrlClickInsideMerge_AddsWholeMergeAsNewArea_NotSubCellSliver()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var merge = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 4)); // B2:D2
                sheet.AddMergedRegion(merge);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheetId, 1, 1)); // A1

                // Ctrl+click hit-tests to C2 -- an interior (non-anchor) sub-cell of the merge.
                R49MainWindowTestHarness.Invoke(
                    window, "AddOrMoveAdditionalSelection", new CellAddress(sheetId, 2, 3), false);

                window.SheetGrid.SelectedRange.Should().Be(merge,
                    "Ctrl+clicking anywhere inside a merged cell must add the WHOLE merge (B2:D2) as the " +
                    "new area, not a degenerate sub-cell sliver (C2:C2)");
                window.SheetGrid.SelectedRanges.Should().NotBeNull();
                window.SheetGrid.SelectedRanges!.Should().Contain(merge);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: Ctrl+click on a plain (unmerged) cell still adds just that cell.
    [Fact]
    public void CtrlClickOnPlainCell_StillAddsJustThatCell()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheetId, 1, 1));

                var target = new CellAddress(sheetId, 5, 5); // E5, no merge.
                R49MainWindowTestHarness.Invoke(window, "AddOrMoveAdditionalSelection", target, false);

                window.SheetGrid.SelectedRange.Should().Be(new GridRange(target, target));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void ExtendSelection_PartiallyOverlappingMerge_SnapsToFullyContainIt()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var merge = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 4)); // B2:D2
                sheet.AddMergedRegion(merge);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheetId, 1, 1)); // A1

                // Shift+click C3: the raw rectangle A1:C3 clips through columns B-C of the merge
                // but excludes column D.
                R49MainWindowTestHarness.Invoke(
                    window, "ExtendSelection", new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3));

                window.SheetGrid.SelectedRange.Should().Be(
                    new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 4)),
                    "the selection rectangle must expand to fully contain the merge it partially overlaps " +
                    "(A1:D3), matching Excel's guarantee that a selection never bisects a merged cell");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: extending a selection with no merge overlap is unaffected.
    [Fact]
    public void ExtendSelection_NoMergeOverlap_StaysExactlyAsComputed()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheetId, 1, 1));

                R49MainWindowTestHarness.Invoke(
                    window, "ExtendSelection", new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3));

                window.SheetGrid.SelectedRange.Should().Be(
                    new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
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
