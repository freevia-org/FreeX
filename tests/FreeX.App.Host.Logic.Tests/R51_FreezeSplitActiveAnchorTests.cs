using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R51-commands-freeze-split-view-3-1/3-2
/// (src/FreeX.App.Host/MainWindow.ViewCommands.cs, FreezeAtSelectionMenuItem_Click and
/// SplitViewBtn_Click).
///
/// Before the fix: both handlers derived the freeze/split position from
/// <c>SheetGrid.SelectedRange.Start</c> -- the selection's normalized top-left corner -- instead
/// of the true active/anchor cell. When a selection is extended upward/leftward from its anchor
/// (e.g. click D10, then Shift+click B5 to make B5:D10), the anchor (D10, still the highlighted
/// active cell) and Start (B5, the normalized top-left) diverge, and Excel freezes/splits relative
/// to the ANCHOR, not Start.
///
/// After the fix, both handlers read <c>_selectionAnchor ?? range.Start</c>.
/// </summary>
public sealed class R51_FreezeSplitActiveAnchorTests
{
    [Fact]
    public void FreezeAtSelectionMenuItem_Click_UsesActiveAnchorCell_NotRangeStart()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;
                var anchor = new CellAddress(sheetId, 10, 4); // D10 -- clicked first, stays active.
                var extendTo = new CellAddress(sheetId, 5, 2); // B5 -- Shift+click extends up-left.

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", anchor);
                R49MainWindowTestHarness.Invoke(window, "ExtendSelection", anchor, extendTo);

                // Sanity: the highlighted range normalizes to B5:D10, but the anchor is still D10.
                window.SheetGrid.SelectedRange.Should().Be(new GridRange(extendTo, anchor));

                R49MainWindowTestHarness.Invoke(window, "FreezeAtSelectionMenuItem_Click", null!, null!);

                var sheet = workbook.GetSheetAt(0);
                sheet.FrozenRows.Should().Be(9u,
                    "Excel freezes above the ACTIVE cell (D10, row 10), not the range's normalized top-left (B5, row 5)");
                sheet.FrozenCols.Should().Be(3u,
                    "Excel freezes left of the ACTIVE cell (D10, col 4), not the range's normalized top-left (B5, col 2)");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: a plain (non-extended) selection has anchor == range.Start, so the
    // fix must not change this already-correct case.
    [Fact]
    public void FreezeAtSelectionMenuItem_Click_PlainSelection_StillFreezesAtThatCell()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;
                var cell = new CellAddress(sheetId, 6, 3); // C6, plain click -- no extension.

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", cell);

                R49MainWindowTestHarness.Invoke(window, "FreezeAtSelectionMenuItem_Click", null!, null!);

                var sheet = workbook.GetSheetAt(0);
                sheet.FrozenRows.Should().Be(5u);
                sheet.FrozenCols.Should().Be(2u);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void SplitViewBtn_Click_UsesActiveAnchorCell_NotRangeStart()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;
                var anchor = new CellAddress(sheetId, 10, 4); // D10 -- clicked first, stays active.
                var extendTo = new CellAddress(sheetId, 5, 2); // B5 -- Shift+click extends up-left.

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", anchor);
                R49MainWindowTestHarness.Invoke(window, "ExtendSelection", anchor, extendTo);
                window.SheetGrid.SelectedRange.Should().Be(new GridRange(extendTo, anchor));

                R49MainWindowTestHarness.Invoke(window, "SplitViewBtn_Click", null!, null!);

                var sheet = workbook.GetSheetAt(0);
                sheet.SplitRow.Should().Be(10u,
                    "Excel splits at the ACTIVE cell's row (D10, row 10), not the range's normalized top-left (B5, row 5)");
                sheet.SplitColumn.Should().Be(4u,
                    "Excel splits at the ACTIVE cell's column (D10, col 4), not the range's normalized top-left (B5, col 2)");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: plain (non-extended) selection -- anchor == range.Start.
    [Fact]
    public void SplitViewBtn_Click_PlainSelection_StillSplitsAtThatCell()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;
                var cell = new CellAddress(sheetId, 6, 3); // C6, plain click -- no extension.

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", cell);

                R49MainWindowTestHarness.Invoke(window, "SplitViewBtn_Click", null!, null!);

                var sheet = workbook.GetSheetAt(0);
                sheet.SplitRow.Should().Be(6u);
                sheet.SplitColumn.Should().Be(3u);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
