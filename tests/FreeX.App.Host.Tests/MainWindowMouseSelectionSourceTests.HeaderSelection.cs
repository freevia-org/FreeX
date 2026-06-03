using FluentAssertions;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowMouseSelectionSourceTests
{
    [Fact]
    public void ShiftHeaderMouseSelectionClearsAdditionalRangesAndRefreshesUi()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.Selection.cs"));

        var mouseDown = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseDown", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void MainWindow_TextInput", StringComparison.Ordinal)];

        var columnAnchorIndex = mouseDown.IndexOf("uint anchorCol = _selectionAnchor.Value.Col;", StringComparison.Ordinal);
        var rowAnchorIndex = mouseDown.IndexOf("uint anchorRow = _selectionAnchor.Value.Row;", StringComparison.Ordinal);
        var columnShiftSelection = mouseDown[
            mouseDown.LastIndexOf("HideValidationDropdown();", columnAnchorIndex, StringComparison.Ordinal)..
            mouseDown.IndexOf("else", columnAnchorIndex, StringComparison.Ordinal)];
        var rowShiftSelection = mouseDown[
            mouseDown.LastIndexOf("HideValidationDropdown();", rowAnchorIndex, StringComparison.Ordinal)..
            mouseDown.IndexOf("else", rowAnchorIndex, StringComparison.Ordinal)];

        columnShiftSelection.Should().Contain("SetSelectedRangesIfChanged(null);");
        columnShiftSelection.Should().Contain("SheetGrid.SelectedRange = new GridRange(");
        columnShiftSelection.Should().Contain("CellAddressBox.Text");
        columnShiftSelection.Should().Contain("HideValidationDropdown();");
        columnShiftSelection.Should().Contain("SetFormulaBarSelectionText(FormatFormulaBarText(cell, _selectionAnchor.Value));");
        columnShiftSelection.Should().Contain("SheetGrid.Focus();");
        columnShiftSelection.Should().Contain("RefreshToolbarAfterSelectionChange();");
        columnShiftSelection.Should().Contain("RefreshStatusBar();");

        rowShiftSelection.Should().Contain("SetSelectedRangesIfChanged(null);");
        rowShiftSelection.Should().Contain("SheetGrid.SelectedRange = new GridRange(");
        rowShiftSelection.Should().Contain("CellAddressBox.Text");
        rowShiftSelection.Should().Contain("HideValidationDropdown();");
        rowShiftSelection.Should().Contain("SetFormulaBarSelectionText(FormatFormulaBarText(cell, _selectionAnchor.Value));");
        rowShiftSelection.Should().Contain("SheetGrid.Focus();");
        rowShiftSelection.Should().Contain("RefreshToolbarAfterSelectionChange();");
        rowShiftSelection.Should().Contain("RefreshStatusBar();");
    }

    [Fact]
    public void HeaderMouseSelectionClearsStaleCommentPreview()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.Selection.cs"));

        var selectRow = selectionSource[
            selectionSource.IndexOf("private void SelectRow", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void SelectColumn", StringComparison.Ordinal)];
        var selectColumn = selectionSource[
            selectionSource.IndexOf("private void SelectColumn", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void SelectAll", StringComparison.Ordinal)];
        var selectAll = selectionSource[
            selectionSource.IndexOf("private void SelectAll", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void SheetGrid_MouseDown", StringComparison.Ordinal)];
        var mouseDown = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseDown", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void MainWindow_TextInput", StringComparison.Ordinal)];
        var clearTransientOverlays = selectionSource[
            selectionSource.IndexOf("private void ClearSelectionTransientOverlays", StringComparison.Ordinal)..
            selectionSource.IndexOf("private CellAddress? HitTestCell", StringComparison.Ordinal)];

        selectRow.Should().Contain("ClearSelectionTransientOverlays();");
        selectColumn.Should().Contain("ClearSelectionTransientOverlays();");
        selectAll.Should().Contain("ClearSelectionTransientOverlays();");
        clearTransientOverlays.Should().Contain("ClearCommentPreview();");

        var columnAnchorIndex = mouseDown.IndexOf("uint anchorCol = _selectionAnchor.Value.Col;", StringComparison.Ordinal);
        var rowAnchorIndex = mouseDown.IndexOf("uint anchorRow = _selectionAnchor.Value.Row;", StringComparison.Ordinal);
        var columnShiftSelection = mouseDown[
            mouseDown.LastIndexOf("HideValidationDropdown();", columnAnchorIndex, StringComparison.Ordinal)..
            mouseDown.IndexOf("else", columnAnchorIndex, StringComparison.Ordinal)];
        var rowShiftSelection = mouseDown[
            mouseDown.LastIndexOf("HideValidationDropdown();", rowAnchorIndex, StringComparison.Ordinal)..
            mouseDown.IndexOf("else", rowAnchorIndex, StringComparison.Ordinal)];

        columnShiftSelection.Should().Contain("ClearCommentPreview();");
        rowShiftSelection.Should().Contain("ClearCommentPreview();");
    }

    [Fact]
    public void HeaderMouseSelectionBeginsDragAndExtendsAcrossHeaders()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.Selection.cs"));
        var windowSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.xaml.cs"));

        var mouseDown = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseDown", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void MainWindow_TextInput", StringComparison.Ordinal)];
        var headerHelpers = selectionSource[
            selectionSource.IndexOf("private void BeginHeaderSelectionDrag", StringComparison.Ordinal)..
            selectionSource.IndexOf("private CellAddress? HitTestCell", StringComparison.Ordinal)];
        var mouseMove = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseMove", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void RequestSelectionDragAutoScroll", StringComparison.Ordinal)];

        windowSource.Should().Contain("private GridHeaderContextMenuTarget? _dragHeaderSelectionTarget;");
        windowSource.Should().Contain("private uint _dragHeaderSelectionAnchor;");
        mouseDown.Should().Contain("BeginHeaderSelectionDrag(GridHeaderContextMenuTarget.Column, cm.Col);");
        mouseDown.Should().Contain("BeginHeaderSelectionDrag(GridHeaderContextMenuTarget.Row, rm.Row);");
        headerHelpers.Should().Contain("_dragHeaderSelectionTarget = target;");
        headerHelpers.Should().Contain("_dragHeaderSelectionAnchor = index;");
        headerHelpers.Should().Contain("_dragSelectActive = true;");
        headerHelpers.Should().Contain("SheetGrid.CaptureMouse();");
        headerHelpers.Should().Contain("GridHeaderContextMenuHitPlanner.HitTest(");
        headerHelpers.Should().Contain("RefreshToolbarAfterDragSelectionChange();");
        headerHelpers.Should().Contain("RefreshStatusBarAfterDragSelectionChange();");
        mouseMove.Should().Contain("var headerHit = HitTestHeaderSelection(pos);");
        mouseMove.Should().Contain("if (_dragHeaderSelectionTarget is { } headerTarget)");
        mouseMove.Should().Contain("ExtendHeaderSelection(headerTarget, _dragHeaderSelectionAnchor, hit.Index);");
        mouseMove.IndexOf("if (_dragHeaderSelectionTarget is { } headerTarget)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseMove.IndexOf("RequestSelectionDragAutoScroll(pos);", StringComparison.Ordinal));
    }

    [Fact]
    public void HeaderMouseSelectionClearsDragStateOnCancelAndMouseUp()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.Selection.cs"));

        var mouseMove = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseMove", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void RequestSelectionDragAutoScroll", StringComparison.Ordinal)];
        var mouseUp = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseUp", StringComparison.Ordinal)..];

        var cancelBlock = mouseMove[
            mouseMove.IndexOf("if (e.LeftButton != MouseButtonState.Pressed)", StringComparison.Ordinal)..
            mouseMove.IndexOf("e.Handled = true;", mouseMove.IndexOf("if (e.LeftButton != MouseButtonState.Pressed)", StringComparison.Ordinal), StringComparison.Ordinal)];

        cancelBlock.Should().Contain("_dragHeaderSelectionTarget = null;");
        cancelBlock.Should().Contain("_dragHeaderSelectionAnchor = 0;");
        mouseUp.Should().Contain("_dragHeaderSelectionTarget = null;");
        mouseUp.Should().Contain("_dragHeaderSelectionAnchor = 0;");
        mouseUp.IndexOf("_dragHeaderSelectionTarget = null;", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseUp.IndexOf("SheetGrid.ReleaseMouseCapture();", StringComparison.Ordinal));
    }
}
