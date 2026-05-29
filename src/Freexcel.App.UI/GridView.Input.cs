using System.Windows;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public partial class GridView
{
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (_objectDragKind != ObjectDragKind.None)
        {
            _objectDragCurrentRect = GridObjectDragPlanner.CalculateDragRect(
                _objectDragKind,
                _objectDragStartRect,
                _objectDragStartPos,
                pos);
            Cursor = ObjectDragCursor(_objectDragKind);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_marginDragEdge.HasValue)
        {
            if (GetPageMarginsForDraggedGuide(pos) is { } margins)
                PageMargins = margins;
            Cursor = _marginDragEdge is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right
                ? Cursors.SizeWE
                : Cursors.SizeNS;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_splitDividerDragHandle != SplitDividerHandle.None)
        {
            Cursor = _splitDividerDragHandle == SplitDividerHandle.Intersection ? Cursors.SizeAll
                   : _splitDividerDragHandle == SplitDividerHandle.Vertical ? Cursors.SizeWE
                   : Cursors.SizeNS;
            e.Handled = true;
            return;
        }

        if (_splitPaneScrollbarDragging)
        {
            if (Viewport is not null)
            {
                if (_splitPaneScrollbarDragSource is not null &&
                    CalculateSplitPaneScrollbarThumbDragTarget(
                        _splitPaneScrollbarDragSource,
                        pos,
                        _splitPaneScrollbarDragPointerOffset) is { } target)
                    SplitPaneScrollbarScrolled?.Invoke(target);
            }

            Cursor = _splitPaneScrollbarDragSource?.Orientation == SplitPaneScrollbarOrientation.Horizontal
                ? Cursors.SizeWE
                : _splitPaneScrollbarDragSource?.Orientation == SplitPaneScrollbarOrientation.Vertical
                    ? Cursors.SizeNS
                    : null;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_autofillDragging && Viewport != null && _autofillSourceRange.HasValue)
        {
            var scrollRequest = CalculateAutofillEdgeScrollIntent(
                pos.X,
                pos.Y,
                ActualWidth,
                ActualHeight,
                ActualRowHeaderWidth,
                EffectiveColHeaderHeight);
            if (scrollRequest.HasAnyDirection)
                AutofillEdgeScrollRequested?.Invoke(scrollRequest);

            var src = _autofillSourceRange.Value;
            if (GridAutofillPlanner.CalculateDragTarget(
                    Viewport,
                    src,
                    pos,
                    ActualRowHeaderWidth,
                    EffectiveColHeaderHeight) is { } newTarget)
                _autofillTarget = ConstrainAutofillTarget(src, newTarget);

            InvalidateVisual();
            Cursor = Cursors.Cross;
            e.Handled = true;
            return;
        }

        if (_autofillDragging)
        {
            Cursor = Cursors.Cross;
            e.Handled = true;
            return;
        }

        if (_resizeTarget == ResizeTarget.Column)
        {
            var col = FindColMetric(Viewport!.ColMetrics, _resizeIndex);
            if (col is null)
            {
                Cursor = Cursors.SizeWE;
                e.Handled = true;
                return;
            }
            double newWidth = Math.Max(MinCellSize, _resizeSizeStart + (pos.X - _resizeDragStart));
            _resizeLinePos = col.LeftOffset + newWidth + ActualRowHeaderWidth;
            ColumnResizing?.Invoke(_resizeIndex, newWidth);
            Cursor = Cursors.SizeWE;
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        else if (_resizeTarget == ResizeTarget.Row)
        {
            var row = FindRowMetric(Viewport!.RowMetrics, _resizeIndex);
            if (row is null)
            {
                Cursor = Cursors.SizeNS;
                e.Handled = true;
                return;
            }
            double newHeight = Math.Max(MinCellSize, _resizeSizeStart + (pos.Y - _resizeDragStart));
            _resizeLinePos = row.TopOffset + newHeight + EffectiveColHeaderHeight;
            RowResizing?.Invoke(_resizeIndex, newHeight);
            Cursor = Cursors.SizeNS;
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        else
        {
            var (target, _, _) = HitTestResize(pos);
            var selectedObjectDragKind = ObjectDragKind.None;
            if (SelectedObjectId != Guid.Empty && SelectedObjectKind != ObjectKind.None)
                selectedObjectDragKind = HitTestObjectHandle(pos, GetSelectedObjectRect());
            var hoveringObjectBody = selectedObjectDragKind == ObjectDragKind.None &&
                HitTestDrawingObject(pos).Id != Guid.Empty;
            var marginGuide = HitTestPageMarginGuide(pos);
            var splitHandle = Viewport is null ? SplitDividerHandle.None : HitTestSplitDividerHandle(Viewport, pos);
            var splitScrollbarHit = Viewport is null
                ? null
                : HitTestSplitPaneScrollbar(CalculateSplitPaneScrollbarChrome(Viewport, ActualWidth, ActualHeight), pos);
            Cursor = selectedObjectDragKind != ObjectDragKind.None ? ObjectDragCursor(selectedObjectDragKind)
                   : hoveringObjectBody ? Cursors.SizeAll
                   : target == ResizeTarget.Column ? Cursors.SizeWE
                   : target == ResizeTarget.Row    ? Cursors.SizeNS
                   : splitHandle == SplitDividerHandle.Intersection ? Cursors.SizeAll
                   : splitHandle == SplitDividerHandle.Vertical ? Cursors.SizeWE
                   : splitHandle == SplitDividerHandle.Horizontal ? Cursors.SizeNS
                   : splitScrollbarHit?.Orientation == SplitPaneScrollbarOrientation.Horizontal ? Cursors.SizeWE
                   : splitScrollbarHit?.Orientation == SplitPaneScrollbarOrientation.Vertical ? Cursors.SizeNS
                   : marginGuide is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right ? Cursors.SizeWE
                   : marginGuide is WorksheetPageMarginEdge.Top or WorksheetPageMarginEdge.Bottom ? Cursors.SizeNS
                   : IsOnAutofillHandle(pos) ? Cursors.Cross
                   : null;
        }
    }

    public static GridAutoScrollRequest CalculateAutofillEdgeScrollIntent(
        double pointerX,
        double pointerY,
        double width,
        double height,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double edgeThreshold = 24)
        => GridAutofillPlanner.CalculateEdgeScrollIntent(
            pointerX,
            pointerY,
            width,
            height,
            rowHeaderWidth,
            columnHeaderHeight,
            edgeThreshold);

    private bool HasActiveCapturedGridDrag() =>
        _objectDragKind != ObjectDragKind.None ||
        _marginDragEdge.HasValue ||
        _splitDividerDragHandle != SplitDividerHandle.None ||
        _splitPaneScrollbarDragging ||
        _autofillDragging ||
        _resizeTarget != ResizeTarget.None;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (HasActiveCapturedGridDrag())
        {
            e.Handled = true;
            return;
        }

        var pos = e.GetPosition(this);

        // Check if clicking on an already-selected object's handles
        if (SelectedObjectId != Guid.Empty && SelectedObjectKind != ObjectKind.None)
        {
            var selRect = GetSelectedObjectRect();
            var dragKind = HitTestObjectHandle(pos, selRect);
            if (dragKind != ObjectDragKind.None)
            {
                _selectedObjectId = SelectedObjectId;
                _selectedObjectKind = SelectedObjectKind;
                _objectDragKind = dragKind;
                _objectDragStartPos = pos;
                _objectDragStartRect = selRect;
                _objectDragCurrentRect = selRect;
                _objectDragStartAnchor = GetSelectedObjectAnchor() ?? HitTestAnchorCell(pos) ?? default;
                Cursor = ObjectDragCursor(dragKind);
                InvalidateVisual();
                CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        // Check if clicking on a new drawing object
        var hit = HitTestDrawingObject(pos);
        if (hit.Id != Guid.Empty)
        {
            SelectedObjectId = hit.Id;
            SelectedObjectKind = hit.Kind;
            _selectedObjectId = hit.Id;
            _selectedObjectKind = hit.Kind;
            _objectDragKind = ObjectDragKind.Move;
            _objectDragStartPos = pos;
            _objectDragStartRect = hit.Rect;
            _objectDragCurrentRect = hit.Rect;
            _objectDragStartAnchor = hit.Anchor;
            Cursor = Cursors.SizeAll;
            InvalidateVisual();
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // Clicking empty space deselects
        if (SelectedObjectId != Guid.Empty)
        {
            SelectedObjectId = Guid.Empty;
            SelectedObjectKind = ObjectKind.None;
            _selectedObjectId = Guid.Empty;
            _selectedObjectKind = ObjectKind.None;
            InvalidateVisual();
        }

        if (HitTestPivotChartFieldButton(Charts, pos, ActualRowHeaderWidth, EffectiveColHeaderHeight) is { } pivotButton)
        {
            PivotChartFieldButtonRequested?.Invoke(pivotButton.Chart, pivotButton.FieldButton, pos);
            e.Handled = true;
            return;
        }

        if (HitTestPageMarginGuide(pos) is { } marginEdge)
        {
            _marginDragEdge = marginEdge;
            Cursor = marginEdge is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right
                ? Cursors.SizeWE
                : Cursors.SizeNS;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (Viewport is not null)
        {
            var chrome = CalculateSplitPaneScrollbarChrome(Viewport, ActualWidth, ActualHeight);
            if (HitTestSplitPaneScrollbar(chrome, pos) is { } scrollbarHit)
            {
                _splitPaneScrollbarDragSource = scrollbarHit.Region == SplitPaneRegion.TopRight
                    ? chrome.HorizontalTopRight
                    : chrome.VerticalBottomLeft;
                _splitPaneScrollbarDragging = scrollbarHit.Part == SplitPaneScrollbarPart.Thumb &&
                    _splitPaneScrollbarDragSource is not null;
                _splitPaneScrollbarDragPointerOffset = _splitPaneScrollbarDragSource is null
                    ? 0
                    : scrollbarHit.Orientation == SplitPaneScrollbarOrientation.Horizontal
                        ? pos.X - _splitPaneScrollbarDragSource.Thumb.Left
                        : pos.Y - _splitPaneScrollbarDragSource.Thumb.Top;
                if (!_splitPaneScrollbarDragging)
                {
                    _splitPaneScrollbarDragSource = null;
                    _splitPaneScrollbarDragPointerOffset = 0;
                }
                if (CalculateSplitPaneScrollbarInteractionTarget(Viewport, chrome, pos) is { } scrollTarget)
                    SplitPaneScrollbarScrolled?.Invoke(scrollTarget);
                Cursor = scrollbarHit.Orientation == SplitPaneScrollbarOrientation.Horizontal ? Cursors.SizeWE : Cursors.SizeNS;
                if (_splitPaneScrollbarDragging)
                    CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        if (Viewport is not null && HitTestSplitDividerHandle(Viewport, pos) is { } splitHandle &&
            splitHandle != SplitDividerHandle.None)
        {
            _splitDividerDragHandle = splitHandle;
            Cursor = splitHandle == SplitDividerHandle.Intersection ? Cursors.SizeAll
                   : splitHandle == SplitDividerHandle.Vertical ? Cursors.SizeWE
                   : Cursors.SizeNS;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (SelectedRange.HasValue && IsOnAutofillHandle(pos))
        {
            _autofillDragging    = true;
            _autofillSourceRange = SelectedRange.Value;
            _autofillTarget      = SelectedRange.Value.End;
            CaptureMouse();
            Cursor = Cursors.Cross;
            e.Handled = true;
            return;
        }

        var (target, index, size) = HitTestResize(pos);
        if (target != ResizeTarget.None)
        {
            if (e.ClickCount >= 2)
            {
                if (target == ResizeTarget.Column)
                    ColumnAutoFitRequested?.Invoke(index);
                else
                    RowAutoFitRequested?.Invoke(index);

                e.Handled = true;
                return;
            }

            _resizeTarget    = target;
            _resizeIndex     = index;
            _resizeSizeStart = size;
            _resizeDragStart = target == ResizeTarget.Column ? pos.X : pos.Y;
            Cursor = target == ResizeTarget.Column ? Cursors.SizeWE : Cursors.SizeNS;

            if (target == ResizeTarget.Column)
            {
                var col = FindColMetric(Viewport!.ColMetrics, index)!;
                _resizeLinePos = col.LeftOffset + col.Width + ActualRowHeaderWidth;
            }
            else
            {
                var row = FindRowMetric(Viewport!.RowMetrics, index)!;
                _resizeLinePos = row.TopOffset + row.Height + EffectiveColHeaderHeight;
            }

            CaptureMouse();
            e.Handled = true;
        }
        else
        {
            base.OnMouseLeftButtonDown(e);
        }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (HasActiveCapturedGridDrag())
        {
            e.Handled = true;
            return;
        }

        if (Viewport == null) { base.OnMouseRightButtonDown(e); return; }
        var pos = e.GetPosition(this);
        if (HitTestPivotChartFieldButton(Charts, pos, ActualRowHeaderWidth, EffectiveColHeaderHeight) is { } pivotButton)
        {
            PivotChartFieldButtonRequested?.Invoke(pivotButton.Chart, pivotButton.FieldButton, pos);
            e.Handled = true;
            return;
        }

        var objectHit = HitTestDrawingObject(pos);
        if (objectHit.Id != Guid.Empty)
        {
            SelectedObjectId = objectHit.Id;
            SelectedObjectKind = objectHit.Kind;
            _selectedObjectId = objectHit.Id;
            _selectedObjectKind = objectHit.Kind;
            InvalidateVisual();
            ContextMenuRequested?.Invoke(objectHit.Anchor, pos);
            e.Handled = true;
            return;
        }

        if (pos.Y <= EffectiveColHeaderHeight && pos.X >= ActualRowHeaderWidth)
        {
            foreach (var cm in Viewport.ColMetrics)
            {
                double left = cm.LeftOffset + ActualRowHeaderWidth;
                if (pos.X >= left && pos.X < left + cm.Width)
                {
                    HeaderContextMenuRequested?.Invoke(GridHeaderContextMenuTarget.Column, cm.Col, pos);
                    e.Handled = true;
                    return;
                }
            }

            base.OnMouseRightButtonDown(e);
            return;
        }

        if (pos.X <= ActualRowHeaderWidth && pos.Y >= EffectiveColHeaderHeight)
        {
            foreach (var rm in Viewport.RowMetrics)
            {
                double top = rm.TopOffset + EffectiveColHeaderHeight;
                if (pos.Y >= top && pos.Y < top + rm.Height)
                {
                    HeaderContextMenuRequested?.Invoke(GridHeaderContextMenuTarget.Row, rm.Row, pos);
                    e.Handled = true;
                    return;
                }
            }

            base.OnMouseRightButtonDown(e);
            return;
        }

        if (HitTestViewportCell(Viewport, default, pos) is { } contextCell)
        {
            ContextMenuRequested?.Invoke(contextCell, pos);
            e.Handled = true;
            return;
        }

        base.OnMouseRightButtonDown(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_objectDragKind != ObjectDragKind.None)
        {
            var pos = e.GetPosition(this);
            var dragKind = _objectDragKind;
            var id = _selectedObjectId;
            var kind = _selectedObjectKind;
            var startRect = _objectDragStartRect;
            var currentRect = _objectDragCurrentRect;

            _objectDragKind = ObjectDragKind.None;
            _objectDragCurrentRect = Rect.Empty;
            Cursor = null;
            ReleaseMouseCapture();

            if (dragKind == ObjectDragKind.Move)
            {
                var newAnchor = HitTestAnchorCell(pos);
                if (newAnchor.HasValue && newAnchor.Value != _objectDragStartAnchor)
                    ObjectMoved?.Invoke(id, kind, newAnchor.Value);
            }
            else
            {
                var newWidth  = Math.Max(8, currentRect.Width);
                var newHeight = Math.Max(8, currentRect.Height);
                if (Math.Abs(newWidth - startRect.Width) > 1 || Math.Abs(newHeight - startRect.Height) > 1)
                    ObjectResized?.Invoke(id, kind, newWidth, newHeight);
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_marginDragEdge.HasValue)
        {
            var pos = e.GetPosition(this);
            if (GetPageMarginsForDraggedGuide(pos) is { } margins)
            {
                PageMargins = margins;
                PageMarginsChanged?.Invoke(margins);
            }

            _marginDragEdge = null;
            Cursor = null;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_splitDividerDragHandle != SplitDividerHandle.None)
        {
            var pos = e.GetPosition(this);
            if (Viewport is not null &&
                CalculateSplitDividerDragTarget(Viewport, _splitDividerDragHandle, pos) is { } target)
            {
                SplitDividerMoved?.Invoke(target.Row, target.Column);
            }

            _splitDividerDragHandle = SplitDividerHandle.None;
            Cursor = null;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_splitPaneScrollbarDragging)
        {
            var pos = e.GetPosition(this);
            if (Viewport is not null && _splitPaneScrollbarDragSource is not null)
            {
                var target = CalculateSplitPaneScrollbarThumbDragTarget(
                    _splitPaneScrollbarDragSource,
                    pos,
                    _splitPaneScrollbarDragPointerOffset);
                SplitPaneScrollbarScrolled?.Invoke(target);
            }

            _splitPaneScrollbarDragging = false;
            _splitPaneScrollbarDragSource = null;
            _splitPaneScrollbarDragPointerOffset = 0;
            Cursor = null;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_autofillDragging)
        {
            _autofillDragging = false;
            ReleaseMouseCapture();
            Cursor = null;

            if (_autofillSourceRange.HasValue && _autofillTarget.HasValue)
            {
                var src = _autofillSourceRange.Value;
                var fillRange = GridAutofillPlanner.CalculateFillRange(src, _autofillTarget.Value);
                if (fillRange.HasValue)
                {
                    AutofillRequested?.Invoke(src, fillRange.Value);
                }
            }

            _autofillSourceRange = null;
            _autofillTarget      = null;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_resizeTarget != ResizeTarget.None)
        {
            var pos = e.GetPosition(this);
            double delta = _resizeTarget == ResizeTarget.Column
                ? pos.X - _resizeDragStart
                : pos.Y - _resizeDragStart;
            double newSize = Math.Max(MinCellSize, _resizeSizeStart + delta);

            if (_resizeTarget == ResizeTarget.Column)
                ColumnResized?.Invoke(_resizeIndex, newSize);
            else
                RowResized?.Invoke(_resizeIndex, newSize);

            _resizeTarget = ResizeTarget.None;
            Cursor = null;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
        }
        else
        {
            base.OnMouseLeftButtonUp(e);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (!HasActiveCapturedGridDrag())
            Cursor = null;
        base.OnMouseLeave(e);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (_objectDragKind != ObjectDragKind.None)
        {
            _objectDragKind = ObjectDragKind.None;
            _objectDragCurrentRect = Rect.Empty;
            Cursor = null;
            InvalidateVisual();
        }

        if (_marginDragEdge.HasValue)
        {
            _marginDragEdge = null;
            Cursor = null;
            InvalidateVisual();
        }

        if (_splitDividerDragHandle != SplitDividerHandle.None)
        {
            _splitDividerDragHandle = SplitDividerHandle.None;
            Cursor = null;
            InvalidateVisual();
        }

        if (_splitPaneScrollbarDragging)
        {
            _splitPaneScrollbarDragging = false;
            _splitPaneScrollbarDragSource = null;
            _splitPaneScrollbarDragPointerOffset = 0;
            Cursor = null;
            InvalidateVisual();
        }

        if (_autofillDragging)
        {
            _autofillDragging = false;
            _autofillSourceRange = null;
            _autofillTarget = null;
            Cursor = null;
            InvalidateVisual();
        }

        if (_resizeTarget != ResizeTarget.None)
        {
            _resizeTarget = ResizeTarget.None;
            Cursor = null;
            ResizeCanceled?.Invoke();
            InvalidateVisual();
        }

        base.OnLostMouseCapture(e);
    }

}
