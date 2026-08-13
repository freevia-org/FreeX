using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowMouseSelectionSourceTests
{
    [Fact]
    public void DragSelectionRequestsEdgeAutoScrollDuringMouseMove()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var mouseMoveStart = source.IndexOf("private void SheetGrid_MouseMove", StringComparison.Ordinal);
        var helperStart = source.IndexOf("private void RequestSelectionDragAutoScroll", StringComparison.Ordinal);
        var previewStart = source.IndexOf("private void UpdateCommentPreview", StringComparison.Ordinal);

        mouseMoveStart.Should().BeGreaterThanOrEqualTo(0);
        helperStart.Should().BeGreaterThan(mouseMoveStart);
        previewStart.Should().BeGreaterThan(helperStart);

        var mouseMove = source[mouseMoveStart..helperStart];
        mouseMove.Should().Contain("var pos = e.GetPosition(SheetGrid);");
        mouseMove.Should().Contain("var hitAddr = _dragHeaderSelectionTarget.HasValue ? null : HitTestCell(pos);");
        mouseMove.Should().Contain("e.Handled = true;");
        mouseMove.Should().Contain("RequestSelectionDragAutoScroll(pos);");
        mouseMove.LastIndexOf("e.Handled = true;", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseMove.IndexOf("RequestSelectionDragAutoScroll(pos);", StringComparison.Ordinal));

        var helper = source[helperStart..previewStart];
        helper.Should().Contain("FreeX.App.UI.GridView.CalculateAutofillEdgeScrollIntent");
        helper.Should().Contain("SheetGrid.ActualRowHeaderWidth");
        helper.Should().Contain("SheetGrid.EffectiveColHeaderHeight");
        helper.Should().Contain("if (request.HasAnyDirection)");
        helper.Should().Contain("OnAutofillEdgeScrollRequested(request);");
    }

    [Fact]
    public void DragSelectionMouseMoveCancelsWhenLeftButtonIsReleased()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var mouseMove = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseMove", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void RequestSelectionDragAutoScroll", StringComparison.Ordinal)];

        mouseMove.Should().Contain("if (!_dragSelectActive)");
        mouseMove.Should().Contain("if (e.LeftButton != MouseButtonState.Pressed)");
        mouseMove.Should().Contain("_formatPainterTargetSelectionActive = false;");
        mouseMove.Should().Contain("_dragSelectActive = false;");
        mouseMove.Should().Contain("_dragSelectAddsAdditionalRange = false;");
        mouseMove.Should().Contain("SheetGrid.ReleaseMouseCapture();");
        mouseMove.Should().Contain("CompleteDragSelectionToolbarRefresh();");
        mouseMove.Should().Contain("CompleteDragSelectionStatusRefresh();");
        mouseMove.Should().Contain("if (hitAddr.HasValue)");
        mouseMove.Should().Contain("UpdateCommentPreview(hitAddr.Value);");
        mouseMove.Should().Contain("ClearCommentPreview();");
        mouseMove.Should().Contain("e.Handled = true;");
        var cancelBlock = mouseMove[
            mouseMove.IndexOf("if (e.LeftButton != MouseButtonState.Pressed)", StringComparison.Ordinal)..
            mouseMove.IndexOf("RequestSelectionDragAutoScroll(pos);", StringComparison.Ordinal)];

        mouseMove.IndexOf("if (e.LeftButton != MouseButtonState.Pressed)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseMove.IndexOf("RequestSelectionDragAutoScroll(pos);", StringComparison.Ordinal));
        mouseMove.IndexOf("e.Handled = true;", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseMove.IndexOf("RequestSelectionDragAutoScroll(pos);", StringComparison.Ordinal));
        cancelBlock.IndexOf("CompleteDragSelectionToolbarRefresh();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(cancelBlock.IndexOf("CompleteDragSelectionStatusRefresh();", StringComparison.Ordinal));
        cancelBlock.IndexOf("CompleteDragSelectionStatusRefresh();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(cancelBlock.IndexOf("UpdateCommentPreview(hitAddr.Value);", StringComparison.Ordinal));
    }

    [Fact]
    public void CtrlMouseSelectionAddsNonContiguousRangesWithoutBreakingHyperlinkOpen()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");
        var windowSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");

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
    public void CtrlMouseSelectionHidesValidationDropdownBeforeAddingRange()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var addSelection = selectionSource[
            selectionSource.IndexOf("private void AddOrMoveAdditionalSelection", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void RefreshStatusBarAfterDragSelectionChange", StringComparison.Ordinal)];

        addSelection.Should().Contain("ClearSelectionTransientOverlays();");
        addSelection.Should().Contain("SetSelectedRangesIfChanged(ranges);");
        addSelection.IndexOf("ClearSelectionTransientOverlays();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(addSelection.IndexOf("SetSelectedRangesIfChanged(ranges);", StringComparison.Ordinal));
    }

    [Fact]
    public void ShiftCellMouseSelectionHidesValidationDropdownBeforeExtendingRange()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var shiftCellSelection = selectionSource[
            selectionSource.IndexOf("private bool TryHandleCellAreaExtendClick", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void MainWindow_TextInput", StringComparison.Ordinal)];

        shiftCellSelection.Should().Contain("HideValidationDropdown();");
        shiftCellSelection.Should().Contain("ExtendSelection(_selectionAnchor.Value, newAddr);");
        shiftCellSelection.IndexOf("HideValidationDropdown();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(shiftCellSelection.IndexOf("ExtendSelection(_selectionAnchor.Value, newAddr);", StringComparison.Ordinal));
    }

    [Fact]
    public void DragRangeExtensionHidesValidationDropdownBeforeReplacingSelection()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var extendSelection = selectionSource[
            selectionSource.IndexOf("private void ExtendSelection", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void AddOrMoveAdditionalSelection", StringComparison.Ordinal)];

        extendSelection.Should().Contain("ClearSelectionTransientOverlays();");
        extendSelection.Should().Contain("SheetGrid.SelectedRange = range;");
        extendSelection.IndexOf("ClearSelectionTransientOverlays();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(extendSelection.IndexOf("SheetGrid.SelectedRange = range;", StringComparison.Ordinal));
    }

    [Fact]
    public void RangeMouseSelectionClearsStaleCommentPreviewBeforeReplacingSelection()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var extendSelection = selectionSource[
            selectionSource.IndexOf("private void ExtendSelection", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void AddOrMoveAdditionalSelection", StringComparison.Ordinal)];
        var addSelection = selectionSource[
            selectionSource.IndexOf("private void AddOrMoveAdditionalSelection", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void RefreshStatusBarAfterDragSelectionChange", StringComparison.Ordinal)];
        var clearTransientOverlays = selectionSource[
            selectionSource.IndexOf("private void ClearSelectionTransientOverlays", StringComparison.Ordinal)..
            selectionSource.IndexOf("private CellAddress? HitTestCell", StringComparison.Ordinal)];

        extendSelection.Should().Contain("ClearSelectionTransientOverlays();");
        addSelection.Should().Contain("ClearSelectionTransientOverlays();");
        clearTransientOverlays.Should().Contain("ClearCommentPreview();");
        extendSelection.IndexOf("ClearSelectionTransientOverlays();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(extendSelection.IndexOf("SheetGrid.SelectedRange = range;", StringComparison.Ordinal));
        addSelection.IndexOf("ClearSelectionTransientOverlays();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(addSelection.IndexOf("SetSelectedRangesIfChanged(ranges);", StringComparison.Ordinal));
    }

    [Fact]
    public void DragSelectionNoOpsUnchangedTargetsBeforeRefreshingUiState()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var extendSelection = selectionSource[
            selectionSource.IndexOf("private void ExtendSelection", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void AddOrMoveAdditionalSelection", StringComparison.Ordinal)];
        var addSelection = selectionSource[
            selectionSource.IndexOf("private void AddOrMoveAdditionalSelection", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void RefreshToolbarAfterDragSelectionChange", StringComparison.Ordinal)];

        extendSelection.Should().Contain("if (IsSelectionExtensionUnchanged(anchor, to))");
        addSelection.Should().Contain("if (IsAdditionalSelectionExtensionUnchanged(target, extendSelection))");
        extendSelection.IndexOf("if (IsSelectionExtensionUnchanged(anchor, to))", StringComparison.Ordinal)
            .Should()
            .BeLessThan(extendSelection.IndexOf("ClearSelectionTransientOverlays();", StringComparison.Ordinal));
        addSelection.IndexOf("if (IsAdditionalSelectionExtensionUnchanged(target, extendSelection))", StringComparison.Ordinal)
            .Should()
            .BeLessThan(addSelection.IndexOf("ClearSelectionTransientOverlays();", StringComparison.Ordinal));
    }

    [Fact]
    public void DragMouseMoveClearsStaleCommentPreviewWhenPointerLeavesCells()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var mouseMove = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseMove", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void RequestSelectionDragAutoScroll", StringComparison.Ordinal)];

        mouseMove.Should().Contain("if (!hitAddr.HasValue)");
        mouseMove.Should().Contain("ClearCommentPreview();");
        mouseMove.IndexOf("RequestSelectionDragAutoScroll(pos);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseMove.LastIndexOf("if (!hitAddr.HasValue)", StringComparison.Ordinal));
        mouseMove.LastIndexOf("if (!hitAddr.HasValue)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseMove.LastIndexOf("if (_selectionAnchor is not { } anchor) return;", StringComparison.Ordinal));
    }

    [Fact]
    public void FormulaRangeMouseSelectionClearsTransientCellUiBeforeReplacingSelection()
    {
        var formulaReferenceSource = DialogSourceTestSupport.ReadHostSources("MainWindow.FormulaReferenceEditing.cs");

        var formulaRangeSelection = formulaReferenceSource[
            formulaReferenceSource.IndexOf("private bool TryApplyFormulaRangeSelection", StringComparison.Ordinal)..
            formulaReferenceSource.IndexOf("private IReadOnlyList<FormulaReferenceHighlight>", StringComparison.Ordinal)];

        formulaRangeSelection.Should().Contain("HideValidationDropdown();");
        formulaRangeSelection.Should().Contain("ClearCommentPreview();");
        formulaRangeSelection.Should().Contain("SheetGrid.SelectedRange = range;");
        formulaRangeSelection.IndexOf("HideValidationDropdown();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(formulaRangeSelection.IndexOf("SheetGrid.SelectedRange = range;", StringComparison.Ordinal));
        formulaRangeSelection.IndexOf("ClearCommentPreview();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(formulaRangeSelection.IndexOf("SheetGrid.SelectedRange = range;", StringComparison.Ordinal));
    }
}
