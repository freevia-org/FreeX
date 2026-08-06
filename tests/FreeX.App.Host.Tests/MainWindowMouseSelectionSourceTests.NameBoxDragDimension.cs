using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowMouseSelectionSourceTests
{
    // R69-render-active-cell-selection-6-2: while a mouse-drag selection is in progress, Excel's
    // Name Box shows a live "{rows}R x {cols}C" dimension readout instead of the range address,
    // reverting to the plain address once the drag ends. Before this fix ExtendSelection always
    // wrote the plain range address to the Name Box, even mid-drag.
    [Fact]
    public void ExtendSelectionShowsLiveDimensionTextInNameBoxWhileDragging()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var extendSelection = selectionSource[
            selectionSource.IndexOf("private void ExtendSelection", StringComparison.Ordinal)..
            selectionSource.IndexOf("private static GridRange ExpandRangeToFullyContainMerges", StringComparison.Ordinal)];

        extendSelection.Should().Contain("SetCellAddressBoxSelectionText(_dragSelectActive");
        extendSelection.Should().Contain("? GridSelectionNavigationPlanner.FormatDragDimensionText(range)");
        extendSelection.Should().Contain(": FormatRangeReference(range.Start, range.End));");
    }

    [Fact]
    public void SharedDragDimensionProjectionFormatsRowsByColumns()
    {
        var sheet = new Workbook("Book1").AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 5, 4));

        GridSelectionNavigationPlanner.FormatDragDimensionText(range).Should().Be("4R x 3C");
    }

    // No-regression: once the drag ends, CompleteDragSelectionStatusRefresh must revert the Name
    // Box back to the plain range address (matching a keyboard-driven selection, which never shows
    // dimension text at all since it never sets _dragSelectActive).
    [Fact]
    public void CompleteDragSelectionStatusRefreshRevertsNameBoxToPlainAddress()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var completeRefresh = selectionSource[
            selectionSource.IndexOf("private void CompleteDragSelectionStatusRefresh", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void BeginHeaderSelectionDrag", StringComparison.Ordinal)];

        completeRefresh.Should().Contain("if (SheetGrid.SelectedRange is { } activeRange)");
        completeRefresh.Should().Contain("SetCellAddressBoxSelectionText(FormatRangeReference(activeRange.Start, activeRange.End));");
    }
}
