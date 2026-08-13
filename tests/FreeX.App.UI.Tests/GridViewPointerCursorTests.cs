using FluentAssertions;

namespace FreeX.App.UI.Tests;

public sealed class GridViewPointerCursorTests
{
    [Fact]
    public void MouseMoveUsesObjectDragCursorOverSelectedObject()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var hoverCursorBlock = source[
            source.IndexOf("var selectedObjectDragKind = ObjectDragKind.None;", StringComparison.Ordinal)..
            source.IndexOf("public static GridAutoScrollRequest", StringComparison.Ordinal)];

        hoverCursorBlock.Should().Contain("var selectedObjectRect = GetSelectedObjectRect();");
        hoverCursorBlock.Should().Contain("selectedObjectDragKind = HitTestObjectHandle(pos, selectedObjectRect);");
        hoverCursorBlock.Should().Contain("if (selectedObjectDragKind != ObjectDragKind.None)");
        hoverCursorBlock.Should().Contain("Cursor = ObjectDragCursor(selectedObjectDragKind);");
        hoverCursorBlock.IndexOf("if (selectedObjectDragKind != ObjectDragKind.None)", StringComparison.Ordinal)
            .Should().BeLessThan(hoverCursorBlock.IndexOf("var (target, _, _, _) = HitTestResize(pos);", StringComparison.Ordinal));
    }

    [Fact]
    public void MouseMoveUsesMoveCursorOverUnselectedObjectBody()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var hoverCursorBlock = source[
            source.IndexOf("var selectedObjectDragKind = ObjectDragKind.None;", StringComparison.Ordinal)..
            source.IndexOf("public static GridAutoScrollRequest", StringComparison.Ordinal)];

        hoverCursorBlock.Should().Contain("var hoveringObjectBody = selectedObjectDragKind == ObjectDragKind.None");
        hoverCursorBlock.Should().Contain("var hitObject = HitTestDrawingObject(pos);");
        hoverCursorBlock.Should().Contain("hitObject.Id != Guid.Empty");
        hoverCursorBlock.Should().NotContain("hitObject.Kind != ObjectKind.Chart");
        hoverCursorBlock.Should().Contain("if (hoveringObjectBody)");
        hoverCursorBlock.Should().Contain("Cursor = Cursors.SizeAll;");
        hoverCursorBlock.IndexOf("if (hoveringObjectBody)", StringComparison.Ordinal)
            .Should().BeLessThan(hoverCursorBlock.IndexOf("var (target, _, _, _) = HitTestResize(pos);", StringComparison.Ordinal));
    }

    [Fact]
    public void MouseMoveUsesObjectHandlesForDefaultSelectedPictureSurface()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var hoverCursorBlock = source[
            source.IndexOf("var selectedObjectDragKind = ObjectDragKind.None;", StringComparison.Ordinal)..
            source.IndexOf("public static GridAutoScrollRequest", StringComparison.Ordinal)];

        hoverCursorBlock.Should().Contain("selectedObjectDragKind = HitTestObjectHandle(pos, selectedObjectRect);");
        hoverCursorBlock.Should().Contain("Cursor = ObjectDragCursor(selectedObjectDragKind);");
        hoverCursorBlock.Should().Contain("IsSelectedPictureCropModeActive()");
        hoverCursorBlock.Should().Contain("HitTestPictureCropHandle(pos, selectedObjectRect)");
        hoverCursorBlock.Should().Contain("PictureCropCursor(selectedPictureCropHandle)");
    }

    [Fact]
    public void RightClickObjectRoutesContextMenuToObjectAnchor()
    {
        var inputSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var objectDragSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.ObjectDrag.cs");
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
        var inputSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
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
        var inputSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
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
        var inputSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var selectedObjectDragBlock = inputSource[
            inputSource.IndexOf("// Check if clicking on an already-selected object's handles", StringComparison.Ordinal)..
            inputSource.IndexOf("// Check if clicking on a new drawing object", StringComparison.Ordinal)];

        selectedObjectDragBlock.Should().Contain("_selectedObjectId = SelectedObjectId;");
        selectedObjectDragBlock.Should().Contain("_selectedObjectKind = SelectedObjectKind;");
        selectedObjectDragBlock.IndexOf("_selectedObjectId = SelectedObjectId;", StringComparison.Ordinal)
            .Should().BeLessThan(selectedObjectDragBlock.IndexOf("_objectDragKind = dragKind;", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectedPictureMouseDownUsesObjectDragHandlesForDefaultSurface()
    {
        var inputSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var objectDragSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.ObjectDrag.cs");
        var mouseDownBlock = inputSource[
            inputSource.IndexOf("// Check if clicking on an already-selected object's handles", StringComparison.Ordinal)..
            inputSource.IndexOf("// Check if clicking on a new drawing object", StringComparison.Ordinal)];

        objectDragSource.Should().Contain("GridPictureCropPlanner.HitTestHandle(localPos, objRect)");
        mouseDownBlock.Should().Contain("IsSelectedPictureCropModeActive()");
        mouseDownBlock.Should().Contain("var cropHandle = HitTestPictureCropHandle(pos, selRect);");
        mouseDownBlock.Should().Contain("_pictureCropDragHandle = cropHandle;");
        mouseDownBlock.Should().Contain("dragKind = HitTestObjectHandle(pos, selRect);");
        mouseDownBlock.Should().Contain("_objectDragKind = dragKind;");
        mouseDownBlock.Should().Contain("CaptureMouse();");
    }

    [Fact]
    public void LeftMouseDownIgnoresReentrantClicksWhileCapturedDragIsActive()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var mouseDownBlock = source[
            source.IndexOf("protected override void OnMouseLeftButtonDown", StringComparison.Ordinal)..
            source.IndexOf("protected override void OnMouseRightButtonDown", StringComparison.Ordinal)];

        mouseDownBlock.Should().Contain("if (HasActiveCapturedGridDrag())");
        mouseDownBlock.Should().Contain("e.Handled = true;");
        mouseDownBlock.IndexOf("if (HasActiveCapturedGridDrag())", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseDownBlock.IndexOf("var pos = e.GetPosition(this);", StringComparison.Ordinal));
        mouseDownBlock.IndexOf("e.Handled = true;", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseDownBlock.IndexOf("HitTestDrawingObject(pos)", StringComparison.Ordinal));
    }

    [Fact]
    public void CapturedGridDragPredicateCoversAllMouseDragStates()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var helperBlock = source[
            source.IndexOf("private bool HasActiveCapturedGridDrag", StringComparison.Ordinal)..
            source.IndexOf("protected override void OnMouseLeftButtonDown", StringComparison.Ordinal)];

        helperBlock.Should().Contain("_pictureCropDragHandle != PictureCropHandle.None");
        helperBlock.Should().Contain("_objectDragKind != ObjectDragKind.None");
        helperBlock.Should().Contain("_marginDragEdge.HasValue");
        helperBlock.Should().Contain("_splitDividerDragHandle != SplitDividerHandle.None");
        helperBlock.Should().Contain("_splitPaneScrollbarDragging");
        helperBlock.Should().Contain("_autofillDragging");
        helperBlock.Should().Contain("_resizeTarget != ResizeTarget.None");
    }

    [Fact]
    public void MouseMoveCancelsCapturedGridDragWhenLeftButtonIsReleased()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var mouseMoveBlock = source[
            source.IndexOf("protected override void OnMouseMove", StringComparison.Ordinal)..
            source.IndexOf("public static GridAutoScrollRequest", StringComparison.Ordinal)];

        mouseMoveBlock.Should().Contain("if (HasActiveCapturedGridDrag() && e.LeftButton != MouseButtonState.Pressed)");
        mouseMoveBlock.Should().Contain("CancelActiveCapturedGridDrag();");
        mouseMoveBlock.Should().Contain("e.Handled = true;");
        mouseMoveBlock.IndexOf("CancelActiveCapturedGridDrag();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseMoveBlock.IndexOf("var pos = e.GetPosition(this);", StringComparison.Ordinal));
    }

    [Fact]
    public void SplitPaneScrollbarDragPreservesOrientationCursor()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
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
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var mouseDownBlock = source[
            source.IndexOf("if (HitTestSplitPaneScrollbar(chrome, pos) is { } scrollbarHit)", StringComparison.Ordinal)..
            source.IndexOf("if (Viewport is not null && HitTestSplitDividerHandle", StringComparison.Ordinal)];

        mouseDownBlock.Should().Contain("_splitPaneScrollbarDragging = scrollbarHit.Part == SplitPaneScrollbarPart.Thumb");
        mouseDownBlock.Should().Contain("if (!_splitPaneScrollbarDragging)");
        mouseDownBlock.Should().Contain("_splitPaneScrollbarDragSource = null;");
        mouseDownBlock.Should().Contain("_splitPaneScrollbarDragPointerOffset = 0;");
        mouseDownBlock.IndexOf("if (!_splitPaneScrollbarDragging)", StringComparison.Ordinal)
            .Should().BeLessThan(mouseDownBlock.IndexOf("CalculateSplitPaneScrollbarInteractionTarget", StringComparison.Ordinal));
        mouseDownBlock.Should().Contain("CalculateSplitPaneScrollbarInteractionTarget(Viewport, chrome, scrollbarHit, pos)");
        mouseDownBlock.Should().NotContain("CalculateSplitPaneScrollbarInteractionTarget(Viewport, chrome, pos)");
    }

    [Fact]
    public void SplitPaneScrollbarMouseUpPreservesThumbDragOffset()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var mouseUpStart = source.IndexOf("protected override void OnMouseLeftButtonUp", StringComparison.Ordinal);
        var mouseUpBlock = source[
            source.IndexOf("if (_splitPaneScrollbarDragging)", mouseUpStart, StringComparison.Ordinal)..
            source.IndexOf("if (_autofillDragging)", mouseUpStart, StringComparison.Ordinal)];

        mouseUpBlock.Should().Contain("_splitPaneScrollbarDragSource is { } dragSource");
        mouseUpBlock.Should().Contain("CalculateSplitPaneScrollbarThumbDragTarget(");
        mouseUpBlock.Should().Contain("_splitPaneScrollbarDragPointerOffset");
        mouseUpBlock.Should().NotContain("CalculateSplitPaneScrollbarScrollTarget(chrome, pos)");
    }

    [Fact]
    public void ObjectMoveMouseUpSnapsAnchorFromPreviewTopLeft()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var mouseUpStart = source.IndexOf("protected override void OnMouseLeftButtonUp", StringComparison.Ordinal);
        var objectMoveBlock = source[
            source.IndexOf("if (_objectDragKind != ObjectDragKind.None)", mouseUpStart, StringComparison.Ordinal)..
            source.IndexOf("if (_marginDragEdge.HasValue)", mouseUpStart, StringComparison.Ordinal)];

        objectMoveBlock.Should().Contain("HitTestAnchorCell(new Point(currentRect.Left, currentRect.Top))");
        objectMoveBlock.Should().NotContain("HitTestAnchorCell(pos)");
        objectMoveBlock.Should().Contain("GridObjectDragPlanner.PlanCommit(");
        objectMoveBlock.Should().Contain("case ObjectDragCommitKind.Move:");
        objectMoveBlock.Should().Contain("ObjectMoved?.Invoke(id, kind, plan.Anchor!.Value);");
    }

    [Fact]
    public void ObjectRotationDragUpdatesPreviewAndCommitsPreviewAngle()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var rotationMouseMoveBlock = source[
            source.IndexOf("if (_objectDragKind == ObjectDragKind.Rotate)", StringComparison.Ordinal)..
            source.IndexOf("if (_objectDragKind != ObjectDragKind.None)", StringComparison.Ordinal)];
        var mouseUpStart = source.IndexOf("protected override void OnMouseLeftButtonUp", StringComparison.Ordinal);
        var objectMouseUpBlock = source[
            source.IndexOf("if (_objectDragKind != ObjectDragKind.None)", mouseUpStart, StringComparison.Ordinal)..
            source.IndexOf("if (_marginDragEdge.HasValue)", mouseUpStart, StringComparison.Ordinal)];

        rotationMouseMoveBlock.Should().Contain("_objectDragStartRect.Left + _objectDragStartRect.Width / 2");
        rotationMouseMoveBlock.Should().Contain("_objectDragStartRect.Top + _objectDragStartRect.Height / 2");
        rotationMouseMoveBlock.Should().Contain("_objectRotationPreviewDegrees = GridObjectDragPlanner.CalculateRotationDegrees(center, pos);");
        rotationMouseMoveBlock.Should().Contain("Cursor = ObjectDragCursor(_objectDragKind);");
        rotationMouseMoveBlock.Should().Contain("InvalidateVisual();");
        objectMouseUpBlock.Should().Contain("var rotationDegrees = _objectRotationPreviewDegrees;");
        objectMouseUpBlock.Should().Contain("GridObjectDragPlanner.PlanCommit(");
        objectMouseUpBlock.Should().Contain("case ObjectDragCommitKind.Rotate:");
        objectMouseUpBlock.Should().Contain("ObjectRotated?.Invoke(id, kind, plan.RotationDegrees);");
        objectMouseUpBlock.IndexOf("var rotationDegrees = _objectRotationPreviewDegrees;", StringComparison.Ordinal)
            .Should().BeLessThan(objectMouseUpBlock.IndexOf("_objectRotationPreviewDegrees = 0;", StringComparison.Ordinal));
        objectMouseUpBlock.IndexOf("ObjectRotated?.Invoke(id, kind, plan.RotationDegrees);", StringComparison.Ordinal)
            .Should().BeLessThan(objectMouseUpBlock.IndexOf("InvalidateVisual();", StringComparison.Ordinal));
    }

    [Fact]
    public void ChartObjectDragMouseUpCommitsBoundsInsteadOfDrawingAnchorEvents()
    {
        var inputSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var eventsSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Events.cs");
        var mouseUpStart = inputSource.IndexOf("protected override void OnMouseLeftButtonUp", StringComparison.Ordinal);
        var objectMouseUpBlock = inputSource[
            inputSource.IndexOf("if (_objectDragKind != ObjectDragKind.None)", mouseUpStart, StringComparison.Ordinal)..
            inputSource.IndexOf("if (_marginDragEdge.HasValue)", mouseUpStart, StringComparison.Ordinal)];

        eventsSource.Should().Contain("ChartBoundsChanged");
        objectMouseUpBlock.Should().Contain("if (kind == ObjectKind.Chart)");
        objectMouseUpBlock.Should().Contain("CommitChartObjectBoundsChange(id, startRect, currentRect);");
        objectMouseUpBlock.Should().Contain("ObjectMoved?.Invoke(id, kind, plan.Anchor!.Value);");
        objectMouseUpBlock.IndexOf("if (kind == ObjectKind.Chart)", StringComparison.Ordinal)
            .Should().BeLessThan(objectMouseUpBlock.IndexOf("ObjectMoved?.Invoke(id, kind, plan.Anchor!.Value);", StringComparison.Ordinal));
    }

    [Fact]
    public void ObjectDragMouseUpClearsCursorAndCaptureAfterCommit()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var mouseUpStart = source.IndexOf("protected override void OnMouseLeftButtonUp", StringComparison.Ordinal);
        var objectMouseUpBlock = source[
            source.IndexOf("if (_objectDragKind != ObjectDragKind.None)", mouseUpStart, StringComparison.Ordinal)..
            source.IndexOf("if (_marginDragEdge.HasValue)", mouseUpStart, StringComparison.Ordinal)];

        objectMouseUpBlock.Should().Contain("_objectDragKind = ObjectDragKind.None;");
        objectMouseUpBlock.Should().Contain("_objectDragCurrentRect = Rect.Empty;");
        objectMouseUpBlock.Should().Contain("Cursor = null;");
        objectMouseUpBlock.Should().Contain("ReleaseMouseCapture();");
        objectMouseUpBlock.Should().Contain("e.Handled = true;");
        objectMouseUpBlock.IndexOf("Cursor = null;", StringComparison.Ordinal)
            .Should().BeLessThan(objectMouseUpBlock.IndexOf("ReleaseMouseCapture();", StringComparison.Ordinal));
    }

    [Fact]
    public void SplitPaneDividerMouseDownCapturesDragBeforeAutofillAndResize()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
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
    public void SplitPaneDividerHoverCursorTakesPriorityBeforeResizeHitTesting()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var hoverCursorBlock = source[
            source.IndexOf("var splitHandle = Viewport is null", StringComparison.Ordinal)..
            source.IndexOf("public static GridAutoScrollRequest", StringComparison.Ordinal)];

        hoverCursorBlock.Should().Contain("HitTestSplitDividerHandle(Viewport, pos, ActualWidth, ActualHeight)");
        hoverCursorBlock.Should().Contain("if (splitHandle != SplitDividerHandle.None)");
        hoverCursorBlock.Should().Contain("Cursor = splitHandle == SplitDividerHandle.Intersection ? Cursors.SizeAll");
        hoverCursorBlock.Should().Contain("splitHandle == SplitDividerHandle.Vertical ? Cursors.SizeWE");
        hoverCursorBlock.Should().Contain(": Cursors.SizeNS;");
        hoverCursorBlock.IndexOf("if (splitHandle != SplitDividerHandle.None)", StringComparison.Ordinal)
            .Should().BeLessThan(hoverCursorBlock.IndexOf("var (target, _, _, _) = HitTestResize(pos);", StringComparison.Ordinal));
    }

    [Fact]
    public void HoverCursorStopsAfterResizeOrSplitPaneScrollbarHit()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var hoverCursorBlock = source[
            source.IndexOf("var (target, _, _, _) = HitTestResize(pos);", StringComparison.Ordinal)..
            source.IndexOf("public static GridAutoScrollRequest", StringComparison.Ordinal)];

        hoverCursorBlock.Should().Contain("if (target == ResizeTarget.Column)");
        hoverCursorBlock.Should().Contain("if (target == ResizeTarget.Row)");
        hoverCursorBlock.Should().Contain("if (splitScrollbarHit?.Orientation == SplitPaneScrollbarOrientation.Horizontal)");
        hoverCursorBlock.Should().Contain("if (splitScrollbarHit?.Orientation == SplitPaneScrollbarOrientation.Vertical)");
        hoverCursorBlock.IndexOf("if (target == ResizeTarget.Column)", StringComparison.Ordinal)
            .Should().BeLessThan(hoverCursorBlock.IndexOf("HitTestPageMarginGuide(pos)", StringComparison.Ordinal));
        hoverCursorBlock.IndexOf("if (splitScrollbarHit?.Orientation == SplitPaneScrollbarOrientation.Horizontal)", StringComparison.Ordinal)
            .Should().BeLessThan(hoverCursorBlock.IndexOf("HitTestPageMarginGuide(pos)", StringComparison.Ordinal));
    }

    [Fact]
    public void CtrlHoverOverHyperlinkCellUsesHandCursorAfterHigherPriorityGridHits()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var hoverCursorBlock = source[
            source.IndexOf("var (target, _, _, _) = HitTestResize(pos);", StringComparison.Ordinal)..
            source.IndexOf("public static GridAutoScrollRequest", StringComparison.Ordinal)];

        hoverCursorBlock.Should().Contain("IsCtrlModifierDown() && TryHitTestHyperlinkCell(pos, out _) ? Cursors.Hand");
        hoverCursorBlock.Should().Contain("private bool TryHitTestHyperlinkCell(Point pos, out CellAddress address)");
        hoverCursorBlock.Should().Contain("HyperlinkCells.Contains(hitCell)");
        source.Should().Contain("public void RefreshPointerCursor()");
        hoverCursorBlock.IndexOf("if (target == ResizeTarget.Column)", StringComparison.Ordinal)
            .Should().BeLessThan(hoverCursorBlock.IndexOf("TryHitTestHyperlinkCell(pos, out _)", StringComparison.Ordinal));
        hoverCursorBlock.IndexOf("IsOnSelectionMoveBorder(pos) ? Cursors.SizeAll", StringComparison.Ordinal)
            .Should().BeLessThan(hoverCursorBlock.IndexOf("TryHitTestHyperlinkCell(pos, out _)", StringComparison.Ordinal));
    }

    [Fact]
    public void SplitPaneDividerMouseUpRaisesMoveEventAndClearsCaptureState()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
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
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
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
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
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
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
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
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var dragBlock = source[
            source.IndexOf("if (_autofillDragging && Viewport != null && _autofillSourceRange.HasValue)", StringComparison.Ordinal)..
            source.IndexOf("if (_resizeTarget == ResizeTarget.Column)", StringComparison.Ordinal)];

        dragBlock.Should().Contain("Cursor = Cursors.Cross;");
        dragBlock.Should().Contain("e.Handled = true;");
    }

    [Fact]
    public void AutofillDragMouseMoveKeepsCaptureWhenViewportDisappears()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
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
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var resizeBlock = source[
            source.IndexOf("if (_resizeTarget == ResizeTarget.Column)", StringComparison.Ordinal)..
            source.IndexOf("var (target, _, _, _) = HitTestResize(pos);", StringComparison.Ordinal)];

        resizeBlock.Should().Contain("Cursor = Cursors.SizeWE;");
        resizeBlock.Should().Contain("Cursor = Cursors.SizeNS;");
        resizeBlock.Should().Contain("e.Handled = true;");
        resizeBlock.Should().Contain("return;");
    }

    [Fact]
    public void ResizeDragMouseMoveKeepsCaptureWhenMetricDisappears()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var resizeBlock = source[
            source.IndexOf("if (_resizeTarget == ResizeTarget.Column)", StringComparison.Ordinal)..
            source.IndexOf("var (target, _, _, _) = HitTestResize(pos);", StringComparison.Ordinal)];

        resizeBlock.Should().Contain("if (col is null)");
        resizeBlock.Should().Contain("Cursor = Cursors.SizeWE;");
        resizeBlock.Should().Contain("if (row is null)");
        resizeBlock.Should().Contain("Cursor = Cursors.SizeNS;");
        resizeBlock.Should().Contain("e.Handled = true;");
    }

    [Fact]
    public void ResizeDragMouseMoveKeepsCaptureWhenViewportDisappears()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var resizeBlock = source[
            source.IndexOf("if (_resizeTarget == ResizeTarget.Column)", StringComparison.Ordinal)..
            source.IndexOf("var (target, _, _, _) = HitTestResize(pos);", StringComparison.Ordinal)];

        resizeBlock.Should().Contain("if (Viewport is null)");
        resizeBlock.Should().Contain("Cursor = Cursors.SizeWE;");
        resizeBlock.Should().Contain("Cursor = Cursors.SizeNS;");
        resizeBlock.Should().Contain("e.Handled = true;");
        resizeBlock.Should().Contain("return;");
        resizeBlock.Should().Contain("FindColMetric(Viewport.ColMetrics, _resizeIndex)");
        resizeBlock.Should().Contain("FindRowMetric(Viewport.RowMetrics, _resizeIndex)");
        resizeBlock.Should().NotContain("Viewport!.ColMetrics");
        resizeBlock.Should().NotContain("Viewport!.RowMetrics");
    }

    [Fact]
    public void AutofillMouseUpInvalidatesAfterClearingPreview()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var mouseUp = source.IndexOf("protected override void OnMouseLeftButtonUp", StringComparison.Ordinal);
        var releaseStart = source.IndexOf("if (_autofillDragging)", mouseUp, StringComparison.Ordinal);
        var resizeStart = source.IndexOf("if (_resizeTarget != ResizeTarget.None)", releaseStart, StringComparison.Ordinal);
        var releaseBlock = source[releaseStart..resizeStart];

        releaseBlock.Should().Contain("_autofillSourceRange = null;");
        releaseBlock.Should().Contain("_autofillTarget");
        releaseBlock.Should().Contain("= null;");
        releaseBlock.Should().Contain("InvalidateVisual();");
        releaseBlock.IndexOf("InvalidateVisual();", StringComparison.Ordinal)
            .Should().BeGreaterThan(releaseBlock.IndexOf("_autofillTarget", StringComparison.Ordinal));
    }

    [Fact]
    public void MouseLeavePreservesCursorDuringCapturedDrags()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var mouseLeave = source[
            source.IndexOf("protected override void OnMouseLeave", StringComparison.Ordinal)..];

        mouseLeave.Should().Contain("if (!HasActiveCapturedGridDrag())");
        mouseLeave.Should().Contain("Cursor = null;");
    }

    [Fact]
    public void LostMouseCaptureCancelsActiveResize()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var eventsSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Events.cs");
        var cancellationHelper = source[
            source.IndexOf("private void CancelActiveCapturedGridDrag", StringComparison.Ordinal)..
            source.IndexOf("protected override void OnMouseLeftButtonDown", StringComparison.Ordinal)];

        eventsSource.Should().Contain("public event Action? ResizeCanceled;");
        cancellationHelper.Should().Contain("if (_resizeTarget != ResizeTarget.None)");
        cancellationHelper.Should().Contain("_resizeTarget = ResizeTarget.None;");
        cancellationHelper.Should().Contain("ResizeCanceled?.Invoke();");
        cancellationHelper.Should().Contain("InvalidateVisual();");
        source[
            source.IndexOf("protected override void OnLostMouseCapture", StringComparison.Ordinal)..]
            .Should()
            .Contain("CancelActiveCapturedGridDrag();");
    }

    [Fact]
    public void ResizeMouseUpAndLostCaptureClearPreviewState()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var mouseUpStart = source.IndexOf("protected override void OnMouseLeftButtonUp", StringComparison.Ordinal);
        var resizeMouseUp = source[
            source.IndexOf("if (_resizeTarget != ResizeTarget.None)", mouseUpStart, StringComparison.Ordinal)..
            source.IndexOf("protected override void OnMouseLeave", StringComparison.Ordinal)];
        var cancellationHelper = source[
            source.IndexOf("private void CancelActiveCapturedGridDrag", StringComparison.Ordinal)..
            source.IndexOf("protected override void OnMouseLeftButtonDown", StringComparison.Ordinal)];

        resizeMouseUp.Should().Contain("_resizeTarget = ResizeTarget.None;");
        resizeMouseUp.Should().Contain("_resizeIndex = 0;");
        resizeMouseUp.Should().Contain("_resizeDragStart = 0;");
        resizeMouseUp.Should().Contain("_resizeSizeStart = 0;");
        resizeMouseUp.Should().Contain("_resizeLinePos = 0;");

        cancellationHelper.Should().Contain("_resizeTarget = ResizeTarget.None;");
        cancellationHelper.Should().Contain("_resizeIndex = 0;");
        cancellationHelper.Should().Contain("_resizeDragStart = 0;");
        cancellationHelper.Should().Contain("_resizeSizeStart = 0;");
        cancellationHelper.Should().Contain("_resizeLinePos = 0;");
        cancellationHelper.IndexOf("_resizeLinePos = 0;", StringComparison.Ordinal)
            .Should()
            .BeLessThan(cancellationHelper.IndexOf("ResizeCanceled?.Invoke();", StringComparison.Ordinal));
    }

    [Fact]
    public void LostMouseCaptureClearsCapturedPointerDragStates()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var cancellationHelper = source[
            source.IndexOf("private void CancelActiveCapturedGridDrag", StringComparison.Ordinal)..
            source.IndexOf("protected override void OnMouseLeftButtonDown", StringComparison.Ordinal)];

        cancellationHelper.Should().Contain("if (_pictureCropDragHandle != PictureCropHandle.None)");
        cancellationHelper.Should().Contain("_pictureCropDragHandle = PictureCropHandle.None;");
        cancellationHelper.Should().Contain("_pictureCropDragId = Guid.Empty;");
        cancellationHelper.Should().Contain("if (_objectDragKind != ObjectDragKind.None)");
        cancellationHelper.Should().Contain("_objectDragKind = ObjectDragKind.None;");
        cancellationHelper.Should().Contain("_objectDragCurrentRect = Rect.Empty;");
        cancellationHelper.Should().Contain("if (_marginDragEdge.HasValue)");
        cancellationHelper.Should().Contain("_marginDragEdge = null;");
        cancellationHelper.Should().Contain("if (_splitDividerDragHandle != SplitDividerHandle.None)");
        cancellationHelper.Should().Contain("_splitDividerDragHandle = SplitDividerHandle.None;");
        cancellationHelper.Should().Contain("if (_splitPaneScrollbarDragging)");
        cancellationHelper.Should().Contain("_splitPaneScrollbarDragging = false;");
        cancellationHelper.Should().Contain("_splitPaneScrollbarDragSource = null;");
        cancellationHelper.Should().Contain("_splitPaneScrollbarDragPointerOffset = 0;");
        cancellationHelper.Should().Contain("if (_autofillDragging)");
        cancellationHelper.Should().Contain("_autofillDragging = false;");
        cancellationHelper.Should().Contain("_autofillSourceRange = null;");
        cancellationHelper.Should().Contain("_autofillTarget = null;");
        cancellationHelper.Should().Contain("Cursor = null;");
    }

}
