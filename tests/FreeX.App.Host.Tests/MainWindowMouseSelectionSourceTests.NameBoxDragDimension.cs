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
            selectionSource.IndexOf("internal void ExtendSelection", StringComparison.Ordinal)..
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

    // Once the drag ends, Excel shows the active cell (or a matching defined name), not the full
    // selection range. The shared formatter also keeps this consistent with keyboard selection.
    [Fact]
    public void CompleteDragSelectionStatusRefreshRestoresExcelNameBoxSelectionText()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var completeRefresh = selectionSource[
            selectionSource.IndexOf("internal void CompleteDragSelectionStatusRefresh", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void BeginHeaderSelectionDrag", StringComparison.Ordinal)];

        completeRefresh.Should().Contain("if (SheetGrid.SelectedRange is { } activeRange)");
        completeRefresh.Should().Contain("SetCellAddressBoxSelectionText(FormatNameBoxSelectionText(activeRange));");
    }
}
