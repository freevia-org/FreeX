using FluentAssertions;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowMouseSelectionSourceTests
{
    [Fact]
    public void MouseDownUpdatesActiveSplitPaneRegionOnlyAfterCellHit()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.Selection.cs"));

        var mouseDown = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseDown", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void MainWindow_TextInput", StringComparison.Ordinal)];

        mouseDown.Should().Contain("var hitAddress = FreeX.App.UI.GridView.HitTestViewportCell(viewport, _currentSheetId, pos);");
        mouseDown.Should().Contain("if (hitAddress is { } newAddr)");
        mouseDown.Should().Contain("_activeSplitPaneRegion = FreeX.App.UI.GridView.HitTestSplitPaneRegion(viewport, pos);");
        mouseDown.IndexOf("if (hitAddress is { } newAddr)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseDown.IndexOf("_activeSplitPaneRegion = FreeX.App.UI.GridView.HitTestSplitPaneRegion(viewport, pos);", StringComparison.Ordinal));
    }

    [Fact]
    public void DragSelectionDefersStatusRefreshUntilMouseUp()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.Selection.cs"));
        var windowSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.xaml.cs"));

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

    [Fact]
    public void AdditionalDragSelectionDefersToolbarRefreshUntilMouseUp()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.Selection.cs"));
        var windowSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.xaml.cs"));

        var addSelection = selectionSource[
            selectionSource.IndexOf("private void AddOrMoveAdditionalSelection", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void RefreshToolbarAfterDragSelectionChange", StringComparison.Ordinal)];
        var refreshHelper = selectionSource[
            selectionSource.IndexOf("private void RefreshToolbarAfterDragSelectionChange", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void RefreshStatusBarAfterDragSelectionChange", StringComparison.Ordinal)];
        var mouseUp = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseUp", StringComparison.Ordinal)..];

        windowSource.Should().Contain("private bool _dragSelectToolbarRefreshPending;");
        addSelection.Should().Contain("RefreshToolbarAfterDragSelectionChange();");
        addSelection.Should().NotContain("RefreshToolbar();");
        refreshHelper.Should().Contain("if (_dragSelectActive)");
        refreshHelper.Should().Contain("_dragSelectToolbarRefreshPending = true;");
        refreshHelper.Should().Contain("CompleteDragSelectionToolbarRefresh");
        refreshHelper.Should().Contain("RefreshToolbarAfterSelectionChange();");
        mouseUp.Should().Contain("CompleteDragSelectionToolbarRefresh();");
    }

    [Fact]
    public void LostMouseCaptureClearsDragSelectionStateAndCompletesDeferredRefreshes()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.Selection.cs"));
        var windowSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.xaml.cs"));

        var lostCapture = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_LostMouseCapture", StringComparison.Ordinal)..
            selectionSource.IndexOf("private static IReadOnlyList<GridRange> CreateAdditionalSelectionRanges", StringComparison.Ordinal)];

        windowSource.Should().Contain("SheetGrid.LostMouseCapture += SheetGrid_LostMouseCapture;");
        lostCapture.Should().Contain("if (!_dragSelectActive &&");
        lostCapture.Should().Contain("!_formatPainterTargetSelectionActive &&");
        lostCapture.Should().Contain("!_dragSelectAddsAdditionalRange &&");
        lostCapture.Should().Contain("!_dragHeaderSelectionTarget.HasValue)");
        lostCapture.Should().Contain("_formatPainterTargetSelectionActive = false;");
        lostCapture.Should().Contain("_dragSelectActive = false;");
        lostCapture.Should().Contain("_dragSelectAddsAdditionalRange = false;");
        lostCapture.Should().Contain("_dragHeaderSelectionTarget = null;");
        lostCapture.Should().Contain("_dragHeaderSelectionAnchor = 0;");
        lostCapture.Should().Contain("CompleteDragSelectionToolbarRefresh();");
        lostCapture.Should().Contain("CompleteDragSelectionStatusRefresh();");
    }

    [Fact]
    public void AdditionalDragSelectionReusesOwnedRangesInsteadOfCloningList()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.Selection.cs"));

        var addSelection = selectionSource[
            selectionSource.IndexOf("private void AddOrMoveAdditionalSelection", StringComparison.Ordinal)..
            selectionSource.IndexOf("private bool IsSelectionExtensionUnchanged", StringComparison.Ordinal)];

        addSelection.Should().Contain("CreateAdditionalSelectionRanges(");
        addSelection.Should().NotContain(".ToList()");
        selectionSource.Should().Contain("private sealed class MutableSelectionRanges");
    }

    [Fact]
    public void SelectionHotPathsUpdateTextBoxesWithoutBuildingUndoHistory()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.Selection.cs"));

        var setActiveCell = selectionSource[
            selectionSource.IndexOf("private void SetActiveCell", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void EnsureActiveCellSelection", StringComparison.Ordinal)];
        var extendSelection = selectionSource[
            selectionSource.IndexOf("private void ExtendSelection", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void AddOrMoveAdditionalSelection", StringComparison.Ordinal)];
        var addSelection = selectionSource[
            selectionSource.IndexOf("private void AddOrMoveAdditionalSelection", StringComparison.Ordinal)..
            selectionSource.IndexOf("private bool IsSelectionExtensionUnchanged", StringComparison.Ordinal)];
        var helper = selectionSource[
            selectionSource.IndexOf("private void SetCellAddressBoxSelectionText", StringComparison.Ordinal)..
            selectionSource.IndexOf("private CellAddress? HitTestCell", StringComparison.Ordinal)];

        setActiveCell.Should().Contain("SetCellAddressBoxSelectionText(FormatCellReference(addr));");
        setActiveCell.Should().Contain("SetFormulaBarSelectionText(FormatFormulaBarText(cell, addr));");
        extendSelection.Should().Contain("SetCellAddressBoxSelectionText(FormatRangeReference(anchor, to));");
        addSelection.Should().Contain("SetCellAddressBoxSelectionText(FormatRangeReference(activeRange.Start, activeRange.End));");
        addSelection.Should().Contain("SetFormulaBarSelectionText(FormatFormulaBarText(sheet?.GetCell(target), target));");
        helper.Should().Contain("CellAddressBox.IsKeyboardFocusWithin");
        helper.Should().Contain("CellAddressBox.IsUndoEnabled = false;");
        helper.Should().Contain("CellAddressBox.IsUndoEnabled = true;");
        helper.Should().Contain("FormulaBar.IsKeyboardFocusWithin");
        helper.Should().Contain("FormulaBar.IsUndoEnabled = false;");
        helper.Should().Contain("FormulaBar.IsUndoEnabled = true;");
    }
}
