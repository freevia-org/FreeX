using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    public bool IsShapePlacementPending => _pendingShapePlacementKind.HasValue;

    public void BeginShapePlacement(DrawingShapeKind kind)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Drawing shape kind is not supported.");

        CancelTextBoxPlacement();
        _pendingShapePlacementKind = kind;
        _shapePlacementDragging = false;
        _shapePlacementPreviewRect = Rect.Empty;
        SelectedObjectId = Guid.Empty;
        SelectedObjectKind = ObjectKind.None;
        _selectedObjectId = Guid.Empty;
        _selectedObjectKind = ObjectKind.None;
        Cursor = Cursors.Cross;
        Focus();
        InvalidateVisual();
    }

    public void CancelShapePlacement()
    {
        if (!_pendingShapePlacementKind.HasValue && !_shapePlacementDragging)
            return;

        var releaseCapture = _shapePlacementDragging && IsMouseCaptured;
        _pendingShapePlacementKind = null;
        _shapePlacementDragging = false;
        _shapePlacementPreviewRect = Rect.Empty;
        Cursor = null;
        if (releaseCapture)
            ReleaseMouseCapture();
        InvalidateVisual();
    }

    private bool TryBeginShapePlacement(Point position)
    {
        if (_pendingShapePlacementKind is not { } kind)
            return false;

        if (HitTestAnchorCell(position) is not { } anchor)
            return false;

        _shapePlacementDragging = true;
        _shapePlacementKind = kind;
        _shapePlacementStartAnchor = anchor;
        _shapePlacementStartPos = position;
        _shapePlacementPreviewRect = GridShapePlacementPlanner.CalculatePreviewRect(position, position);
        Cursor = Cursors.Cross;
        CaptureMouse();
        InvalidateVisual();
        return true;
    }

    private void UpdateShapePlacementPreview(Point position)
    {
        _shapePlacementPreviewRect = GridShapePlacementPlanner.CalculatePreviewRect(_shapePlacementStartPos, position);
        Cursor = Cursors.Cross;
        InvalidateVisual();
    }

    private void CommitShapePlacement(Point position)
    {
        var kind = _shapePlacementKind;
        var anchorPoint = GridShapePlacementPlanner.CalculateAnchorPoint(_shapePlacementStartPos, position);
        var anchor = HitTestAnchorCell(anchorPoint) ?? _shapePlacementStartAnchor;
        var request = GridShapePlacementPlanner.CreateRequest(kind, anchor, _shapePlacementStartPos, position);

        _pendingShapePlacementKind = null;
        _shapePlacementDragging = false;
        _shapePlacementPreviewRect = Rect.Empty;
        Cursor = null;
        ReleaseMouseCapture();
        InvalidateVisual();

        ShapePlacementRequested?.Invoke(request);
    }

    private void CancelCapturedShapePlacement()
    {
        if (!_shapePlacementDragging)
            return;

        _pendingShapePlacementKind = null;
        _shapePlacementDragging = false;
        _shapePlacementPreviewRect = Rect.Empty;
        Cursor = null;
        InvalidateVisual();
    }

    internal void RenderShapePlacementPreview(DrawingContext dc)
    {
        if (!_shapePlacementDragging || _shapePlacementPreviewRect.IsEmpty)
            return;

        dc.DrawRectangle(DragPreviewFill, DragPreviewPen, _shapePlacementPreviewRect);
    }
}
