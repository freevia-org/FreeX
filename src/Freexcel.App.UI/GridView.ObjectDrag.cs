using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public partial class GridView
{
    private const double HandleSize = 8.0;
    private const double HandleHitPad = 4.0;

    private static readonly Brush HandleFill = new SolidColorBrush(Colors.White);
    private static readonly Pen HandlePen = new(new SolidColorBrush(Color.FromRgb(0x20, 0x7A, 0xC5)), 1.0);
    private static readonly Pen SelectionBorderPen = new(new SolidColorBrush(Color.FromRgb(0x20, 0x7A, 0xC5)), 1.5);

    static GridView()
    {
        HandleFill.Freeze();
        ((SolidColorBrush)((Pen)HandlePen).Brush).Freeze();
        HandlePen.Freeze();
        ((SolidColorBrush)((Pen)SelectionBorderPen).Brush).Freeze();
        SelectionBorderPen.Freeze();

        var dragFillBrush = new SolidColorBrush(Color.FromArgb(40, 0x20, 0x7A, 0xC5));
        dragFillBrush.Freeze();
        DragPreviewFill = dragFillBrush;

        var dragPenBrush = new SolidColorBrush(Color.FromRgb(0x20, 0x7A, 0xC5));
        dragPenBrush.Freeze();
        DragPreviewPen = new Pen(dragPenBrush, 1.5) { DashStyle = DashStyles.Dash };
        DragPreviewPen.Freeze();
    }

    // Returns the Rect of the selected object if it is currently selected, else Rect.Empty
    private Rect GetSelectedObjectRect()
    {
        if (SelectedObjectId == Guid.Empty || SelectedObjectKind == ObjectKind.None)
            return Rect.Empty;

        return SelectedObjectKind switch
        {
            ObjectKind.Picture when Pictures is not null =>
                TryGetObjectRect(Pictures, p => p.Id == SelectedObjectId, p => (p.Anchor, p.Width, p.Height)),
            ObjectKind.Shape when DrawingShapes is not null =>
                TryGetObjectRect(DrawingShapes, s => s.Id == SelectedObjectId, s => (s.Anchor, s.Width, s.Height)),
            ObjectKind.TextBox when TextBoxes is not null =>
                TryGetObjectRect(TextBoxes, t => t.Id == SelectedObjectId, t => (t.Anchor, t.Width, t.Height)),
            _ => Rect.Empty
        };
    }

    private Rect TryGetObjectRect<T>(IEnumerable<T> items, Func<T, bool> match, Func<T, (CellAddress Anchor, double Width, double Height)> props)
    {
        foreach (var item in items)
        {
            if (!match(item)) continue;
            var (anchor, width, height) = props(item);
            if (TryCreateAnchoredObjectRect(anchor, width, height, 8, 8, out var rect))
                return rect;
        }
        return Rect.Empty;
    }

    internal void DrawObjectSelectionHandles(DrawingContext dc, Rect r)
    {
        dc.DrawRectangle(null, SelectionBorderPen, r);

        var handles = GetHandleRects(r);
        foreach (var h in handles)
            dc.DrawRectangle(HandleFill, HandlePen, h);
    }

    private static Rect[] GetHandleRects(Rect r)
    {
        double hs = HandleSize;
        double hh = hs / 2;
        return
        [
            new Rect(r.Left - hh,              r.Top - hh,               hs, hs), // NW
            new Rect(r.Left + r.Width / 2 - hh, r.Top - hh,              hs, hs), // N
            new Rect(r.Right - hh,              r.Top - hh,               hs, hs), // NE
            new Rect(r.Right - hh,              r.Top + r.Height / 2 - hh, hs, hs), // E
            new Rect(r.Right - hh,              r.Bottom - hh,            hs, hs), // SE
            new Rect(r.Left + r.Width / 2 - hh, r.Bottom - hh,           hs, hs), // S
            new Rect(r.Left - hh,              r.Bottom - hh,             hs, hs), // SW
            new Rect(r.Left - hh,              r.Top + r.Height / 2 - hh, hs, hs), // W
        ];
    }

    private ObjectDragKind HitTestObjectHandle(Point pos, Rect objRect)
    {
        if (objRect.IsEmpty) return ObjectDragKind.None;
        double pad = HandleHitPad + HandleSize / 2;

        bool nearRight  = Math.Abs(pos.X - objRect.Right) < pad;
        bool nearBottom = Math.Abs(pos.Y - objRect.Bottom) < pad;
        bool inVertical = pos.Y >= objRect.Top - pad && pos.Y <= objRect.Bottom + pad;
        bool inHoriz    = pos.X >= objRect.Left - pad && pos.X <= objRect.Right + pad;

        if (nearRight && nearBottom) return ObjectDragKind.ResizeSE;
        if (nearRight && inVertical) return ObjectDragKind.ResizeE;
        if (nearBottom && inHoriz)   return ObjectDragKind.ResizeS;
        if (objRect.Contains(pos))   return ObjectDragKind.Move;
        return ObjectDragKind.None;
    }

    // Returns the cell address closest to the given screen coordinates (for anchor snapping)
    private CellAddress? HitTestAnchorCell(Point pos)
    {
        if (Viewport is null) return null;
        foreach (var row in Viewport.RowMetrics)
        {
            double top = row.TopOffset + EffectiveColHeaderHeight;
            if (pos.Y < top || pos.Y >= top + row.Height) continue;
            foreach (var col in Viewport.ColMetrics)
            {
                double left = col.LeftOffset + ActualRowHeaderWidth;
                if (pos.X >= left && pos.X < left + col.Width)
                    return new CellAddress(default, row.Row, col.Col);
            }
        }
        return null;
    }

    private static readonly Brush DragPreviewFill;
    private static readonly Pen DragPreviewPen;

    internal void RenderObjectDragPreview(DrawingContext dc, Rect baseRect)
    {
        var previewRect = CalculateDragPreviewRect(baseRect);
        dc.DrawRectangle(DragPreviewFill, DragPreviewPen, previewRect);
    }

    private Rect CalculateDragPreviewRect(Rect baseRect)
    {
        if (_objectDragKind == ObjectDragKind.None) return baseRect;
        // For move: get the anchor rect of the cell under the last known mouse pos
        // For resize: adjust width/height by drag delta
        // We store the current drag rect in _objectDragCurrentRect during mouse move
        return _objectDragCurrentRect.IsEmpty ? baseRect : _objectDragCurrentRect;
    }

    private Rect _objectDragCurrentRect;

    private static Cursor ObjectDragCursor(ObjectDragKind kind) => kind switch
    {
        ObjectDragKind.Move      => Cursors.SizeAll,
        ObjectDragKind.ResizeSE  => Cursors.SizeNWSE,
        ObjectDragKind.ResizeE   => Cursors.SizeWE,
        ObjectDragKind.ResizeS   => Cursors.SizeNS,
        _ => Cursors.Arrow
    };

    private (Guid Id, ObjectKind Kind, Rect Rect) HitTestDrawingObject(Point pos)
    {
        if (Viewport is null) return default;

        if (TextBoxes is not null)
            foreach (var t in TextBoxes)
                if (t.IsVisible && TryCreateAnchoredObjectRect(t.Anchor, t.Width, t.Height, 8, 8, out var r) && r.Contains(pos))
                    return (t.Id, ObjectKind.TextBox, r);

        if (DrawingShapes is not null)
            foreach (var s in DrawingShapes)
                if (s.IsVisible && TryCreateAnchoredObjectRect(s.Anchor, s.Width, s.Height, 8, 8, out var r) && r.Contains(pos))
                    return (s.Id, ObjectKind.Shape, r);

        if (Pictures is not null)
            foreach (var p in Pictures)
                if (p.IsVisible && TryCreateAnchoredObjectRect(p.Anchor, p.Width, p.Height, 24, 18, out var r) && r.Contains(pos))
                    return (p.Id, ObjectKind.Picture, r);

        return default;
    }
}
