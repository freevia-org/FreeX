using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R99-render-header-select-merge-expand (src/FreeX.App.Host/MainWindow.Selection.cs,
/// SelectColumn/SelectRow + AddAdditionalColumnSelection/AddAdditionalRowSelection +
/// ExtendHeaderSelection): clicking the header of a column/row that a merged region only
/// partially spans used to select just that single column/row, silently truncating the merge's
/// visible footprint. Every other selection gesture in this file (drag-select, Shift+click
/// extend, Ctrl+click add-to-cell-selection, Name Box/Go To navigation) already routes through
/// <c>ExpandRangeToFullyContainMerges</c> before assigning <c>SheetGrid.SelectedRange</c> --
/// header-click selection was the one gesture that didn't, in violation of the Excel invariant
/// that a rectangular selection can never include only part of a merged cell.
///
/// Invokes the real product methods (SelectColumn/SelectRow/AddAdditionalColumnSelection/
/// AddAdditionalRowSelection/ExtendHeaderSelection) via the shared MainWindow test harness rather
/// than a hand-built selection model -- the nearest headless seam available for App.Host, since
/// MainWindow itself is a WPF window (StaTestRunner + R49MainWindowTestHarness are this project's
/// standard way to exercise it without a full interactive MouseButtonEventArgs round trip).
/// </summary>
public sealed class R99_HeaderClickMergeExpansionTests
{
    [Fact]
    public void SelectColumn_HeaderPartiallyOverlappingMerge_ExpandsToFullMergeFootprint()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.Sheets[0];
                // B1:C2 merged -- clicking column B's header must expand to cover column C too.
                var mergeRegion = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 2, 3));
                sheet.AddMergedRegion(mergeRegion);

                R49MainWindowTestHarness.Invoke(window, "SelectColumn", 2u);

                window.SheetGrid.SelectedRange.Should().Be(
                    new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, CellAddress.MaxRow, 3)),
                    "clicking column B's header must expand the selection to cover column C too, " +
                    "since real Excel never lets a rectangular selection bisect a merged cell");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void SelectRow_HeaderPartiallyOverlappingMerge_ExpandsToFullMergeFootprint()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.Sheets[0];
                // B2:C3 merged -- clicking row 2's header must expand to cover row 3 too.
                var mergeRegion = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 3, 3));
                sheet.AddMergedRegion(mergeRegion);

                R49MainWindowTestHarness.Invoke(window, "SelectRow", 2u);

                window.SheetGrid.SelectedRange.Should().Be(
                    new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 3, CellAddress.MaxCol)),
                    "clicking row 2's header must expand the selection to cover row 3 too, " +
                    "since real Excel never lets a rectangular selection bisect a merged cell");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void AddAdditionalColumnSelection_CtrlClickHeaderPartiallyOverlappingMerge_ExpandsInSelectedRangesToo()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.Sheets[0];
                var mergeRegion = new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 1, 5));
                sheet.AddMergedRegion(mergeRegion);

                R49MainWindowTestHarness.Invoke(window, "SelectColumn", 2u);
                // Ctrl+click column D's header (D4:E1 merge straddles D/E) must add the FULL D:E
                // footprint as the second disjoint area, not just D:D.
                R49MainWindowTestHarness.Invoke(window, "AddAdditionalColumnSelection", 4u);

                var expectedSecondArea = new GridRange(
                    new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, CellAddress.MaxRow, 5));
                window.SheetGrid.SelectedRange.Should().Be(expectedSecondArea);
                window.SheetGrid.SelectedRanges.Should().NotBeNull();
                window.SheetGrid.SelectedRanges!.Should().HaveCount(2);
                window.SheetGrid.SelectedRanges![1].Should().Be(expectedSecondArea,
                    "the accumulated multi-area SelectedRanges list must also carry the expanded footprint, " +
                    "not the truncated single-column rectangle, so a subsequent format command hits the whole merge");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: a header click with NO merge anywhere near the clicked column/row
    // must still select exactly that single column/row (ExpandRangeToFullyContainMerges is a
    // documented no-op with no overlapping merges present).
    [Fact]
    public void SelectColumn_NoMerge_StillSelectsSinglePlainColumn()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.Sheets[0];

                R49MainWindowTestHarness.Invoke(window, "SelectColumn", 2u);

                window.SheetGrid.SelectedRange.Should().Be(
                    new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, CellAddress.MaxRow, 2)));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
