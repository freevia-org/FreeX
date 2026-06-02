using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private const double MinimumShapeObjectWidth = 8.0;
    private const double MinimumShapeObjectHeight = 8.0;
    private const double MinimumPictureObjectWidth = 24.0;
    private const double MinimumPictureObjectHeight = 18.0;
    private const double MinimumTextBoxObjectWidth = 24.0;
    private const double MinimumTextBoxObjectHeight = 18.0;

    private const double HandleSize = 8.0;
    private const double HandleHitPad = 4.0;

    private const double RotationGripDiameter = 10.0;
    private const double PictureCropHandleSize = GridPictureCropPlanner.DefaultHandleSize;

    private static readonly Brush HandleFill = new SolidColorBrush(Colors.White);
    private static readonly Pen HandlePen = new(new SolidColorBrush(Color.FromRgb(0x20, 0x7A, 0xC5)), 1.0);
    private static readonly Pen SelectionBorderPen = new(new SolidColorBrush(Color.FromRgb(0x20, 0x7A, 0xC5)), 1.5);
    private static readonly Brush RotationGripFill = new SolidColorBrush(Colors.White);
    private static readonly Pen RotationGripPen = new(new SolidColorBrush(Color.FromRgb(0x20, 0x7A, 0xC5)), 1.0);
    private static readonly Brush PictureCropHandleFill = new SolidColorBrush(Colors.Black);
    private static readonly Pen PictureCropHandlePen = new(new SolidColorBrush(Colors.White), 1.0);
    private static readonly Brush PictureCropPreviewFill;
    private static readonly Pen PictureCropPreviewPen;

    static GridView()
    {
        HandleFill.Freeze();
        ((SolidColorBrush)((Pen)HandlePen).Brush).Freeze();
        HandlePen.Freeze();
        ((SolidColorBrush)((Pen)SelectionBorderPen).Brush).Freeze();
        SelectionBorderPen.Freeze();
        RotationGripFill.Freeze();
        ((SolidColorBrush)((Pen)RotationGripPen).Brush).Freeze();
        RotationGripPen.Freeze();
        PictureCropHandleFill.Freeze();
        ((SolidColorBrush)((Pen)PictureCropHandlePen).Brush).Freeze();
        PictureCropHandlePen.Freeze();

        var dragFillBrush = new SolidColorBrush(Color.FromArgb(40, 0x20, 0x7A, 0xC5));
        dragFillBrush.Freeze();
        DragPreviewFill = dragFillBrush;

        var dragPenBrush = new SolidColorBrush(Color.FromRgb(0x20, 0x7A, 0xC5));
        dragPenBrush.Freeze();
        DragPreviewPen = new Pen(dragPenBrush, 1.5) { DashStyle = DashStyles.Dash };
        DragPreviewPen.Freeze();

        var cropPreviewFill = new SolidColorBrush(Color.FromArgb(24, 0, 0, 0));
        cropPreviewFill.Freeze();
        PictureCropPreviewFill = cropPreviewFill;

        var cropPreviewPenBrush = new SolidColorBrush(Colors.Black);
        cropPreviewPenBrush.Freeze();
        PictureCropPreviewPen = new Pen(cropPreviewPenBrush, 1.5) { DashStyle = DashStyles.Dash };
        PictureCropPreviewPen.Freeze();
    }

    // Returns the Rect of the selected object if it is currently selected, else Rect.Empty
    private Rect GetSelectedObjectRect()
    {
        if (ObjectDisplayMode == GridObjectDisplayMode.Nothing ||
            SelectedObjectId == Guid.Empty ||
            SelectedObjectKind == ObjectKind.None)
        {
            return Rect.Empty;
        }

        return SelectedObjectKind switch
        {
            ObjectKind.Picture when Pictures is not null =>
                TryGetObjectRect(
                    Pictures,
                    p => p.Id == SelectedObjectId && p.IsVisible,
                    p => (p.Anchor, p.Width, p.Height),
                    MinimumPictureObjectWidth,
                    MinimumPictureObjectHeight),
            ObjectKind.Shape when DrawingShapes is not null =>
                TryGetObjectRect(
                    DrawingShapes,
                    s => s.Id == SelectedObjectId && s.IsVisible,
                    s => (s.Anchor, s.Width, s.Height),
                    MinimumShapeObjectWidth,
                    MinimumShapeObjectHeight),
            ObjectKind.TextBox when TextBoxes is not null =>
                TryGetObjectRect(
                    TextBoxes,
                    t => t.Id == SelectedObjectId && t.IsVisible,
                    t => (t.Anchor, t.Width, t.Height),
                    MinimumTextBoxObjectWidth,
                    MinimumTextBoxObjectHeight),
            _ => Rect.Empty
        };
    }

    private CellAddress? GetSelectedObjectAnchor()
    {
        if (ObjectDisplayMode == GridObjectDisplayMode.Nothing ||
            SelectedObjectId == Guid.Empty ||
            SelectedObjectKind == ObjectKind.None)
        {
            return null;
        }

        return SelectedObjectKind switch
        {
            ObjectKind.Picture when Pictures is not null =>
                TryGetObjectAnchor(Pictures, p => p.Id == SelectedObjectId && p.IsVisible, p => p.Anchor),
            ObjectKind.Shape when DrawingShapes is not null =>
                TryGetObjectAnchor(DrawingShapes, s => s.Id == SelectedObjectId && s.IsVisible, s => s.Anchor),
            ObjectKind.TextBox when TextBoxes is not null =>
                TryGetObjectAnchor(TextBoxes, t => t.Id == SelectedObjectId && t.IsVisible, t => t.Anchor),
            _ => null
        };
    }

    private Rect TryGetObjectRect<T>(
        IEnumerable<T> items,
        Func<T, bool> match,
        Func<T, (CellAddress Anchor, double Width, double Height)> props,
        double minimumWidth,
        double minimumHeight)
    {
        foreach (var item in items)
        {
            if (!match(item)) continue;
            var (anchor, width, height) = props(item);
            if (TryCreateAnchoredObjectRect(anchor, width, height, minimumWidth, minimumHeight, out var rect))
                return rect;
        }
        return Rect.Empty;
    }

    private static CellAddress? TryGetObjectAnchor<T>(IEnumerable<T> items, Func<T, bool> match, Func<T, CellAddress> anchor)
    {
        foreach (var item in items)
        {
            if (match(item))
                return anchor(item);
        }

        return null;
    }

    // The selection frame and handles stay axis-aligned around the object's
    // unrotated bounding box; body hit-testing still honors RotationDegrees.
    internal void DrawObjectSelectionHandles(DrawingContext dc, Rect r)
    {
        dc.DrawRectangle(null, SelectionBorderPen, r);

        // Office-style rotation grip: a small circle above the top-center handle with a connector line.
        var topCenter = new Point(r.Left + r.Width / 2, r.Top);
        var gripCenter = new Point(topCenter.X, r.Top - GridObjectDragPlanner.RotationGripOffset);
        dc.DrawLine(HandlePen, topCenter, new Point(gripCenter.X, gripCenter.Y + RotationGripDiameter / 2));
        dc.DrawEllipse(RotationGripFill, RotationGripPen, gripCenter, RotationGripDiameter / 2, RotationGripDiameter / 2);

        double hs = HandleSize;
        double hh = hs / 2;
        var centerX = r.Left + r.Width / 2;
        var centerY = r.Top + r.Height / 2;

        DrawObjectSelectionHandle(dc, r.Left - hh, r.Top - hh, hs);
        DrawObjectSelectionHandle(dc, centerX - hh, r.Top - hh, hs);
        DrawObjectSelectionHandle(dc, r.Right - hh, r.Top - hh, hs);
        DrawObjectSelectionHandle(dc, r.Right - hh, centerY - hh, hs);
        DrawObjectSelectionHandle(dc, r.Right - hh, r.Bottom - hh, hs);
        DrawObjectSelectionHandle(dc, centerX - hh, r.Bottom - hh, hs);
        DrawObjectSelectionHandle(dc, r.Left - hh, r.Bottom - hh, hs);
        DrawObjectSelectionHandle(dc, r.Left - hh, centerY - hh, hs);
    }

    private static void DrawObjectSelectionHandle(DrawingContext dc, double x, double y, double size) =>
        dc.DrawRectangle(HandleFill, HandlePen, new Rect(x, y, size, size));

    private ObjectDragKind HitTestObjectHandle(Point pos, Rect objRect)
        => GridObjectDragPlanner.HitTestHandle(pos, objRect, HandleSize, HandleHitPad);

    private PictureCropHandle HitTestPictureCropHandle(Point pos)
    {
        return TryGetSelectedImagePicture(out _, out var rect)
            ? GridPictureCropPlanner.HitTestHandle(pos, rect)
            : PictureCropHandle.None;
    }

    private bool TryGetSelectedImagePicture(out PictureModel? picture, out Rect rect)
    {
        picture = null;
        rect = Rect.Empty;
        if (ObjectDisplayMode == GridObjectDisplayMode.Nothing ||
            SelectedObjectId == Guid.Empty ||
            SelectedObjectKind != ObjectKind.Picture ||
            Pictures is null)
        {
            return false;
        }

        foreach (var candidate in Pictures)
        {
            if (candidate.Id != SelectedObjectId ||
                !candidate.IsVisible ||
                candidate.Kind != PictureKind.Image)
            {
                continue;
            }

            if (!TryCreateAnchoredObjectRect(
                    candidate.Anchor,
                    candidate.Width,
                    candidate.Height,
                    MinimumPictureObjectWidth,
                    MinimumPictureObjectHeight,
                    out rect))
            {
                return false;
            }

            picture = candidate;
            return true;
        }

        return false;
    }

    private static PictureCropRatios GetPictureCropRatios(PictureModel picture) =>
        new(picture.CropLeft, picture.CropTop, picture.CropRight, picture.CropBottom);

    internal void DrawSelectedPictureCropHandles(DrawingContext dc, Rect rect)
    {
        foreach (var (_, center) in GridPictureCropPlanner.GetHandleCenters(rect))
        {
            var handleRect = new Rect(
                center.X - PictureCropHandleSize / 2,
                center.Y - PictureCropHandleSize / 2,
                PictureCropHandleSize,
                PictureCropHandleSize);
            dc.DrawRectangle(PictureCropHandleFill, PictureCropHandlePen, handleRect);
        }
    }

    internal void RenderPictureCropPreview(DrawingContext dc, Rect pictureRect)
    {
        if (_pictureCropDragHandle == PictureCropHandle.None)
            return;

        var cropRect = GridPictureCropPlanner.CalculateVisibleCropRect(pictureRect, _pictureCropCurrentCrop);
        dc.DrawRectangle(PictureCropPreviewFill, PictureCropPreviewPen, cropRect);
        DrawSelectedPictureCropHandles(dc, pictureRect);
    }

    // Returns the cell address closest to the given screen coordinates (for anchor snapping)
    private CellAddress? HitTestAnchorCell(Point pos) =>
        GridObjectDragPlanner.HitTestAnchorCell(
            Viewport,
            pos,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight);

    private static readonly Brush DragPreviewFill;
    private static readonly Pen DragPreviewPen;

    internal void RenderObjectDragPreview(DrawingContext dc, Rect baseRect)
    {
        if (_objectDragKind == ObjectDragKind.Rotate)
        {
            // Preview the rotation by drawing the dashed frame rotated about the object center.
            dc.PushTransform(new RotateTransform(
                _objectRotationPreviewDegrees,
                baseRect.Left + baseRect.Width / 2,
                baseRect.Top + baseRect.Height / 2));
            dc.DrawRectangle(DragPreviewFill, DragPreviewPen, baseRect);
            dc.Pop();
            return;
        }

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
    private double _objectRotationPreviewDegrees;

    private static Cursor ObjectDragCursor(ObjectDragKind kind) => kind switch
    {
        ObjectDragKind.Move      => Cursors.SizeAll,
        ObjectDragKind.ResizeNW  => Cursors.SizeNWSE,
        ObjectDragKind.ResizeSE  => Cursors.SizeNWSE,
        ObjectDragKind.ResizeNE  => Cursors.SizeNESW,
        ObjectDragKind.ResizeSW  => Cursors.SizeNESW,
        ObjectDragKind.ResizeN   => Cursors.SizeNS,
        ObjectDragKind.ResizeS   => Cursors.SizeNS,
        ObjectDragKind.ResizeE   => Cursors.SizeWE,
        ObjectDragKind.ResizeW   => Cursors.SizeWE,
        ObjectDragKind.Rotate    => Cursors.Cross,
        _ => Cursors.Arrow
    };

    private static Cursor PictureCropCursor(PictureCropHandle handle) => handle switch
    {
        PictureCropHandle.CropNW => Cursors.SizeNWSE,
        PictureCropHandle.CropSE => Cursors.SizeNWSE,
        PictureCropHandle.CropNE => Cursors.SizeNESW,
        PictureCropHandle.CropSW => Cursors.SizeNESW,
        PictureCropHandle.CropN  => Cursors.SizeNS,
        PictureCropHandle.CropS  => Cursors.SizeNS,
        PictureCropHandle.CropE  => Cursors.SizeWE,
        PictureCropHandle.CropW  => Cursors.SizeWE,
        _ => Cursors.Arrow
    };

    private (Guid Id, ObjectKind Kind, Rect Rect, CellAddress Anchor) HitTestDrawingObject(Point pos)
    {
        if (Viewport is null || ObjectDisplayMode == GridObjectDisplayMode.Nothing) return default;

        var metricLookups = GetRenderMetricLookups(Viewport);
        if (TextBoxes is not null)
            for (var i = TextBoxes.Count - 1; i >= 0; i--)
            {
                var t = TextBoxes[i];
                if (t.IsVisible &&
                    TryCreateAnchoredObjectRect(
                        metricLookups,
                        t.Anchor,
                        t.Width,
                        t.Height,
                        MinimumTextBoxObjectWidth,
                        MinimumTextBoxObjectHeight,
                        out var r) &&
                    ContainsRotatedInclusive(r, pos, t.RotationDegrees))
                {
                    return (t.Id, ObjectKind.TextBox, r, t.Anchor);
                }
            }

        if (Pictures is not null)
            for (var i = Pictures.Count - 1; i >= 0; i--)
            {
                var p = Pictures[i];
                if (p.IsVisible &&
                    TryCreateAnchoredObjectRect(
                        metricLookups,
                        p.Anchor,
                        p.Width,
                        p.Height,
                        MinimumPictureObjectWidth,
                        MinimumPictureObjectHeight,
                        out var r) &&
                    ContainsRotatedInclusive(r, pos, p.RotationDegrees))
                {
                    return (p.Id, ObjectKind.Picture, r, p.Anchor);
                }
            }

        if (DrawingShapes is not null)
            for (var i = DrawingShapes.Count - 1; i >= 0; i--)
            {
                var s = DrawingShapes[i];
                if (s.IsVisible &&
                    TryCreateAnchoredObjectRect(
                        metricLookups,
                        s.Anchor,
                        s.Width,
                        s.Height,
                        MinimumShapeObjectWidth,
                        MinimumShapeObjectHeight,
                        out var r) &&
                    ContainsRotatedInclusive(r, pos, s.RotationDegrees))
                {
                    return (s.Id, ObjectKind.Shape, r, s.Anchor);
                }
            }

        return default;
    }

    private static bool ContainsRotatedInclusive(Rect rect, Point pos, double rotationDegrees)
    {
        if (Math.Abs(rotationDegrees) <= 0.0001)
            return ContainsInclusive(rect, pos);

        var radians = -rotationDegrees * Math.PI / 180.0;
        var centerX = rect.Left + rect.Width / 2.0;
        var centerY = rect.Top + rect.Height / 2.0;
        var dx = pos.X - centerX;
        var dy = pos.Y - centerY;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var local = new Point(
            centerX + dx * cos - dy * sin,
            centerY + dx * sin + dy * cos);

        return ContainsInclusive(rect, local);
    }

    private static bool ContainsInclusive(Rect rect, Point pos) =>
        pos.X >= rect.Left &&
        pos.X <= rect.Right &&
        pos.Y >= rect.Top &&
        pos.Y <= rect.Bottom;
}
