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
        mouseMove.Should().Contain("RequestSelectionDragAutoScroll(pos);");

        var helper = source[helperStart..previewStart];
        helper.Should().Contain("Freexcel.App.UI.GridView.CalculateAutofillEdgeScrollIntent");
        helper.Should().Contain("SheetGrid.ActualRowHeaderWidth");
        helper.Should().Contain("SheetGrid.EffectiveColHeaderHeight");
        helper.Should().Contain("if (request.HasAnyDirection)");
        helper.Should().Contain("OnAutofillEdgeScrollRequested(request);");
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
}
