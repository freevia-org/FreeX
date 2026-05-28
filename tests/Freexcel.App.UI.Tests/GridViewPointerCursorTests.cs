using FluentAssertions;
using System.IO;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewPointerCursorTests
{
    [Fact]
    public void MouseMoveUsesObjectDragCursorOverSelectedObject()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var hoverCursorBlock = source[
            source.IndexOf("var (target, _, _) = HitTestResize(pos);", StringComparison.Ordinal)..
            source.IndexOf("public static GridAutoScrollRequest", StringComparison.Ordinal)];

        hoverCursorBlock.Should().Contain("selectedObjectDragKind = HitTestObjectHandle(pos, GetSelectedObjectRect());");
        hoverCursorBlock.Should().Contain("Cursor = selectedObjectDragKind != ObjectDragKind.None ? ObjectDragCursor(selectedObjectDragKind)");
    }

    [Fact]
    public void MouseMoveUsesMoveCursorOverUnselectedObjectBody()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var hoverCursorBlock = source[
            source.IndexOf("var (target, _, _) = HitTestResize(pos);", StringComparison.Ordinal)..
            source.IndexOf("public static GridAutoScrollRequest", StringComparison.Ordinal)];

        hoverCursorBlock.Should().Contain("var hoveringObjectBody = selectedObjectDragKind == ObjectDragKind.None");
        hoverCursorBlock.Should().Contain("HitTestDrawingObject(pos).Id != Guid.Empty");
        hoverCursorBlock.Should().Contain(": hoveringObjectBody ? Cursors.SizeAll");
    }

    [Fact]
    public void RightClickObjectRoutesContextMenuToObjectAnchor()
    {
        var inputSource = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var objectDragSource = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.ObjectDrag.cs"));
        var rightClickBlock = inputSource[
            inputSource.IndexOf("protected override void OnMouseRightButtonDown", StringComparison.Ordinal)..];

        objectDragSource.Should().Contain("Rect Rect, CellAddress Anchor");
        rightClickBlock.Should().Contain("var objectHit = HitTestDrawingObject(pos);");
        rightClickBlock.Should().Contain("SelectedObjectId = objectHit.Id;");
        rightClickBlock.Should().Contain("SelectedObjectKind = objectHit.Kind;");
        rightClickBlock.Should().Contain("InvalidateVisual();");
        rightClickBlock.Should().Contain("ContextMenuRequested?.Invoke(objectHit.Anchor, pos);");
        rightClickBlock.IndexOf("InvalidateVisual();", StringComparison.Ordinal)
            .Should().BeLessThan(rightClickBlock.IndexOf("ContextMenuRequested?.Invoke(objectHit.Anchor, pos);", StringComparison.Ordinal));
    }

    [Fact]
    public void LeftClickObjectInvalidatesSelectionBeforeCapturingDrag()
    {
        var inputSource = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var objectClickBlock = inputSource[
            inputSource.IndexOf("// Check if clicking on a new drawing object", StringComparison.Ordinal)..
            inputSource.IndexOf("// Clicking empty space deselects", StringComparison.Ordinal)];

        objectClickBlock.Should().Contain("SelectedObjectId = hit.Id;");
        objectClickBlock.Should().Contain("SelectedObjectKind = hit.Kind;");
        objectClickBlock.Should().Contain("InvalidateVisual();");
        objectClickBlock.IndexOf("InvalidateVisual();", StringComparison.Ordinal)
            .Should().BeLessThan(objectClickBlock.IndexOf("CaptureMouse();", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectedObjectDragStartInvalidatesPreviewBeforeCapturingMouse()
    {
        var inputSource = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var selectedObjectDragBlock = inputSource[
            inputSource.IndexOf("// Check if clicking on an already-selected object's handles", StringComparison.Ordinal)..
            inputSource.IndexOf("// Check if clicking on a new drawing object", StringComparison.Ordinal)];

        selectedObjectDragBlock.Should().Contain("_objectDragKind = dragKind;");
        selectedObjectDragBlock.Should().Contain("_objectDragCurrentRect = selRect;");
        selectedObjectDragBlock.Should().Contain("InvalidateVisual();");
        selectedObjectDragBlock.IndexOf("InvalidateVisual();", StringComparison.Ordinal)
            .Should().BeLessThan(selectedObjectDragBlock.IndexOf("CaptureMouse();", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectedObjectDragStartRefreshesEventPayloadState()
    {
        var inputSource = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var selectedObjectDragBlock = inputSource[
            inputSource.IndexOf("// Check if clicking on an already-selected object's handles", StringComparison.Ordinal)..
            inputSource.IndexOf("// Check if clicking on a new drawing object", StringComparison.Ordinal)];

        selectedObjectDragBlock.Should().Contain("_selectedObjectId = SelectedObjectId;");
        selectedObjectDragBlock.Should().Contain("_selectedObjectKind = SelectedObjectKind;");
        selectedObjectDragBlock.IndexOf("_selectedObjectId = SelectedObjectId;", StringComparison.Ordinal)
            .Should().BeLessThan(selectedObjectDragBlock.IndexOf("_objectDragKind = dragKind;", StringComparison.Ordinal));
    }

    [Fact]
    public void SplitPaneScrollbarDragPreservesOrientationCursor()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var dragBlock = source[
            source.IndexOf("if (_splitPaneScrollbarDragging)", StringComparison.Ordinal)..
            source.IndexOf("if (_autofillDragging", StringComparison.Ordinal)];

        dragBlock.Should().Contain("_splitPaneScrollbarDragSource?.Orientation == SplitPaneScrollbarOrientation.Horizontal");
        dragBlock.Should().Contain("? Cursors.SizeWE");
        dragBlock.Should().Contain("_splitPaneScrollbarDragSource?.Orientation == SplitPaneScrollbarOrientation.Vertical");
        dragBlock.Should().Contain("? Cursors.SizeNS");
    }

    [Fact]
    public void SplitPaneScrollbarTrackClickClearsDragOnlyState()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var mouseDownBlock = source[
            source.IndexOf("if (HitTestSplitPaneScrollbar(chrome, pos) is { } scrollbarHit)", StringComparison.Ordinal)..
            source.IndexOf("if (Viewport is not null && HitTestSplitDividerHandle", StringComparison.Ordinal)];

        mouseDownBlock.Should().Contain("_splitPaneScrollbarDragging = scrollbarHit.Part == SplitPaneScrollbarPart.Thumb");
        mouseDownBlock.Should().Contain("if (!_splitPaneScrollbarDragging)");
        mouseDownBlock.Should().Contain("_splitPaneScrollbarDragSource = null;");
        mouseDownBlock.Should().Contain("_splitPaneScrollbarDragPointerOffset = 0;");
        mouseDownBlock.IndexOf("if (!_splitPaneScrollbarDragging)", StringComparison.Ordinal)
            .Should().BeLessThan(mouseDownBlock.IndexOf("CalculateSplitPaneScrollbarInteractionTarget", StringComparison.Ordinal));
    }

    [Fact]
    public void SplitPaneScrollbarMouseUpPreservesThumbDragOffset()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var mouseUpStart = source.IndexOf("protected override void OnMouseLeftButtonUp", StringComparison.Ordinal);
        var mouseUpBlock = source[
            source.IndexOf("if (_splitPaneScrollbarDragging)", mouseUpStart, StringComparison.Ordinal)..
            source.IndexOf("if (_autofillDragging)", mouseUpStart, StringComparison.Ordinal)];

        mouseUpBlock.Should().Contain("_splitPaneScrollbarDragSource is not null");
        mouseUpBlock.Should().Contain("CalculateSplitPaneScrollbarThumbDragTarget(");
        mouseUpBlock.Should().Contain("_splitPaneScrollbarDragPointerOffset");
        mouseUpBlock.Should().NotContain("CalculateSplitPaneScrollbarScrollTarget(chrome, pos)");
    }

    [Fact]
    public void SplitPaneDividerMouseDownCapturesDragBeforeAutofillAndResize()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var mouseDownBlock = source[
            source.IndexOf("if (Viewport is not null && HitTestSplitDividerHandle", StringComparison.Ordinal)..
            source.IndexOf("if (SelectedRange.HasValue && IsOnAutofillHandle(pos))", StringComparison.Ordinal)];

        mouseDownBlock.Should().Contain("_splitDividerDragHandle = splitHandle;");
        mouseDownBlock.Should().Contain("CaptureMouse();");
        mouseDownBlock.Should().Contain("e.Handled = true;");
        mouseDownBlock.Should().Contain("splitHandle == SplitDividerHandle.Intersection ? Cursors.SizeAll");
        mouseDownBlock.Should().Contain("splitHandle == SplitDividerHandle.Vertical ? Cursors.SizeWE");
        mouseDownBlock.Should().Contain(": Cursors.SizeNS;");
    }

    [Fact]
    public void SplitPaneDividerMouseUpRaisesMoveEventAndClearsCaptureState()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var mouseUpStart = source.IndexOf("protected override void OnMouseLeftButtonUp", StringComparison.Ordinal);
        var mouseUpBlock = source[
            source.IndexOf("if (_splitDividerDragHandle != SplitDividerHandle.None)", mouseUpStart, StringComparison.Ordinal)..
            source.IndexOf("if (_splitPaneScrollbarDragging)", mouseUpStart, StringComparison.Ordinal)];

        mouseUpBlock.Should().Contain("CalculateSplitDividerDragTarget(Viewport, _splitDividerDragHandle, pos)");
        mouseUpBlock.Should().Contain("SplitDividerMoved?.Invoke(target.Row, target.Column);");
        mouseUpBlock.Should().Contain("_splitDividerDragHandle = SplitDividerHandle.None;");
        mouseUpBlock.Should().Contain("Cursor = null;");
        mouseUpBlock.Should().Contain("ReleaseMouseCapture();");
        mouseUpBlock.Should().Contain("InvalidateVisual();");
        mouseUpBlock.Should().Contain("e.Handled = true;");
    }

    [Fact]
    public void PageMarginGuideMouseDownCapturesDragBeforeSplitPaneAndResize()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var marginGuideStart = source.IndexOf("if (HitTestPageMarginGuide(pos) is { } marginEdge)", StringComparison.Ordinal);
        var mouseDownBlock = source[
            marginGuideStart..
            source.IndexOf("if (Viewport is not null)", marginGuideStart, StringComparison.Ordinal)];

        mouseDownBlock.Should().Contain("_marginDragEdge = marginEdge;");
        mouseDownBlock.Should().Contain("marginEdge is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right");
        mouseDownBlock.Should().Contain("? Cursors.SizeWE");
        mouseDownBlock.Should().Contain(": Cursors.SizeNS;");
        mouseDownBlock.Should().Contain("CaptureMouse();");
        mouseDownBlock.Should().Contain("e.Handled = true;");
    }

    [Fact]
    public void PageMarginGuideMouseMoveUpdatesPreviewMarginsAndKeepsResizeCursor()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var mouseMoveBlock = source[
            source.IndexOf("if (_marginDragEdge.HasValue)", StringComparison.Ordinal)..
            source.IndexOf("if (_splitDividerDragHandle != SplitDividerHandle.None)", StringComparison.Ordinal)];

        mouseMoveBlock.Should().Contain("GetPageMarginsForDraggedGuide(pos)");
        mouseMoveBlock.Should().Contain("PageMargins = margins;");
        mouseMoveBlock.Should().Contain("_marginDragEdge is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right");
        mouseMoveBlock.Should().Contain("? Cursors.SizeWE");
        mouseMoveBlock.Should().Contain(": Cursors.SizeNS;");
        mouseMoveBlock.Should().Contain("InvalidateVisual();");
        mouseMoveBlock.Should().Contain("e.Handled = true;");
    }

    [Fact]
    public void PageMarginGuideMouseUpCommitsMarginsAndClearsCaptureState()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var mouseUpStart = source.IndexOf("protected override void OnMouseLeftButtonUp", StringComparison.Ordinal);
        var mouseUpBlock = source[
            source.IndexOf("if (_marginDragEdge.HasValue)", mouseUpStart, StringComparison.Ordinal)..
            source.IndexOf("if (_splitDividerDragHandle != SplitDividerHandle.None)", mouseUpStart, StringComparison.Ordinal)];

        mouseUpBlock.Should().Contain("GetPageMarginsForDraggedGuide(pos)");
        mouseUpBlock.Should().Contain("PageMargins = margins;");
        mouseUpBlock.Should().Contain("PageMarginsChanged?.Invoke(margins);");
        mouseUpBlock.Should().Contain("_marginDragEdge = null;");
        mouseUpBlock.Should().Contain("Cursor = null;");
        mouseUpBlock.Should().Contain("ReleaseMouseCapture();");
        mouseUpBlock.Should().Contain("InvalidateVisual();");
        mouseUpBlock.Should().Contain("e.Handled = true;");
    }

    [Fact]
    public void AutofillDragMouseMoveKeepsCrossCursorAndHandlesEvent()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var dragBlock = source[
            source.IndexOf("if (_autofillDragging && Viewport != null && _autofillSourceRange.HasValue)", StringComparison.Ordinal)..
            source.IndexOf("if (_resizeTarget == ResizeTarget.Column)", StringComparison.Ordinal)];

        dragBlock.Should().Contain("Cursor = Cursors.Cross;");
        dragBlock.Should().Contain("e.Handled = true;");
    }

    [Fact]
    public void AutofillDragMouseMoveKeepsCaptureWhenViewportDisappears()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var resizeStart = source.IndexOf("if (_resizeTarget == ResizeTarget.Column)", StringComparison.Ordinal);
        var fallbackStart = source.LastIndexOf("if (_autofillDragging)", resizeStart, StringComparison.Ordinal);
        var dragFallback = source[fallbackStart..resizeStart];

        dragFallback.Should().Contain("Cursor = Cursors.Cross;");
        dragFallback.Should().Contain("e.Handled = true;");
        dragFallback.Should().Contain("return;");
    }

    [Fact]
    public void ResizeDragMouseMoveKeepsResizeCursorAndHandlesEvent()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var resizeBlock = source[
            source.IndexOf("if (_resizeTarget == ResizeTarget.Column)", StringComparison.Ordinal)..
            source.IndexOf("var (target, _, _) = HitTestResize(pos);", StringComparison.Ordinal)];

        resizeBlock.Should().Contain("Cursor = Cursors.SizeWE;");
        resizeBlock.Should().Contain("Cursor = Cursors.SizeNS;");
        resizeBlock.Should().Contain("e.Handled = true;");
        resizeBlock.Should().Contain("return;");
    }

    [Fact]
    public void ResizeDragMouseMoveKeepsCaptureWhenMetricDisappears()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var resizeBlock = source[
            source.IndexOf("if (_resizeTarget == ResizeTarget.Column)", StringComparison.Ordinal)..
            source.IndexOf("var (target, _, _) = HitTestResize(pos);", StringComparison.Ordinal)];

        resizeBlock.Should().Contain("if (col is null)");
        resizeBlock.Should().Contain("Cursor = Cursors.SizeWE;");
        resizeBlock.Should().Contain("if (row is null)");
        resizeBlock.Should().Contain("Cursor = Cursors.SizeNS;");
        resizeBlock.Should().Contain("e.Handled = true;");
    }

    [Fact]
    public void AutofillMouseUpInvalidatesAfterClearingPreview()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var releaseStart = source.LastIndexOf("if (_autofillDragging)", StringComparison.Ordinal);
        var resizeStart = source.IndexOf("if (_resizeTarget != ResizeTarget.None)", releaseStart, StringComparison.Ordinal);
        var releaseBlock = source[releaseStart..resizeStart];

        releaseBlock.Should().Contain("_autofillSourceRange = null;");
        releaseBlock.Should().Contain("_autofillTarget      = null;");
        releaseBlock.Should().Contain("InvalidateVisual();");
        releaseBlock.IndexOf("InvalidateVisual();", StringComparison.Ordinal)
            .Should().BeGreaterThan(releaseBlock.IndexOf("_autofillTarget      = null;", StringComparison.Ordinal));
    }

    [Fact]
    public void MouseLeavePreservesCursorDuringCapturedDrags()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var mouseLeave = source[
            source.IndexOf("protected override void OnMouseLeave", StringComparison.Ordinal)..];

        mouseLeave.Should().Contain("_objectDragKind == ObjectDragKind.None");
        mouseLeave.Should().Contain("!_autofillDragging");
        mouseLeave.Should().Contain("_resizeTarget == ResizeTarget.None");
        mouseLeave.Should().Contain("!_marginDragEdge.HasValue");
        mouseLeave.Should().Contain("_splitDividerDragHandle == SplitDividerHandle.None");
        mouseLeave.Should().Contain("!_splitPaneScrollbarDragging");
        mouseLeave.Should().Contain("Cursor = null;");
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
