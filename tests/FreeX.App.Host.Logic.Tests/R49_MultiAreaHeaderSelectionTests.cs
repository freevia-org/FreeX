using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R49-render-multiarea-selection-3-2
/// (src/FreeX.App.Host/MainWindow.Selection.cs, SheetGrid_MouseDown's column/row header branches).
///
/// Before the fix: Ctrl+clicking a second column (or row) header fell into the same `else` branch
/// as a plain click -- SelectColumn/SelectRow always called `SetSelectedRangesIfChanged(null)`,
/// wiping any previously accumulated SheetGrid.SelectedRanges. So Ctrl+click on a column/row header
/// always collapsed the selection down to just the newly clicked column/row, identical to a plain
/// click -- multi-area column/row selection via header Ctrl+click was unreachable.
///
/// After the fix, MainWindow.Selection.cs gained AddAdditionalColumnSelection/
/// AddAdditionalRowSelection (sharing GridSelectionNavigationPlanner's disjoint-area construction
/// with cell-area Ctrl+click) and SheetGrid_MouseDown's header
/// branches now route a Control-held click there instead of through SelectColumn/SelectRow. These
/// tests exercise that new method directly (the underlying reusable unit SheetGrid_MouseDown now
/// dispatches to), since driving an actual pixel-accurate WPF MouseButtonEventArgs through real
/// hit-testing isn't a reliable/deterministic unit-test surface for a header click.
/// </summary>
public sealed class R49_MultiAreaHeaderSelectionTests
{
    [Fact]
    public void AddAdditionalColumnSelection_AfterPlainColumnSelection_AddsDisjointArea_DoesNotWipeIt()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;

                // Plain click on column header B (mirrors SelectColumn(2)).
                R49MainWindowTestHarness.Invoke(window, "SelectColumn", 2u);
                window.SheetGrid.SelectedRanges.Should().BeNull("a plain click never accumulates extra areas");

                // Ctrl+click column header D must ADD D:D as a second disjoint area, not replace B:B.
                R49MainWindowTestHarness.Invoke(window, "AddAdditionalColumnSelection", 4u);

                window.SheetGrid.SelectedRanges.Should().NotBeNull();
                window.SheetGrid.SelectedRanges!.Should().HaveCount(2);
                window.SheetGrid.SelectedRanges![0].Should().Be(WholeColumn(sheetId, 2));
                window.SheetGrid.SelectedRanges![1].Should().Be(WholeColumn(sheetId, 4));

                // The active range (what a subsequent format/edit action targets) is the
                // just-Ctrl-clicked column, not the first one.
                window.SheetGrid.SelectedRange.Should().Be(WholeColumn(sheetId, 4));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void AddAdditionalRowSelection_AfterPlainRowSelection_AddsDisjointArea_DoesNotWipeIt()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;

                R49MainWindowTestHarness.Invoke(window, "SelectRow", 2u);
                R49MainWindowTestHarness.Invoke(window, "AddAdditionalRowSelection", 4u);

                window.SheetGrid.SelectedRanges.Should().NotBeNull();
                window.SheetGrid.SelectedRanges!.Should().HaveCount(2);
                window.SheetGrid.SelectedRanges![0].Should().Be(WholeRow(sheetId, 2));
                window.SheetGrid.SelectedRanges![1].Should().Be(WholeRow(sheetId, 4));
                window.SheetGrid.SelectedRange.Should().Be(WholeRow(sheetId, 4));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: a plain (non-Ctrl) column click still behaves exactly as before --
    // SelectColumn continues to collapse the selection down to just that one column, with no
    // leftover SelectedRanges from any earlier accumulation.
    [Fact]
    public void SelectColumn_PlainClick_StillCollapsesToSingleColumn()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;

                R49MainWindowTestHarness.Invoke(window, "SelectColumn", 2u);
                R49MainWindowTestHarness.Invoke(window, "AddAdditionalColumnSelection", 4u);

                // A subsequent PLAIN click on column F must collapse back down to just F:F.
                R49MainWindowTestHarness.Invoke(window, "SelectColumn", 6u);

                window.SheetGrid.SelectedRanges.Should().BeNull();
                window.SheetGrid.SelectedRange.Should().Be(WholeColumn(sheetId, 6));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static GridRange WholeColumn(SheetId sheetId, uint col) =>
        new(new CellAddress(sheetId, 1, col), new CellAddress(sheetId, CellAddress.MaxRow, col));

    private static GridRange WholeRow(SheetId sheetId, uint row) =>
        new(new CellAddress(sheetId, row, 1), new CellAddress(sheetId, row, CellAddress.MaxCol));
}
