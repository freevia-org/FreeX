using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FreeX.App.UI;

public partial class GridView
{
    public bool IsTextBoxPlacementPending => _pendingTextBoxPlacement;

    public void BeginTextBoxPlacement()
    {
        CancelShapePlacement();
        _pendingTextBoxPlacement = true;
        _textBoxPlacementDragging = false;
        _textBoxPlacementPreviewRect = Rect.Empty;
        SelectedObjectId = Guid.Empty;
        SelectedObjectKind = ObjectKind.None;
        _selectedObjectId = Guid.Empty;
        _selectedObjectKind = ObjectKind.None;
        Cursor = Cursors.Cross;
        Focus();
        InvalidateVisual();
    }

    public void CancelTextBoxPlacement()
    {
        if (!_pendingTextBoxPlacement && !_textBoxPlacementDragging)
            return;

        var releaseCapture = _textBoxPlacementDragging && IsMouseCaptured;
        _pendingTextBoxPlacement = false;
        _textBoxPlacementDragging = false;
        _textBoxPlacementPreviewRect = Rect.Empty;
        Cursor = null;
        if (releaseCapture)
            ReleaseMouseCapture();
        InvalidateVisual();
    }

    private bool TryBeginTextBoxPlacement(Point position)
    {
        if (!_pendingTextBoxPlacement)
            return false;

        if (HitTestAnchorCell(position) is not { } anchor)
            return false;

        _textBoxPlacementDragging = true;
        _textBoxPlacementStartAnchor = anchor;
        _textBoxPlacementStartPos = position;
        _textBoxPlacementPreviewRect = GridShapePlacementPlanner.CalculatePreviewRect(position, position);
        Cursor = Cursors.Cross;
        CaptureMouse();
        InvalidateVisual();
        return true;
    }

    private void UpdateTextBoxPlacementPreview(Point position)
    {
        _textBoxPlacementPreviewRect = GridShapePlacementPlanner.CalculatePreviewRect(_textBoxPlacementStartPos, position);
        Cursor = Cursors.Cross;
        InvalidateVisual();
    }

    private void CommitTextBoxPlacement(Point position)
    {
        var previewRect = GridShapePlacementPlanner.CalculatePreviewRect(_textBoxPlacementStartPos, position);
        var anchorPoint = GridShapePlacementPlanner.IsMeaningfulDrag(_textBoxPlacementStartPos, position)
            ? previewRect.TopLeft
            : _textBoxPlacementStartPos;
        var anchor = HitTestAnchorCell(anchorPoint) ?? _textBoxPlacementStartAnchor;
        var request = GridTextBoxPlacementPlanner.CreateRequest(anchor, _textBoxPlacementStartPos, position);

        _pendingTextBoxPlacement = false;
        _textBoxPlacementDragging = false;
        _textBoxPlacementPreviewRect = Rect.Empty;
        Cursor = null;
        ReleaseMouseCapture();
        InvalidateVisual();

        TextBoxPlacementRequested?.Invoke(request);
    }

    private void CancelCapturedTextBoxPlacement()
    {
        if (!_textBoxPlacementDragging)
            return;

        _pendingTextBoxPlacement = false;
        _textBoxPlacementDragging = false;
        _textBoxPlacementPreviewRect = Rect.Empty;
        Cursor = null;
        InvalidateVisual();
    }

    internal void RenderTextBoxPlacementPreview(DrawingContext dc)
    {
        if (!_textBoxPlacementDragging || _textBoxPlacementPreviewRect.IsEmpty)
            return;

        dc.DrawRectangle(DragPreviewFill, DragPreviewPen, _textBoxPlacementPreviewRect);
    }
}
