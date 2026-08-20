using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Presentation.DrawingInteraction;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    /// <summary>
    /// Fired immediately before <see cref="SelectionMoveRequested"/> with the Ctrl-key state at
    /// release. Mirrors <see cref="AutofillModifiersResolved"/>: Excel copies the dragged range
    /// to the destination (leaving the source intact) instead of moving it when Ctrl is held
    /// during a selection-border drag. Hosts that want Ctrl-drag-to-copy support should read this
    /// value in a handler for this event and branch between a copy and a move command when
    /// handling the paired <see cref="SelectionMoveRequested"/> call.
    /// </summary>
    public event Action<bool>? SelectionMoveModifiersResolved;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (HasActiveCapturedGridDrag() && e.LeftButton != MouseButtonState.Pressed)
        {
            CancelActiveCapturedGridDrag();
            e.Handled = true;
            return;
        }

        var pos = e.GetPosition(this);
        if (HasActiveCapturedGridDrag())
        {
            DismissCommentPreview();
            DismissHyperlinkScreenTip();
        }
        else
        {
            UpdateCommentPreviewForPointer(pos);
            UpdateHyperlinkScreenTip(pos);
        }

        if (_shapePlacementDragging)
        {
            UpdateShapePlacementPreview(pos);
            e.Handled = true;
            return;
        }

        if (_textBoxPlacementDragging)
        {
            UpdateTextBoxPlacementPreview(pos);
            e.Handled = true;
            return;
        }

        if (_pictureCropDragHandle != PictureCropHandle.None)
        {
            var localPos = TransformPointToUnrotatedObjectSpace(
                _pictureCropDragStartRect,
                pos,
                GetSelectedObjectRotationDegrees());
            _pictureCropDragCurrentRatios = GridPictureCropPlanner.CalculateCrop(
                _pictureCropDragHandle,
                _pictureCropDragStartRatios,
                _pictureCropDragStartRect,
                _pictureCropDragStartPos,
                localPos);
            Cursor = PictureCropCursor(_pictureCropDragHandle);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_objectDragKind == ObjectDragKind.Rotate)
        {
            var center = new Point(
                _objectDragStartRect.Left + _objectDragStartRect.Width / 2,
                _objectDragStartRect.Top + _objectDragStartRect.Height / 2);
            _objectRotationPreviewDegrees = GridObjectDragPlanner.CalculateRotationDegrees(center, pos);
            Cursor = ObjectDragCursor(_objectDragKind);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_objectDragKind != ObjectDragKind.None)
        {
            var dragTransform = GridObjectDragPlanner.CalculateDragTransform(
                _objectDragKind,
                _objectDragStartRect,
                _objectDragStartPos,
                pos);
            _objectDragCurrentRect = dragTransform.Rect;
            _objectDragCurrentFlipHorizontal = _objectDragStartFlipHorizontal ^ dragTransform.CrossedHorizontally;
            _objectDragCurrentFlipVertical = _objectDragStartFlipVertical ^ dragTransform.CrossedVertically;
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

        if (_pageBreakLineDragHit.HasValue)
        {
            Cursor = _pageBreakLineDragHit.Value.Orientation == PageBreakLineOrientation.Row
                ? Cursors.SizeNS
                : Cursors.SizeWE;
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
                if (_splitPaneScrollbarDragSource is { } dragSource &&
                    CalculateSplitPaneScrollbarThumbDragTarget(
                        dragSource,
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
                GetLogicalViewportWidth(),
                GetLogicalViewportHeight(),
                ActualRowHeaderWidth,
                EffectiveColHeaderHeight);
            if (scrollRequest.HasAnyDirection)
                AutofillEdgeScrollRequested?.Invoke(scrollRequest);

            var src = _autofillSourceRange.Value;
            if (GridAutofillPlanner.CalculateDragTarget(
                    Viewport,
                    src,
                    new GridPoint(pos.X, pos.Y),
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

        if (_selectionMoveDragging)
        {
            UpdateSelectionMovePreview(pos);
            Cursor = Cursors.SizeAll;
            e.Handled = true;
            return;
        }

        if (_resizeTarget == ResizeTarget.Column)
        {
            if (Viewport is null)
            {
                Cursor = Cursors.SizeWE;
                e.Handled = true;
                return;
            }

            var col = FindColMetric(Viewport.ColMetrics, _resizeIndex);
            if (col is null)
            {
                var delta = pos.X - _resizeDragStart;
                double previewWidth = GridResizeSizePlanner.ClampColumnSize(_resizeSizeStart + delta);
                if ((_resizeCollapsedBoundary && delta > 0) || (!_resizeCollapsedBoundary && previewWidth > 0))
                {
                    _resizeLinePos = GridResizeSizePlanner.CalculateLinePosition(_resizeSizeStart, _resizeDragStart, previewWidth);
                    ColumnResizing?.Invoke(_resizeIndex, previewWidth);
                    InvalidateVisual();
                }

                Cursor = Cursors.SizeWE;
                e.Handled = true;
                return;
            }
            double newWidth = GridResizeSizePlanner.ClampColumnSize(_resizeSizeStart + (pos.X - _resizeDragStart));
            _resizeLinePos = GridResizeSizePlanner.CalculateLinePosition(_resizeSizeStart, _resizeDragStart, newWidth);
            ColumnResizing?.Invoke(_resizeIndex, newWidth);
            Cursor = Cursors.SizeWE;
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        else if (_resizeTarget == ResizeTarget.Row)
        {
            if (Viewport is null)
            {
                Cursor = Cursors.SizeNS;
                e.Handled = true;
                return;
            }

            var row = FindRowMetric(Viewport.RowMetrics, _resizeIndex);
            if (row is null)
            {
                var delta = pos.Y - _resizeDragStart;
                double previewHeight = GridResizeSizePlanner.ClampRowSize(_resizeSizeStart + delta);
                if ((_resizeCollapsedBoundary && delta > 0) || (!_resizeCollapsedBoundary && previewHeight > 0))
                {
                    _resizeLinePos = GridResizeSizePlanner.CalculateLinePosition(_resizeSizeStart, _resizeDragStart, previewHeight);
                    RowResizing?.Invoke(_resizeIndex, previewHeight);
                    InvalidateVisual();
                }

                Cursor = Cursors.SizeNS;
                e.Handled = true;
                return;
            }
            double newHeight = GridResizeSizePlanner.ClampRowSize(_resizeSizeStart + (pos.Y - _resizeDragStart));
            _resizeLinePos = GridResizeSizePlanner.CalculateLinePosition(_resizeSizeStart, _resizeDragStart, newHeight);
            RowResizing?.Invoke(_resizeIndex, newHeight);
            Cursor = Cursors.SizeNS;
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        else
        {
            UpdateHoverCursor(pos);
        }
    }

    public void RefreshPointerCursor()
    {
        if (!IsMouseOver || HasActiveCapturedGridDrag())
            return;

        UpdateHoverCursor(Mouse.GetPosition(this));
    }

    private void UpdateHoverCursor(Point pos)
    {
        var selectedObjectDragKind = ObjectDragKind.None;
        if (SelectedObjectId != Guid.Empty &&
            SelectedObjectKind != ObjectKind.None)
        {
            var selectedObjectRect = GetSelectedObjectRect();
            if (IsSelectedPictureCropModeActive())
            {
                var selectedPictureCropHandle = HitTestPictureCropHandle(pos, selectedObjectRect);
                if (selectedPictureCropHandle != PictureCropHandle.None)
                {
                    Cursor = PictureCropCursor(selectedPictureCropHandle);
                    return;
                }
            }
            else
            {
                selectedObjectDragKind = HitTestObjectHandle(pos, selectedObjectRect);
            }
        }
        if (selectedObjectDragKind != ObjectDragKind.None)
        {
            Cursor = ObjectDragCursor(selectedObjectDragKind);
            return;
        }

        var hitObject = HitTestDrawingObject(pos);
        var hoveringObjectBody = selectedObjectDragKind == ObjectDragKind.None &&
            hitObject.Id != Guid.Empty;
        if (hoveringObjectBody)
        {
            Cursor = Cursors.SizeAll;
            return;
        }

        var splitHandle = Viewport is null
            ? SplitDividerHandle.None
            : HitTestSplitDividerHandle(Viewport, pos, ActualWidth, ActualHeight);
        if (splitHandle != SplitDividerHandle.None)
        {
            Cursor = splitHandle == SplitDividerHandle.Intersection ? Cursors.SizeAll
                   : splitHandle == SplitDividerHandle.Vertical ? Cursors.SizeWE
                   : Cursors.SizeNS;
            return;
        }

        if (TryHitTestOutlineGroupToggle(Viewport, pos, ActualRowHeaderWidth, EffectiveColHeaderHeight, out _))
        {
            Cursor = Cursors.Hand;
            return;
        }

        var (target, _, _, _) = HitTestResize(pos);
        if (target == ResizeTarget.Column)
        {
            Cursor = Cursors.SizeWE;
            return;
        }

        if (target == ResizeTarget.Row)
        {
            Cursor = Cursors.SizeNS;
            return;
        }

        var splitScrollbarHit = Viewport is null
            ? null
            : HitTestSplitPaneScrollbar(CalculateSplitPaneScrollbarChrome(Viewport, GetLogicalViewportWidth(), GetLogicalViewportHeight()), pos);
        if (splitScrollbarHit?.Orientation == SplitPaneScrollbarOrientation.Horizontal)
        {
            Cursor = Cursors.SizeWE;
            return;
        }

        if (splitScrollbarHit?.Orientation == SplitPaneScrollbarOrientation.Vertical)
        {
            Cursor = Cursors.SizeNS;
            return;
        }

        var marginGuide = HitTestPageMarginGuide(pos);
        var pageBreakLine = HitTestPageBreakLine(pos);
        Cursor = marginGuide is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right ? Cursors.SizeWE
               : marginGuide is WorksheetPageMarginEdge.Top or WorksheetPageMarginEdge.Bottom ? Cursors.SizeNS
               : pageBreakLine?.Orientation == PageBreakLineOrientation.Row ? Cursors.SizeNS
               : pageBreakLine?.Orientation == PageBreakLineOrientation.Column ? Cursors.SizeWE
               : IsOnAutofillHandle(pos) ? Cursors.Cross
               : IsOnSelectionMoveBorder(pos) ? Cursors.SizeAll
               : IsCtrlModifierDown() && TryHitTestHyperlinkCell(pos, out _) ? Cursors.Hand
               : null;
    }

    private bool TryHitTestHyperlinkCell(Point pos, out CellAddress address)
    {
        address = default;
        if (Viewport is null || HyperlinkCells is null || HyperlinkCells.Count == 0)
            return false;

        if (HitTestViewportCell(Viewport, default, pos) is not { } hitCell ||
            !HyperlinkCells.Contains(hitCell))
        {
            return false;
        }

        address = hitCell;
        return true;
    }

    private static bool IsCtrlModifierDown() =>
        (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

    private Border? _hyperlinkScreenTipBorder;
    private TextBlock? _hyperlinkScreenTipTextBlock;
    private CellAddress? _hyperlinkScreenTipCell;

    /// <summary>
    /// F1: shows the hover ScreenTip for a hyperlinked cell (the custom ScreenTip if one was set,
    /// otherwise the raw target, per <see cref="HyperlinkTooltips"/>) on plain mouse hover -- no
    /// Ctrl needed, matching Excel and FreeX's own Avalonia shell (MainWindow.cs's
    /// FormatHyperlinkTooltip/ToolTip.SetTip, R88-app-hyperlink-navigation-5-4). The Ctrl+hover
    /// hand cursor in <see cref="UpdateHoverCursor"/> is a separate, independent check. Rendered as
    /// a small Border dropped into <see cref="CommentOverlayHost"/> (the same overlay Canvas the
    /// comment-hover preview already uses) rather than a native WPF ToolTip/Popup, matching
    /// GridView.CommentPreview.cs's own choice to position hover chrome by hand in grid-local
    /// coordinates instead of PlacementMode.
    /// </summary>
    private void UpdateHyperlinkScreenTip(Point pos)
    {
        if (HyperlinkTooltips is { Count: > 0 } tooltips &&
            TryHitTestHyperlinkCell(pos, out var address) &&
            tooltips.TryGetValue(address, out var text) &&
            !string.IsNullOrWhiteSpace(text))
        {
            if (_hyperlinkScreenTipCell == address && _hyperlinkScreenTipBorder is { Visibility: Visibility.Visible })
                return;

            _hyperlinkScreenTipCell = address;
            ShowHyperlinkScreenTip(pos, text);
            return;
        }

        DismissHyperlinkScreenTip();
    }

    private void ShowHyperlinkScreenTip(Point pos, string text)
    {
        var border = EnsureHyperlinkScreenTipBorder();
        if (CommentOverlayHost is null)
            return;

        _hyperlinkScreenTipTextBlock!.Text = text;
        Canvas.SetLeft(border, pos.X + 12);
        Canvas.SetTop(border, pos.Y + 20);
        border.Visibility = Visibility.Visible;
    }

    private Border EnsureHyperlinkScreenTipBorder()
    {
        if (_hyperlinkScreenTipBorder is { } existing)
        {
            if (CommentOverlayHost is not null && !CommentOverlayHost.Children.Contains(existing))
                CommentOverlayHost.Children.Add(existing);
            return existing;
        }

        _hyperlinkScreenTipTextBlock = new TextBlock
        {
            Foreground = Brushes.Black,
            FontSize = 12
        };
        _hyperlinkScreenTipBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(255, 255, 225)),
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2),
            Child = _hyperlinkScreenTipTextBlock,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };

        if (CommentOverlayHost is not null)
            CommentOverlayHost.Children.Add(_hyperlinkScreenTipBorder);

        return _hyperlinkScreenTipBorder;
    }

    private void DismissHyperlinkScreenTip()
    {
        if (_hyperlinkScreenTipBorder is { Visibility: Visibility.Visible } border)
            border.Visibility = Visibility.Collapsed;
        _hyperlinkScreenTipCell = null;
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
        _pictureCropDragHandle != PictureCropHandle.None ||
        _marginDragEdge.HasValue ||
        _pageBreakLineDragHit.HasValue ||
        _splitDividerDragHandle != SplitDividerHandle.None ||
        _splitPaneScrollbarDragging ||
        _autofillDragging ||
        _selectionMoveDragging ||
        _shapePlacementDragging ||
        _textBoxPlacementDragging ||
        _resizeTarget != ResizeTarget.None;

    private void CancelActiveCapturedGridDrag()
    {
        if (_objectDragKind != ObjectDragKind.None)
        {
            _objectDragKind = ObjectDragKind.None;
            _objectDragCurrentRect = Rect.Empty;
            _objectRotationPreviewDegrees = 0;
            _objectDragStartFlipHorizontal = false;
            _objectDragStartFlipVertical = false;
            _objectDragCurrentFlipHorizontal = false;
            _objectDragCurrentFlipVertical = false;
            Cursor = null;
            InvalidateVisual();
        }

        if (_pictureCropDragHandle != PictureCropHandle.None)
        {
            _pictureCropDragHandle = PictureCropHandle.None;
            _pictureCropDragId = Guid.Empty;
            _pictureCropDragStartRect = Rect.Empty;
            _pictureCropDragStartRatios = default;
            _pictureCropDragCurrentRatios = default;
            Cursor = null;
            InvalidateVisual();
        }

        if (_marginDragEdge.HasValue)
        {
            _marginDragEdge = null;
            Cursor = null;
            InvalidateVisual();
        }

        if (_pageBreakLineDragHit.HasValue)
        {
            _pageBreakLineDragHit = null;
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

        if (_selectionMoveDragging)
        {
            _selectionMoveDragging = false;
            _selectionMoveSourceRange = null;
            _selectionMovePreviewRange = null;
            _selectionMoveStartCell = default;
            Cursor = null;
            InvalidateVisual();
        }

        CancelCapturedShapePlacement();
        CancelCapturedTextBoxPlacement();

        if (_resizeTarget != ResizeTarget.None)
        {
            _resizeTarget = ResizeTarget.None;
            _resizeIndex = 0;
            _resizeDragStart = 0;
            _resizeSizeStart = 0;
            _resizeLinePos = 0;
            _resizeCollapsedBoundary = false;
            Cursor = null;
            ResizeCanceled?.Invoke();
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (HasActiveCapturedGridDrag())
        {
            e.Handled = true;
            return;
        }

        var pos = e.GetPosition(this);
        DismissCommentPreview(CommentPreviewActivation.Hover);

        if (TryHitTestOutlineGroupToggle(Viewport, pos, ActualRowHeaderWidth, EffectiveColHeaderHeight, out var outlineToggle))
        {
            OutlineGroupToggleRequested?.Invoke(outlineToggle);
            e.Handled = true;
            return;
        }

        if (TryHitTestOutlineLevelButton(Viewport, pos, ActualRowHeaderWidth, EffectiveColHeaderHeight, out var outlineLevelButton))
        {
            OutlineLevelButtonRequested?.Invoke(outlineLevelButton);
            e.Handled = true;
            return;
        }

        if (TryHitTestAutoFilterButton(pos, out var autoFilterHeaderCell))
        {
            AutoFilterDropdownRequested?.Invoke(autoFilterHeaderCell, pos);
            e.Handled = true;
            return;
        }

        if (TryHitTestPivotHeaderDropdownButton(pos, out var pivotHeaderCell))
        {
            PivotHeaderDropdownRequested?.Invoke(pivotHeaderCell, pos);
            e.Handled = true;
            return;
        }

        if (TryBeginShapePlacement(pos))
        {
            e.Handled = true;
            return;
        }

        if (TryBeginTextBoxPlacement(pos))
        {
            e.Handled = true;
            return;
        }

        // Native slicer / timeline hit test: fires clear-filter / tile-toggle / range / granularity
        // events to the host. Runs before form-control and drawing-object drag so that header-icon
        // clicks land on the right handler (the icons sit inside the slicer/timeline control rect
        // and would otherwise be consumed as drawing-object moves).
        if (TryHandleNativeSlicerTimelineClick(pos))
        {
            e.Handled = true;
            return;
        }

        // Form-control hit test runs before the drawing-object drag path so that clicking a
        // checkbox / spinner / etc. fires the interaction event rather than starting a drag.
        if (TryHandleFormControlClick(pos))
        {
            e.Handled = true;
            return;
        }

        // Check if clicking on an already-selected object's handles
        if (SelectedObjectId != Guid.Empty &&
            SelectedObjectKind != ObjectKind.None)
        {
            var selRect = GetSelectedObjectRect();
            if (IsSelectedPictureCropModeActive())
            {
                var cropHandle = HitTestPictureCropHandle(pos, selRect);
                if (cropHandle != PictureCropHandle.None)
                {
                    _selectedObjectId = SelectedObjectId;
                    _selectedObjectKind = SelectedObjectKind;
                    _pictureCropDragHandle = cropHandle;
                    _pictureCropDragId = SelectedObjectId;
                    _pictureCropDragStartRect = selRect;
                    _pictureCropDragStartPos = TransformPointToUnrotatedObjectSpace(
                        selRect,
                        pos,
                        GetSelectedObjectRotationDegrees());
                    _pictureCropDragStartRatios = GetSelectedPictureCropRatios();
                    _pictureCropDragCurrentRatios = _pictureCropDragStartRatios;
                    Cursor = PictureCropCursor(cropHandle);
                    InvalidateVisual();
                    CaptureMouse();
                    e.Handled = true;
                    return;
                }
            }

            var dragKind = ObjectDragKind.None;
            if (!IsSelectedPictureCropModeActive())
                dragKind = HitTestObjectHandle(pos, selRect);
            if (SelectedObjectKind == ObjectKind.TextBox &&
                e.ClickCount >= 2 &&
                dragKind == ObjectDragKind.Move)
            {
                _selectedObjectId = SelectedObjectId;
                _selectedObjectKind = SelectedObjectKind;
                _objectDragKind = ObjectDragKind.None;
                Cursor = Cursors.IBeam;
                InvalidateVisual();
                TextBoxEditRequested?.Invoke(SelectedObjectId);
                e.Handled = true;
                return;
            }

            if (dragKind != ObjectDragKind.None)
            {
                _selectedObjectId = SelectedObjectId;
                _selectedObjectKind = SelectedObjectKind;
                _objectDragKind = dragKind;
                _objectDragStartPos = pos;
                _objectDragStartRect = selRect;
                _objectDragCurrentRect = selRect;
                _objectRotationPreviewDegrees = GetSelectedObjectRotationDegrees();
                var flipState = GetSelectedObjectFlipState();
                _objectDragStartFlipHorizontal = flipState.Horizontal;
                _objectDragStartFlipVertical = flipState.Vertical;
                _objectDragCurrentFlipHorizontal = flipState.Horizontal;
                _objectDragCurrentFlipVertical = flipState.Vertical;
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
            IsPictureCropMode = false;
            SelectedObjectId = hit.Id;
            SelectedObjectKind = hit.Kind;
            _selectedObjectId = hit.Id;
            _selectedObjectKind = hit.Kind;
            if (hit.Kind == ObjectKind.TextBox && e.ClickCount >= 2)
            {
                _objectDragKind = ObjectDragKind.None;
                Cursor = Cursors.IBeam;
                InvalidateVisual();
                TextBoxEditRequested?.Invoke(hit.Id);
                e.Handled = true;
                return;
            }

            _objectDragKind = ObjectDragKind.Move;
            _objectDragStartPos = pos;
            _objectDragStartRect = hit.Rect;
            _objectDragCurrentRect = hit.Rect;
            _objectRotationPreviewDegrees = GetSelectedObjectRotationDegrees();
            var flipState = GetSelectedObjectFlipState();
            _objectDragStartFlipHorizontal = flipState.Horizontal;
            _objectDragStartFlipVertical = flipState.Vertical;
            _objectDragCurrentFlipHorizontal = flipState.Horizontal;
            _objectDragCurrentFlipVertical = flipState.Vertical;
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
            IsPictureCropMode = false;
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

        if (HitTestPageBreakLine(pos) is { } pageBreakLineHit)
        {
            _pageBreakLineDragHit = pageBreakLineHit;
            Cursor = pageBreakLineHit.Orientation == PageBreakLineOrientation.Row
                ? Cursors.SizeNS
                : Cursors.SizeWE;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (Viewport is not null)
        {
            var chrome = CalculateSplitPaneScrollbarChrome(Viewport, GetLogicalViewportWidth(), GetLogicalViewportHeight());
            if (HitTestSplitPaneScrollbar(chrome, pos) is { } scrollbarHit)
            {
                var dragSource = scrollbarHit.Region == SplitPaneRegion.TopRight
                    ? chrome.HorizontalTopRight
                    : chrome.VerticalBottomLeft;
                _splitPaneScrollbarDragSource = dragSource;
                _splitPaneScrollbarDragging = scrollbarHit.Part == SplitPaneScrollbarPart.Thumb &&
                    dragSource is not null;
                _splitPaneScrollbarDragPointerOffset = dragSource is not { } scrollbar
                    ? 0
                    : scrollbarHit.Orientation == SplitPaneScrollbarOrientation.Horizontal
                        ? pos.X - scrollbar.Thumb.Left
                        : pos.Y - scrollbar.Thumb.Top;
                if (!_splitPaneScrollbarDragging)
                {
                    _splitPaneScrollbarDragSource = null;
                    _splitPaneScrollbarDragPointerOffset = 0;
                }
                if (CalculateSplitPaneScrollbarInteractionTarget(Viewport, chrome, scrollbarHit, pos) is { } scrollTarget)
                    SplitPaneScrollbarScrolled?.Invoke(scrollTarget);
                Cursor = scrollbarHit.Orientation == SplitPaneScrollbarOrientation.Horizontal ? Cursors.SizeWE : Cursors.SizeNS;
                if (_splitPaneScrollbarDragging)
                    CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        if (Viewport is not null && HitTestSplitDividerHandle(Viewport, pos, GetLogicalViewportWidth(), GetLogicalViewportHeight()) is { } splitHandle &&
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
            if (e.ClickCount >= 2)
            {
                AutofillHandleDoubleClicked?.Invoke(SelectedRange.Value);
                e.Handled = true;
                return;
            }

            _autofillDragging    = true;
            _autofillSourceRange = SelectedRange.Value;
            _autofillTarget      = SelectedRange.Value.End;
            CaptureMouse();
            Cursor = Cursors.Cross;
            e.Handled = true;
            return;
        }

        if (TryBeginSelectionMoveDrag(pos))
        {
            e.Handled = true;
            return;
        }

        var (target, index, size, isCollapsedBoundary) = HitTestResize(pos);
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
            _resizeCollapsedBoundary = isCollapsedBoundary;
            Cursor = target == ResizeTarget.Column ? Cursors.SizeWE : Cursors.SizeNS;

            if (target == ResizeTarget.Column)
            {
                var col = FindColMetric(Viewport!.ColMetrics, index);
                _resizeLinePos = col is null
                    ? pos.X
                    : col.LeftOffset + col.Width + ActualRowHeaderWidth;
                _resizeDragStart = _resizeLinePos;
            }
            else
            {
                var row = FindRowMetric(Viewport!.RowMetrics, index);
                _resizeLinePos = row is null
                    ? pos.Y
                    : row.TopOffset + row.Height + EffectiveColHeaderHeight;
                _resizeDragStart = _resizeLinePos;
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

        if (HitTestWaterfallChartPoint(Charts, pos, ActualRowHeaderWidth, EffectiveColHeaderHeight) is { } waterfallPoint)
        {
            WaterfallChartPointContextMenuRequested?.Invoke(waterfallPoint.Chart, waterfallPoint.PointIndex, pos);
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

        if (SelectedObjectId != Guid.Empty)
        {
            SelectedObjectId = Guid.Empty;
            SelectedObjectKind = ObjectKind.None;
            _selectedObjectId = Guid.Empty;
            _selectedObjectKind = ObjectKind.None;
            InvalidateVisual();
        }

        if (GridHeaderContextMenuHitPlanner.HitTest(Viewport, pos, ActualRowHeaderWidth, EffectiveColHeaderHeight) is { } headerHit)
        {
            HeaderContextMenuRequested?.Invoke(headerHit.Target, headerHit.Index, pos);
            e.Handled = true;
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
        if (_shapePlacementDragging)
        {
            CommitShapePlacement(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        if (_textBoxPlacementDragging)
        {
            CommitTextBoxPlacement(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        if (_pictureCropDragHandle != PictureCropHandle.None)
        {
            var handle = _pictureCropDragHandle;
            var id = _pictureCropDragId;
            var crop = _pictureCropDragCurrentRatios;
            var changed =
                Math.Abs(crop.Left - _pictureCropDragStartRatios.Left) > 0.0001 ||
                Math.Abs(crop.Top - _pictureCropDragStartRatios.Top) > 0.0001 ||
                Math.Abs(crop.Right - _pictureCropDragStartRatios.Right) > 0.0001 ||
                Math.Abs(crop.Bottom - _pictureCropDragStartRatios.Bottom) > 0.0001;

            _pictureCropDragHandle = PictureCropHandle.None;
            _pictureCropDragId = Guid.Empty;
            _pictureCropDragStartRect = Rect.Empty;
            _pictureCropDragStartRatios = default;
            _pictureCropDragCurrentRatios = default;
            Cursor = null;
            ReleaseMouseCapture();

            if (handle != PictureCropHandle.None && id != Guid.Empty && changed)
                PictureCropped?.Invoke(id, crop);

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_objectDragKind != ObjectDragKind.None)
        {
            var pos = e.GetPosition(this);
            var dragKind = _objectDragKind;
            var id = _selectedObjectId;
            var kind = _selectedObjectKind;
            var startRect = _objectDragStartRect;
            var currentRect = _objectDragCurrentRect;

            var rotationDegrees = _objectRotationPreviewDegrees;
            var startFlipHorizontal = _objectDragStartFlipHorizontal;
            var startFlipVertical = _objectDragStartFlipVertical;
            var currentFlipHorizontal = _objectDragCurrentFlipHorizontal;
            var currentFlipVertical = _objectDragCurrentFlipVertical;
            _objectDragKind = ObjectDragKind.None;
            _objectDragCurrentRect = Rect.Empty;
            _objectRotationPreviewDegrees = 0;
            _objectDragStartFlipHorizontal = false;
            _objectDragStartFlipVertical = false;
            _objectDragCurrentFlipHorizontal = false;
            _objectDragCurrentFlipVertical = false;
            Cursor = null;
            ReleaseMouseCapture();

            if (kind == ObjectKind.Chart)
            {
                CommitChartObjectBoundsChange(id, startRect, currentRect);
            }
            else
            {
                var newWidth  = Math.Max(GridObjectDragPlanner.MinimumObjectSize, currentRect.Width);
                var newHeight = Math.Max(GridObjectDragPlanner.MinimumObjectSize, currentRect.Height);
                var newAnchor = dragKind == ObjectDragKind.Rotate
                    ? null
                    : HitTestAnchorCell(new Point(currentRect.Left, currentRect.Top));
                var plan = GridObjectDragPlanner.PlanCommit(
                    dragKind,
                    startRect,
                    currentRect,
                    _objectDragStartAnchor,
                    newAnchor,
                    newWidth,
                    newHeight,
                    rotationDegrees,
                    startFlipHorizontal,
                    startFlipVertical,
                    currentFlipHorizontal,
                    currentFlipVertical);

                switch (plan.Kind)
                {
                    case ObjectDragCommitKind.Move:
                        ObjectMoved?.Invoke(id, kind, plan.Anchor!.Value);
                        break;
                    case ObjectDragCommitKind.ResizeWithAnchor:
                        ObjectResizedWithAnchor?.Invoke(
                            id,
                            kind,
                            plan.Anchor!.Value,
                            plan.Width,
                            plan.Height,
                            plan.FlipHorizontal,
                            plan.FlipVertical);
                        break;
                    case ObjectDragCommitKind.Resize:
                        ObjectResized?.Invoke(
                            id,
                            kind,
                            plan.Width,
                            plan.Height,
                            plan.FlipHorizontal,
                            plan.FlipVertical);
                        break;
                    case ObjectDragCommitKind.Rotate:
                        ObjectRotated?.Invoke(id, kind, plan.RotationDegrees);
                        break;
                }
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

        if (_pageBreakLineDragHit.HasValue)
        {
            var hit = _pageBreakLineDragHit.Value;
            var pos = e.GetPosition(this);
            var newIndex = Viewport is null
                ? null
                : CalculatePageBreakLineDragTarget(
                    Viewport,
                    hit.Orientation,
                    pos,
                    ActualRowHeaderWidth,
                    EffectiveColHeaderHeight,
                    GetLogicalViewportWidth(),
                    GetLogicalViewportHeight());
            if (newIndex != hit.Index)
                PageBreakLineMoved?.Invoke(hit.Orientation, hit.Index, newIndex);

            _pageBreakLineDragHit = null;
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
            if (Viewport is not null && _splitPaneScrollbarDragSource is { } dragSource)
            {
                var target = CalculateSplitPaneScrollbarThumbDragTarget(
                    dragSource,
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
                var fillRange = GridAutofillPlanner.CalculateFillRange(src, _autofillTarget.Value)
                    ?? GridAutofillPlanner.CalculateClearRange(src, _autofillTarget.Value);
                if (fillRange.HasValue)
                {
                    AutofillModifiersResolved?.Invoke(IsCtrlModifierDown());
                    AutofillRequested?.Invoke(src, fillRange.Value);
                }
            }

            _autofillSourceRange = null;
            _autofillTarget      = null;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_selectionMoveDragging)
        {
            var pos = e.GetPosition(this);
            UpdateSelectionMovePreview(pos);
            var source = _selectionMoveSourceRange;
            var target = _selectionMovePreviewRange;

            _selectionMoveDragging = false;
            _selectionMoveSourceRange = null;
            _selectionMovePreviewRange = null;
            _selectionMoveStartCell = default;
            Cursor = null;
            ReleaseMouseCapture();

            if (source.HasValue && target.HasValue && source.Value != target.Value)
            {
                SelectionMoveModifiersResolved?.Invoke(IsCtrlModifierDown());
                SelectionMoveRequested?.Invoke(source.Value, target.Value);
            }

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
            double newSize = _resizeTarget == ResizeTarget.Column
                ? GridResizeSizePlanner.ClampColumnSize(_resizeSizeStart + delta)
                : GridResizeSizePlanner.ClampRowSize(_resizeSizeStart + delta);

            var shouldCommitResize = !_resizeCollapsedBoundary || delta > 0;
            if (shouldCommitResize && _resizeTarget == ResizeTarget.Column)
                ColumnResized?.Invoke(_resizeIndex, newSize);
            else if (shouldCommitResize)
                RowResized?.Invoke(_resizeIndex, newSize);
            else
                ResizeCanceled?.Invoke();

            _resizeTarget = ResizeTarget.None;
            _resizeIndex = 0;
            _resizeDragStart = 0;
            _resizeSizeStart = 0;
            _resizeLinePos = 0;
            _resizeCollapsedBoundary = false;
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

    private void CommitChartObjectBoundsChange(Guid id, Rect startRect, Rect currentRect)
    {
        var moved =
            Math.Abs(currentRect.Left - startRect.Left) > 1 ||
            Math.Abs(currentRect.Top - startRect.Top) > 1;
        var resized =
            Math.Abs(currentRect.Width - startRect.Width) > 1 ||
            Math.Abs(currentRect.Height - startRect.Height) > 1;
        if (!moved && !resized)
            return;

        ChartBoundsChanged?.Invoke(
            id,
            Math.Max(0, currentRect.Left - ActualRowHeaderWidth),
            Math.Max(0, currentRect.Top - EffectiveColHeaderHeight),
            Math.Max(MinimumChartObjectWidth, currentRect.Width),
            Math.Max(MinimumChartObjectHeight, currentRect.Height));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (!HasActiveCapturedGridDrag())
        {
            Cursor = null;
            RestoreSelectedCommentPreview();
        }
        DismissHyperlinkScreenTip();
        base.OnMouseLeave(e);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        CancelActiveCapturedGridDrag();
        RestoreSelectedCommentPreview();
        base.OnLostMouseCapture(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _activeCommentPreviewKey.HasValue)
        {
            DismissCommentPreview();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private bool IsOnSelectionMoveBorder(Point pos) =>
        EnableFillHandleAndCellDragAndDrop &&
        (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Control) &&
        GridSelectionMovePlanner.IsOnMoveBorder(
            Viewport,
            SelectedRange,
            SelectedRanges,
            pos,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight);

    private bool TryBeginSelectionMoveDrag(Point pos)
    {
        if (!IsOnSelectionMoveBorder(pos) ||
            Viewport is null ||
            SelectedRange is not { } sourceRange)
        {
            return false;
        }

        var hitCell = HitTestViewportCell(Viewport, sourceRange.Start.Sheet, pos) ?? sourceRange.Start;
        _selectionMoveDragging = true;
        _selectionMoveSourceRange = sourceRange;
        _selectionMoveStartCell = GridSelectionMovePlanner.ClampDragStartCell(sourceRange, hitCell);
        _selectionMovePreviewRange = sourceRange;
        Cursor = Cursors.SizeAll;
        InvalidateVisual();
        CaptureMouse();
        return true;
    }

    private void UpdateSelectionMovePreview(Point pos)
    {
        var scrollRequest = CalculateAutofillEdgeScrollIntent(
            pos.X,
            pos.Y,
            GetLogicalViewportWidth(),
            GetLogicalViewportHeight(),
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight);
        if (scrollRequest.HasAnyDirection)
            AutofillEdgeScrollRequested?.Invoke(scrollRequest);

        if (Viewport is null || _selectionMoveSourceRange is not { } source)
            return;

        if (HitTestViewportCell(Viewport, source.Start.Sheet, pos) is not { } currentCell)
            return;

        if (GridSelectionMovePlanner.CalculateTargetRange(
                source,
                _selectionMoveStartCell,
                currentCell) is not { } targetRange)
        {
            return;
        }

        if (_selectionMovePreviewRange == targetRange)
            return;

        _selectionMovePreviewRange = targetRange;
        InvalidateVisual();
    }

}
