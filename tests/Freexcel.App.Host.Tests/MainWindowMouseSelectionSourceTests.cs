using FluentAssertions;
using System.IO;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowMouseSelectionSourceTests
{
    [Fact]
    public void DragSelectionRequestsEdgeAutoScrollDuringMouseMove()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "Freexcel.App.Host", "MainWindow.Selection.cs"));

        var mouseMoveStart = source.IndexOf("private void SheetGrid_MouseMove", StringComparison.Ordinal);
        var helperStart = source.IndexOf("private void RequestSelectionDragAutoScroll", StringComparison.Ordinal);
        var previewStart = source.IndexOf("private void UpdateCommentPreview", StringComparison.Ordinal);

        mouseMoveStart.Should().BeGreaterThanOrEqualTo(0);
        helperStart.Should().BeGreaterThan(mouseMoveStart);
        previewStart.Should().BeGreaterThan(helperStart);

        var mouseMove = source[mouseMoveStart..helperStart];
        mouseMove.Should().Contain("var pos = e.GetPosition(SheetGrid);");
        mouseMove.Should().Contain("var hitAddr = HitTestCell(pos);");
        mouseMove.Should().Contain("e.Handled = true;");
        mouseMove.Should().Contain("RequestSelectionDragAutoScroll(pos);");
        mouseMove.LastIndexOf("e.Handled = true;", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseMove.IndexOf("RequestSelectionDragAutoScroll(pos);", StringComparison.Ordinal));

        var helper = source[helperStart..previewStart];
        helper.Should().Contain("Freexcel.App.UI.GridView.CalculateAutofillEdgeScrollIntent");
        helper.Should().Contain("SheetGrid.ActualRowHeaderWidth");
        helper.Should().Contain("SheetGrid.EffectiveColHeaderHeight");
        helper.Should().Contain("if (request.HasAnyDirection)");
        helper.Should().Contain("OnAutofillEdgeScrollRequested(request);");
    }

    [Fact]
    public void DragSelectionMouseMoveCancelsWhenLeftButtonIsReleased()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "Freexcel.App.Host", "MainWindow.Selection.cs"));

        var mouseMove = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseMove", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void RequestSelectionDragAutoScroll", StringComparison.Ordinal)];

        mouseMove.Should().Contain("if (!_dragSelectActive)");
        mouseMove.Should().Contain("if (e.LeftButton != MouseButtonState.Pressed)");
        mouseMove.Should().Contain("_formatPainterTargetSelectionActive = false;");
        mouseMove.Should().Contain("_dragSelectActive = false;");
        mouseMove.Should().Contain("_dragSelectAddsAdditionalRange = false;");
        mouseMove.Should().Contain("SheetGrid.ReleaseMouseCapture();");
        mouseMove.Should().Contain("CompleteDragSelectionStatusRefresh();");
        mouseMove.Should().Contain("e.Handled = true;");
        mouseMove.IndexOf("if (e.LeftButton != MouseButtonState.Pressed)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseMove.IndexOf("RequestSelectionDragAutoScroll(pos);", StringComparison.Ordinal));
        mouseMove.IndexOf("e.Handled = true;", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseMove.IndexOf("RequestSelectionDragAutoScroll(pos);", StringComparison.Ordinal));
    }

    [Fact]
    public void CtrlMouseSelectionAddsNonContiguousRangesWithoutBreakingHyperlinkOpen()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "Freexcel.App.Host", "MainWindow.Selection.cs"));
        var windowSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        var mouseDownStart = selectionSource.IndexOf("private void SheetGrid_MouseDown", StringComparison.Ordinal);
        var textInputStart = selectionSource.IndexOf("private void MainWindow_TextInput", StringComparison.Ordinal);
        var mouseMoveStart = selectionSource.IndexOf("private void SheetGrid_MouseMove", StringComparison.Ordinal);
        var autoScrollStart = selectionSource.IndexOf("private void RequestSelectionDragAutoScroll", StringComparison.Ordinal);
        var mouseUpStart = selectionSource.IndexOf("private void SheetGrid_MouseUp", StringComparison.Ordinal);

        var mouseDown = selectionSource[mouseDownStart..textInputStart];
        var mouseMove = selectionSource[mouseMoveStart..autoScrollStart];
        var mouseUp = selectionSource[mouseUpStart..];

        windowSource.Should().Contain("private bool _dragSelectAddsAdditionalRange;");
        mouseDown.Should().Contain("else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)");
        mouseDown.Should().Contain("if (TryOpenHyperlink(newAddr))");
        mouseDown.Should().Contain("AddOrMoveAdditionalSelection(newAddr, extendSelection: false);");
        mouseDown.Should().Contain("_dragSelectAddsAdditionalRange = true;");
        mouseMove.Should().Contain("else if (hitAddr.HasValue && _dragSelectAddsAdditionalRange)");
        mouseMove.Should().Contain("AddOrMoveAdditionalSelection(hitAddr.Value, extendSelection: true);");
        mouseUp.Should().Contain("_dragSelectAddsAdditionalRange = false;");
    }

    [Fact]
    public void MouseDownSelectionIgnoresNonLeftButtonsBeforeHitTesting()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "Freexcel.App.Host", "MainWindow.Selection.cs"));

        var mouseDown = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseDown", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void MainWindow_TextInput", StringComparison.Ordinal)];

        mouseDown.Should().Contain("if (e.ChangedButton != MouseButton.Left)");
        mouseDown.Should().Contain("return;");
        mouseDown.IndexOf("if (e.ChangedButton != MouseButton.Left)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseDown.IndexOf("var pos = e.GetPosition(SheetGrid);", StringComparison.Ordinal));
    }

    [Fact]
    public void MouseDownSelectionHandlesSuccessfulGridSelectionPaths()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "Freexcel.App.Host", "MainWindow.Selection.cs"));

        var mouseDown = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseDown", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void MainWindow_TextInput", StringComparison.Ordinal)];

        var topLeftSelection = mouseDown[
            mouseDown.IndexOf("if (pos.X < rowHeaderW && pos.Y < colHeaderH)", StringComparison.Ordinal)..
            mouseDown.IndexOf("// Column header: select entire column", StringComparison.Ordinal)];
        var columnHeaderSelection = mouseDown[
            mouseDown.IndexOf("if (pos.Y < colHeaderH)", StringComparison.Ordinal)..
            mouseDown.IndexOf("// Row header: select entire row", StringComparison.Ordinal)];
        var rowHeaderSelection = mouseDown[
            mouseDown.IndexOf("// Row header: select entire row", StringComparison.Ordinal)..
            mouseDown.IndexOf("Cell area", StringComparison.Ordinal)];
        var cellSelection = mouseDown[
            mouseDown.IndexOf("if (hitAddress is { } newAddr)", StringComparison.Ordinal)..];

        topLeftSelection.Should().Contain("SelectAll();");
        topLeftSelection.Should().Contain("e.Handled = true;");
        topLeftSelection.IndexOf("e.Handled = true;", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(topLeftSelection.IndexOf("SelectAll();", StringComparison.Ordinal));

        columnHeaderSelection.Should().Contain("SelectColumn(cm.Col);");
        columnHeaderSelection.Should().Contain("e.Handled = true;");
        columnHeaderSelection.LastIndexOf("e.Handled = true;", StringComparison.Ordinal)
            .Should()
            .BeLessThan(columnHeaderSelection.LastIndexOf("return;", StringComparison.Ordinal));

        rowHeaderSelection.Should().Contain("SelectRow(rm.Row);");
        rowHeaderSelection.Should().Contain("e.Handled = true;");
        rowHeaderSelection.LastIndexOf("e.Handled = true;", StringComparison.Ordinal)
            .Should()
            .BeLessThan(rowHeaderSelection.LastIndexOf("return;", StringComparison.Ordinal));

        cellSelection.Should().Contain("SetActiveCell(newAddr);");
        cellSelection.Should().Contain("SheetGrid.CaptureMouse();");
        cellSelection.LastIndexOf("e.Handled = true;", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(cellSelection.LastIndexOf("SheetGrid.CaptureMouse();", StringComparison.Ordinal));
    }

    [Fact]
    public void ShiftHeaderMouseSelectionClearsAdditionalRangesAndRefreshesUi()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "Freexcel.App.Host", "MainWindow.Selection.cs"));

        var mouseDown = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseDown", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void MainWindow_TextInput", StringComparison.Ordinal)];

        var columnShiftSelection = mouseDown[
            mouseDown.IndexOf("uint anchorCol = _selectionAnchor.Value.Col;", StringComparison.Ordinal)..
            mouseDown.IndexOf("else", mouseDown.IndexOf("uint anchorCol = _selectionAnchor.Value.Col;", StringComparison.Ordinal), StringComparison.Ordinal)];
        var rowShiftSelection = mouseDown[
            mouseDown.IndexOf("uint anchorRow = _selectionAnchor.Value.Row;", StringComparison.Ordinal)..
            mouseDown.IndexOf("else", mouseDown.IndexOf("uint anchorRow = _selectionAnchor.Value.Row;", StringComparison.Ordinal), StringComparison.Ordinal)];

        columnShiftSelection.Should().Contain("SheetGrid.SelectedRanges = null;");
        columnShiftSelection.Should().Contain("SheetGrid.SelectedRange = new GridRange(");
        columnShiftSelection.Should().Contain("CellAddressBox.Text");
        columnShiftSelection.Should().Contain("SheetGrid.Focus();");
        columnShiftSelection.Should().Contain("RefreshToolbar();");
        columnShiftSelection.Should().Contain("RefreshStatusBar();");

        rowShiftSelection.Should().Contain("SheetGrid.SelectedRanges = null;");
        rowShiftSelection.Should().Contain("SheetGrid.SelectedRange = new GridRange(");
        rowShiftSelection.Should().Contain("CellAddressBox.Text");
        rowShiftSelection.Should().Contain("SheetGrid.Focus();");
        rowShiftSelection.Should().Contain("RefreshToolbar();");
        rowShiftSelection.Should().Contain("RefreshStatusBar();");
    }

    [Fact]
    public void MouseUpSelectionIgnoresNonLeftButtonsBeforeCompletingDrag()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "Freexcel.App.Host", "MainWindow.Selection.cs"));

        var mouseUp = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseUp", StringComparison.Ordinal)..];

        mouseUp.Should().Contain("if (e.ChangedButton != MouseButton.Left)");
        mouseUp.Should().Contain("return;");
        mouseUp.IndexOf("if (e.ChangedButton != MouseButton.Left)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseUp.IndexOf("if (_formatPainterTargetSelectionActive)", StringComparison.Ordinal));
        mouseUp.IndexOf("if (e.ChangedButton != MouseButton.Left)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseUp.IndexOf("if (!_dragSelectActive)", StringComparison.Ordinal));
    }

    [Fact]
    public void MouseUpSelectionHandlesCompletedDragBeforeReturningToWpf()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "Freexcel.App.Host", "MainWindow.Selection.cs"));

        var mouseUp = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseUp", StringComparison.Ordinal)..];

        var completedDrag = mouseUp[
            mouseUp.IndexOf("if (!_dragSelectActive) return;", StringComparison.Ordinal)..];

        completedDrag.Should().Contain("SheetGrid.ReleaseMouseCapture();");
        completedDrag.Should().Contain("CompleteDragSelectionStatusRefresh();");
        completedDrag.Should().Contain("GetFormulaRangeEntryEditor()?.Focus();");
        completedDrag.Should().Contain("e.Handled = true;");
        completedDrag.LastIndexOf("e.Handled = true;", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(completedDrag.IndexOf("GetFormulaRangeEntryEditor()?.Focus();", StringComparison.Ordinal));
    }

    [Fact]
    public void MouseDownUpdatesActiveSplitPaneRegionOnlyAfterCellHit()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "Freexcel.App.Host", "MainWindow.Selection.cs"));

        var mouseDown = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseDown", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void MainWindow_TextInput", StringComparison.Ordinal)];

        mouseDown.Should().Contain("var hitAddress = Freexcel.App.UI.GridView.HitTestViewportCell(viewport, _currentSheetId, pos);");
        mouseDown.Should().Contain("if (hitAddress is { } newAddr)");
        mouseDown.Should().Contain("_activeSplitPaneRegion = Freexcel.App.UI.GridView.HitTestSplitPaneRegion(viewport, pos);");
        mouseDown.IndexOf("if (hitAddress is { } newAddr)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseDown.IndexOf("_activeSplitPaneRegion = Freexcel.App.UI.GridView.HitTestSplitPaneRegion(viewport, pos);", StringComparison.Ordinal));
    }

    [Fact]
    public void DragSelectionDefersStatusRefreshUntilMouseUp()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "Freexcel.App.Host", "MainWindow.Selection.cs"));
        var windowSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        var extendSelection = selectionSource[
            selectionSource.IndexOf("private void ExtendSelection", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void AddOrMoveAdditionalSelection", StringComparison.Ordinal)];
        var addSelection = selectionSource[
            selectionSource.IndexOf("private void AddOrMoveAdditionalSelection", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void RefreshStatusBarAfterDragSelectionChange", StringComparison.Ordinal)];
        var refreshHelper = selectionSource[
            selectionSource.IndexOf("private void RefreshStatusBarAfterDragSelectionChange", StringComparison.Ordinal)..
            selectionSource.IndexOf("private CellAddress? HitTestCell", StringComparison.Ordinal)];
        var mouseUp = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseUp", StringComparison.Ordinal)..];

        windowSource.Should().Contain("private bool _dragSelectStatusRefreshPending;");
        extendSelection.Should().Contain("RefreshStatusBarAfterDragSelectionChange();");
        addSelection.Should().Contain("RefreshStatusBarAfterDragSelectionChange();");
        refreshHelper.Should().Contain("if (_dragSelectActive)");
        refreshHelper.Should().Contain("_dragSelectStatusRefreshPending = true;");
        refreshHelper.Should().Contain("CompleteDragSelectionStatusRefresh");
        refreshHelper.Should().Contain("RefreshStatusBar();");
        mouseUp.Should().Contain("CompleteDragSelectionStatusRefresh();");
    }
}
