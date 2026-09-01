using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowMouseSelectionSourceTests
{
    [Fact]
    public void MouseContextMenuHidesValidationDropdownAfterSelectionAdjustment()
    {
        var contextMenuSource = DialogSourceTestSupport.ReadHostSources("MainWindow.WorksheetContextMenu.cs");

        var contextMenuHandler = contextMenuSource[
            contextMenuSource.IndexOf("internal void OnGridContextMenuRequested", StringComparison.Ordinal)..
            contextMenuSource.IndexOf("internal void OnGridHeaderContextMenuRequested", StringComparison.Ordinal)];

        contextMenuHandler.Should().Contain("SetActiveCell(actualAddr);");
        contextMenuHandler.Should().Contain("HideValidationDropdown();");
        contextMenuHandler.Should().Contain("WorksheetContextMenuPlanner.BuildCommands(targetKind, state)");
        contextMenuHandler.IndexOf("HideValidationDropdown();", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(contextMenuHandler.IndexOf("SetActiveCell(actualAddr);", StringComparison.Ordinal));
        contextMenuHandler.IndexOf("HideValidationDropdown();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(contextMenuHandler.IndexOf("WorksheetContextMenuPlanner.BuildCommands(targetKind, state)", StringComparison.Ordinal));
    }

    [Fact]
    public void MouseUpSelectionIgnoresNonLeftButtonsBeforeCompletingDrag()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

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
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var mouseUp = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseUp", StringComparison.Ordinal)..];

        var completedDrag = mouseUp[
            mouseUp.IndexOf("if (!_dragSelectActive) return;", StringComparison.Ordinal)..];

        completedDrag.Should().Contain("SheetGrid.ReleaseMouseCapture();");
        completedDrag.Should().Contain("CompleteDragSelectionToolbarRefresh();");
        completedDrag.Should().Contain("CompleteDragSelectionStatusRefresh();");
        completedDrag.Should().Contain("if (hitAddr.HasValue)");
        completedDrag.Should().Contain("UpdateCommentPreview(hitAddr.Value);");
        completedDrag.Should().Contain("ClearCommentPreview();");
        completedDrag.Should().Contain("GetFormulaRangeEntryEditor()?.Focus();");
        completedDrag.Should().Contain("e.Handled = true;");
        completedDrag.IndexOf("CompleteDragSelectionToolbarRefresh();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(completedDrag.IndexOf("CompleteDragSelectionStatusRefresh();", StringComparison.Ordinal));
        completedDrag.IndexOf("CompleteDragSelectionStatusRefresh();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(completedDrag.IndexOf("UpdateCommentPreview(hitAddr.Value);", StringComparison.Ordinal));
        completedDrag.LastIndexOf("e.Handled = true;", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(completedDrag.IndexOf("GetFormulaRangeEntryEditor()?.Focus();", StringComparison.Ordinal));
    }

    [Fact]
    public void FormatPainterMouseUpRefreshesCommentPreviewAfterApplyingSelection()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var mouseUp = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseUp", StringComparison.Ordinal)..];
        var formatPainterBlock = mouseUp[
            mouseUp.IndexOf("if (_formatPainterTargetSelectionActive)", StringComparison.Ordinal)..
            mouseUp.IndexOf("if (!_dragSelectActive) return;", StringComparison.Ordinal)];

        formatPainterBlock.Should().Contain("TryApplyFormatPainter(selectedRange);");
        formatPainterBlock.Should().Contain("if (hitAddr.HasValue)");
        formatPainterBlock.Should().Contain("UpdateCommentPreview(hitAddr.Value);");
        formatPainterBlock.Should().Contain("ClearCommentPreview();");
        formatPainterBlock.IndexOf("TryApplyFormatPainter(selectedRange);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(formatPainterBlock.IndexOf("UpdateCommentPreview(hitAddr.Value);", StringComparison.Ordinal));
    }

    [Fact]
    public void FormatPainterMouseDownImmediateApplyRefreshesCommentPreviewBeforeReturning()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var mouseDown = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseDown", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void MainWindow_TextInput", StringComparison.Ordinal)];
        var formatPainterImmediateApply = mouseDown[
            mouseDown.IndexOf("if (_formatPainterActive)", StringComparison.Ordinal)..
            mouseDown.IndexOf("SetActiveCell(newAddr);", StringComparison.Ordinal)];

        formatPainterImmediateApply.Should().Contain("TryApplyFormatPainter(selectedRange);");
        formatPainterImmediateApply.Should().Contain("UpdateCommentPreview(newAddr);");
        formatPainterImmediateApply.Should().Contain("e.Handled = true;");
        formatPainterImmediateApply.IndexOf("TryApplyFormatPainter(selectedRange);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(formatPainterImmediateApply.IndexOf("UpdateCommentPreview(newAddr);", StringComparison.Ordinal));
        formatPainterImmediateApply.IndexOf("UpdateCommentPreview(newAddr);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(formatPainterImmediateApply.IndexOf("e.Handled = true;", StringComparison.Ordinal));
    }
}
