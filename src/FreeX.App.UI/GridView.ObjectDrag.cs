using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Presentation.DrawingInteraction;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private static readonly double MinimumShapeObjectWidth =
        DrawingObjectMinimumSizePlanner.MinimumWidth(DrawingObjectMinimumSizeKind.Shape);
    private static readonly double MinimumShapeObjectHeight =
        DrawingObjectMinimumSizePlanner.MinimumHeight(DrawingObjectMinimumSizeKind.Shape);
    private static readonly double MinimumPictureObjectWidth =
        DrawingObjectMinimumSizePlanner.MinimumWidth(DrawingObjectMinimumSizeKind.Picture);
    private static readonly double MinimumPictureObjectHeight =
        DrawingObjectMinimumSizePlanner.MinimumHeight(DrawingObjectMinimumSizeKind.Picture);
    private static readonly double MinimumTextBoxObjectWidth =
        DrawingObjectMinimumSizePlanner.MinimumWidth(DrawingObjectMinimumSizeKind.TextBox);
    private static readonly double MinimumTextBoxObjectHeight =
        DrawingObjectMinimumSizePlanner.MinimumHeight(DrawingObjectMinimumSizeKind.TextBox);
    private static readonly double MinimumChartObjectWidth =
        DrawingObjectMinimumSizePlanner.MinimumWidth(DrawingObjectMinimumSizeKind.Chart);
    private static readonly double MinimumChartObjectHeight =
        DrawingObjectMinimumSizePlanner.MinimumHeight(DrawingObjectMinimumSizeKind.Chart);

    private const double HandleSize = 8.0;
    private const double HandleHitPad = 4.0;

    private const double RotationGripDiameter = 10.0;

    private static readonly Brush HandleFill = new SolidColorBrush(Colors.White);
    private static readonly Pen HandlePen = new(new SolidColorBrush(Color.FromRgb(0x20, 0x7A, 0xC5)), 1.0);
    private static readonly Pen SelectionBorderPen = new(new SolidColorBrush(Color.FromRgb(0x20, 0x7A, 0xC5)), 1.5);
    private static readonly Brush RotationGripFill = new SolidColorBrush(Colors.White);
    private static readonly Pen RotationGripPen = new(new SolidColorBrush(Color.FromRgb(0x20, 0x7A, 0xC5)), 1.0);
    private static readonly Pen RotationGlyphPen = new(new SolidColorBrush(Color.FromRgb(0x20, 0x7A, 0xC5)), 1.5);
    private static readonly Pen PictureCropFramePen = new(new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)), 1.5);
    private static readonly Pen PictureCropHandlePen = new(new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)), 3.0);
    private static readonly Brush PictureCropDimBrush = new SolidColorBrush(Color.FromArgb(44, 0, 0, 0));

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
        ((SolidColorBrush)((Pen)RotationGlyphPen).Brush).Freeze();
        RotationGlyphPen.Freeze();
        ((SolidColorBrush)((Pen)PictureCropFramePen).Brush).Freeze();
        PictureCropFramePen.Freeze();
        ((SolidColorBrush)((Pen)PictureCropHandlePen).Brush).Freeze();
        PictureCropHandlePen.Freeze();
        PictureCropDimBrush.Freeze();

        var dragFillBrush = new SolidColorBrush(Color.FromArgb(40, 0x20, 0x7A, 0xC5));
        dragFillBrush.Freeze();
        DragPreviewFill = dragFillBrush;

        var dragPenBrush = new SolidColorBrush(Color.FromRgb(0x20, 0x7A, 0xC5));
        dragPenBrush.Freeze();
        DragPreviewPen = new Pen(dragPenBrush, 1.5) { DashStyle = DashStyles.Dash };
        DragPreviewPen.Freeze();

        // Seed the grid's chrome (headers/gridlines/selection) with whatever High Contrast state
        // is ALREADY active when GridView is first used, matching how App.xaml's SystemColors
        // overrides are read once at XAML-parse time. Live toggles thereafter are handled per
        // GridView instance via the SystemParameters.StaticPropertyChanged subscription set up in
        // the instance constructor (GridView.cs). See ApplyHighContrastChromePalette.
        RefreshHighContrastChromePalette();
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
                    p => (p.Anchor, p.Width, p.Height, p.AnchorOffsetX, p.AnchorOffsetY),
                    MinimumPictureObjectWidth,
                    MinimumPictureObjectHeight),
            ObjectKind.Shape when DrawingShapes is not null =>
                TryGetObjectRect(
                    DrawingShapes,
                    s => s.Id == SelectedObjectId && s.IsVisible,
                    s => (s.Anchor, s.Width, s.Height, s.AnchorOffsetX, s.AnchorOffsetY),
                    MinimumShapeObjectWidth,
                    MinimumShapeObjectHeight),
            ObjectKind.TextBox when TextBoxes is not null =>
                TryGetObjectRect(
                    TextBoxes,
                    t => t.Id == SelectedObjectId && t.IsVisible,
                    t => (t.Anchor, t.Width, t.Height, t.AnchorOffsetX, t.AnchorOffsetY),
                    MinimumTextBoxObjectWidth,
                    MinimumTextBoxObjectHeight),
            ObjectKind.Chart when Charts is not null =>
                TryGetChartRect(
                    Charts,
                    c => c.Id == SelectedObjectId && c.IsVisible),
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
            ObjectKind.Chart when Charts is not null =>
                TryGetChartAnchor(Charts, c => c.Id == SelectedObjectId && c.IsVisible),
            _ => null
        };
    }

    private double GetSelectedObjectRotationDegrees()
    {
        if (ObjectDisplayMode == GridObjectDisplayMode.Nothing ||
            SelectedObjectId == Guid.Empty ||
            SelectedObjectKind == ObjectKind.None)
        {
            return 0;
        }

        return SelectedObjectKind switch
        {
            ObjectKind.Picture when Pictures is not null =>
                TryGetObjectRotation(Pictures, p => p.Id == SelectedObjectId && p.IsVisible, p => p.RotationDegrees),
            ObjectKind.Shape when DrawingShapes is not null =>
                TryGetObjectRotation(DrawingShapes, s => s.Id == SelectedObjectId && s.IsVisible, s => s.RotationDegrees),
            ObjectKind.TextBox when TextBoxes is not null =>
                TryGetObjectRotation(TextBoxes, t => t.Id == SelectedObjectId && t.IsVisible, t => t.RotationDegrees),
            _ => 0
        };
    }

    private DrawingObjectFlipState GetSelectedObjectFlipState()
    {
        if (ObjectDisplayMode == GridObjectDisplayMode.Nothing ||
            SelectedObjectId == Guid.Empty ||
            SelectedObjectKind == ObjectKind.None)
        {
            return default;
        }

        return SelectedObjectKind switch
        {
            ObjectKind.Picture when Pictures is not null =>
                TryGetObjectFlip(Pictures, p => p.Id == SelectedObjectId && p.IsVisible, p => (p.FlipHorizontal, p.FlipVertical)),
            ObjectKind.Shape when DrawingShapes is not null =>
                TryGetObjectFlip(DrawingShapes, s => s.Id == SelectedObjectId && s.IsVisible, s => (s.FlipHorizontal, s.FlipVertical)),
            ObjectKind.TextBox when TextBoxes is not null =>
                TryGetObjectFlip(TextBoxes, t => t.Id == SelectedObjectId && t.IsVisible, t => (t.FlipHorizontal, t.FlipVertical)),
            _ => default
        };
    }

    private Rect TryGetObjectRect<T>(
        IEnumerable<T> items,
        Func<T, bool> match,
        Func<T, (CellAddress Anchor, double Width, double Height, double OffsetX, double OffsetY)> props,
        double minimumWidth,
        double minimumHeight)
    {
        foreach (var item in items)
        {
            if (!match(item)) continue;
            var (anchor, width, height, offsetX, offsetY) = props(item);
            if (TryCreateAnchoredObjectRect(anchor, width, height, minimumWidth, minimumHeight, out var rect, offsetX, offsetY))
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

    private Rect TryGetChartRect(
        IEnumerable<ChartModel> charts,
        Func<ChartModel, bool> match)
    {
        foreach (var chart in charts)
        {
            if (match(chart))
                return CreateChartRect(chart);
        }

        return Rect.Empty;
    }

    private CellAddress? TryGetChartAnchor(
        IEnumerable<ChartModel> charts,
        Func<ChartModel, bool> match)
    {
        foreach (var chart in charts)
        {
            if (match(chart))
                return GetChartAnchor(chart);
        }

        return null;
    }

    private Rect CreateChartRect(ChartModel chart) =>
        new(
            chart.Left + ActualRowHeaderWidth,
            chart.Top + EffectiveColHeaderHeight,
            Math.Max(MinimumChartObjectWidth, chart.Width),
            Math.Max(MinimumChartObjectHeight, chart.Height));

    private CellAddress GetChartAnchor(ChartModel chart)
    {
        if (HitTestAnchorCell(new Point(chart.Left + ActualRowHeaderWidth, chart.Top + EffectiveColHeaderHeight)) is { } anchor)
            return new CellAddress(chart.DataRange.Start.Sheet, anchor.Row, anchor.Col);

        return chart.DataRange.Start;
    }

    private static double TryGetObjectRotation<T>(IEnumerable<T> items, Func<T, bool> match, Func<T, double> rotation)
    {
        foreach (var item in items)
        {
            if (match(item))
                return rotation(item);
        }

        return 0;
    }

    private static DrawingObjectFlipState TryGetObjectFlip<T>(
        IEnumerable<T> items,
        Func<T, bool> match,
        Func<T, (bool Horizontal, bool Vertical)> flip)
    {
        foreach (var item in items)
        {
            if (!match(item))
                continue;

            var state = flip(item);
            return new DrawingObjectFlipState(state.Horizontal, state.Vertical);
        }

        return default;
    }

    internal void DrawObjectSelectionHandles(DrawingContext dc, Rect r, double rotationDegrees)
    {
        var rotated = Math.Abs(rotationDegrees) > 0.0001;
        if (rotated)
        {
            dc.PushTransform(new RotateTransform(
                rotationDegrees,
                r.Left + r.Width / 2,
                r.Top + r.Height / 2));
        }

        dc.DrawRectangle(null, SelectionBorderPen, r);

        var topCenter = new Point(r.Left + r.Width / 2, r.Top);
        var gripCenter = new Point(topCenter.X, r.Top - GridObjectDragPlanner.RotationGripOffset);
        dc.DrawLine(HandlePen, topCenter, new Point(gripCenter.X, gripCenter.Y + RotationGripDiameter / 2));
        DrawRotationGrip(dc, gripCenter);

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

        if (rotated)
            dc.Pop();
    }

    private static void DrawObjectSelectionHandle(DrawingContext dc, double x, double y, double size) =>
        dc.DrawRectangle(HandleFill, HandlePen, new Rect(x, y, size, size));

    private static void DrawRotationGrip(DrawingContext dc, Point center)
    {
        var radius = RotationGripDiameter / 2;
        dc.DrawEllipse(RotationGripFill, RotationGripPen, center, radius, radius);

        var glyph = new StreamGeometry();
        using (var context = glyph.Open())
        {
            var arcRadius = radius - 2.5;
            var start = new Point(center.X - arcRadius * 0.65, center.Y + arcRadius * 0.75);
            var end = new Point(center.X + arcRadius * 0.85, center.Y - arcRadius * 0.35);
            context.BeginFigure(start, isFilled: false, isClosed: false);
            context.ArcTo(
                end,
                new Size(arcRadius, arcRadius),
                rotationAngle: 0,
                isLargeArc: true,
                sweepDirection: SweepDirection.Clockwise,
                isStroked: true,
                isSmoothJoin: true);

            context.BeginFigure(end, isFilled: true, isClosed: true);
            context.LineTo(new Point(end.X - 0.5, end.Y + 3.0), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(end.X + 2.7, end.Y + 1.2), isStroked: true, isSmoothJoin: false);
        }

        glyph.Freeze();
        dc.DrawGeometry(null, RotationGlyphPen, glyph);
    }

    private ObjectDragKind HitTestObjectHandle(Point pos, Rect objRect)
        => GridObjectDragPlanner.HitTestHandle(
            pos,
            objRect,
            HandleSize,
            HandleHitPad,
            GetSelectedObjectRotationDegrees());

    private PictureCropHandle HitTestPictureCropHandle(Point pos, Rect objRect)
    {
        var localPos = TransformPointToUnrotatedObjectSpace(
            objRect,
            pos,
            GetSelectedObjectRotationDegrees());
        return GridPictureCropPlanner.HitTestHandle(localPos, objRect);
    }

    private static Point TransformPointToUnrotatedObjectSpace(Rect rect, Point pos, double rotationDegrees)
    {
        if (rect.IsEmpty || Math.Abs(rotationDegrees) <= 0.0001)
            return pos;

        var radians = -rotationDegrees * Math.PI / 180.0;
        var centerX = rect.Left + rect.Width / 2.0;
        var centerY = rect.Top + rect.Height / 2.0;
        var dx = pos.X - centerX;
        var dy = pos.Y - centerY;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new Point(
            centerX + dx * cos - dy * sin,
            centerY + dx * sin + dy * cos);
    }

    internal void DrawPictureCropHandles(DrawingContext dc, Rect r, PictureCropRatios crop, double rotationDegrees)
    {
        var rotated = Math.Abs(rotationDegrees) > 0.0001;
        if (rotated)
        {
            dc.PushTransform(new RotateTransform(
                rotationDegrees,
                r.Left + r.Width / 2,
                r.Top + r.Height / 2));
        }

        var visibleRect = GridPictureCropPlanner.CalculateVisibleCropRect(r, crop);
        DrawPictureCropDimmedEdges(dc, r, visibleRect);
        dc.DrawRectangle(null, PictureCropFramePen, r);

        foreach (var (handle, center) in GridPictureCropPlanner.GetHandleCenters(r))
            DrawPictureCropHandle(dc, handle, center);

        if (rotated)
            dc.Pop();
    }

    private static void DrawPictureCropDimmedEdges(DrawingContext dc, Rect frame, Rect visible)
    {
        if (visible.IsEmpty || visible.Width <= 0 || visible.Height <= 0)
            return;

        if (visible.Top > frame.Top)
            dc.DrawRectangle(PictureCropDimBrush, null, new Rect(frame.Left, frame.Top, frame.Width, visible.Top - frame.Top));
        if (visible.Bottom < frame.Bottom)
            dc.DrawRectangle(PictureCropDimBrush, null, new Rect(frame.Left, visible.Bottom, frame.Width, frame.Bottom - visible.Bottom));
        if (visible.Left > frame.Left)
            dc.DrawRectangle(PictureCropDimBrush, null, new Rect(frame.Left, visible.Top, visible.Left - frame.Left, visible.Height));
        if (visible.Right < frame.Right)
            dc.DrawRectangle(PictureCropDimBrush, null, new Rect(visible.Right, visible.Top, frame.Right - visible.Right, visible.Height));
    }

    private static void DrawPictureCropHandle(DrawingContext dc, PictureCropHandle handle, Point center)
    {
        const double length = 13.0;
        const double half = length / 2.0;

        switch (handle)
        {
            case PictureCropHandle.CropNW:
            case PictureCropHandle.CropNE:
            case PictureCropHandle.CropSE:
            case PictureCropHandle.CropSW:
                dc.DrawLine(PictureCropHandlePen, new Point(center.X - half, center.Y), new Point(center.X + half, center.Y));
                dc.DrawLine(PictureCropHandlePen, new Point(center.X, center.Y - half), new Point(center.X, center.Y + half));
                break;
            case PictureCropHandle.CropN:
            case PictureCropHandle.CropS:
                dc.DrawLine(PictureCropHandlePen, new Point(center.X - half, center.Y), new Point(center.X + half, center.Y));
                break;
            case PictureCropHandle.CropE:
            case PictureCropHandle.CropW:
                dc.DrawLine(PictureCropHandlePen, new Point(center.X, center.Y - half), new Point(center.X, center.Y + half));
                break;
        }
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

    private bool HasLiveObjectTransformPreview() =>
        _objectDragKind != ObjectDragKind.None &&
        _selectedObjectId != Guid.Empty &&
        _selectedObjectKind switch
        {
            ObjectKind.None => false,
            ObjectKind.Chart => _objectDragKind != ObjectDragKind.Rotate,
            _ => true
        };

    private bool IsSelectedPictureCropModeActive() =>
        IsPictureCropMode &&
        SelectedObjectKind == ObjectKind.Picture &&
        SelectedObjectId != Guid.Empty &&
        TryGetSelectedImagePicture() is not null;

    private PictureModel? TryGetSelectedImagePicture()
    {
        if (ObjectDisplayMode == GridObjectDisplayMode.Nothing ||
            SelectedObjectId == Guid.Empty ||
            SelectedObjectKind != ObjectKind.Picture ||
            Pictures is null)
        {
            return null;
        }

        foreach (var picture in Pictures)
        {
            if (picture.Id == SelectedObjectId &&
                picture.IsVisible &&
                picture.Kind == PictureKind.Image)
            {
                return picture;
            }
        }

        return null;
    }

    private PictureCropRatios GetSelectedPictureCropRatios() =>
        TryGetSelectedImagePicture() is { } picture
            ? new PictureCropRatios(picture.CropLeft, picture.CropTop, picture.CropRight, picture.CropBottom)
            : default;

    private bool TryResolveLivePictureCrop(Guid id, out PictureCropRatios crop)
    {
        if (_pictureCropDragHandle != PictureCropHandle.None &&
            _pictureCropDragId == id)
        {
            crop = _pictureCropDragCurrentRatios;
            return true;
        }

        crop = default;
        return false;
    }

    private Rect GetSelectedObjectLiveRect(Rect committedRect)
    {
        if (!HasLiveObjectTransformPreview())
            return committedRect;

        return _objectDragCurrentRect.IsEmpty ? committedRect : _objectDragCurrentRect;
    }

    private double GetSelectedObjectLiveRotationDegrees(double committedRotationDegrees) =>
        HasLiveObjectTransformPreview() && _objectDragKind == ObjectDragKind.Rotate
            ? _objectRotationPreviewDegrees
            : committedRotationDegrees;

    private DrawingObjectFlipState GetSelectedObjectLiveFlipState(bool committedFlipHorizontal, bool committedFlipVertical) =>
        HasLiveObjectTransformPreview()
            ? new DrawingObjectFlipState(_objectDragCurrentFlipHorizontal, _objectDragCurrentFlipVertical)
            : new DrawingObjectFlipState(committedFlipHorizontal, committedFlipVertical);

    private bool TryResolveLiveObjectTransform(
        Guid id,
        ObjectKind kind,
        Rect committedRect,
        double committedRotationDegrees,
        bool committedFlipHorizontal,
        bool committedFlipVertical,
        out Rect renderRect,
        out double renderRotationDegrees,
        out bool renderFlipHorizontal,
        out bool renderFlipVertical)
    {
        renderRect = committedRect;
        renderRotationDegrees = committedRotationDegrees;
        renderFlipHorizontal = committedFlipHorizontal;
        renderFlipVertical = committedFlipVertical;

        if (!HasLiveObjectTransformPreview() ||
            id != _selectedObjectId ||
            kind != _selectedObjectKind)
        {
            return false;
        }

        renderRect = GetSelectedObjectLiveRect(committedRect);
        renderRotationDegrees = GetSelectedObjectLiveRotationDegrees(committedRotationDegrees);
        var flipState = GetSelectedObjectLiveFlipState(committedFlipHorizontal, committedFlipVertical);
        renderFlipHorizontal = flipState.Horizontal;
        renderFlipVertical = flipState.Vertical;
        return true;
    }

    private Rect _objectDragCurrentRect;
    private double _objectRotationPreviewDegrees;
    private bool _objectDragStartFlipHorizontal;
    private bool _objectDragStartFlipVertical;
    private bool _objectDragCurrentFlipHorizontal;
    private bool _objectDragCurrentFlipVertical;

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
        PictureCropHandle.CropN => Cursors.SizeNS,
        PictureCropHandle.CropS => Cursors.SizeNS,
        PictureCropHandle.CropE => Cursors.SizeWE,
        PictureCropHandle.CropW => Cursors.SizeWE,
        _ => Cursors.Arrow
    };

    private (Guid Id, ObjectKind Kind, Rect Rect, CellAddress Anchor) HitTestDrawingObject(Point pos)
    {
        if (Viewport is null || ObjectDisplayMode == GridObjectDisplayMode.Nothing) return default;

        var metricLookups = GetRenderMetricLookups(Viewport);
        if (HasExplicitDrawingObjectZOrder())
        {
            var order = GetNormalizedDrawingObjectZOrder();
            for (var index = order.Count - 1; index >= 0; index--)
            {
                var entry = order[index];
                if (entry.Kind == SelectionPaneObjectKind.TextBox &&
                    FindTextBox(entry.Id) is { } textBox &&
                    TryHitTextBox(metricLookups, textBox, pos, out var textBoxHit))
                {
                    return textBoxHit;
                }

                if (entry.Kind == SelectionPaneObjectKind.Picture &&
                    FindPicture(entry.Id) is { } picture &&
                    TryHitPicture(metricLookups, picture, pos, out var pictureHit))
                {
                    return pictureHit;
                }

                if (entry.Kind == SelectionPaneObjectKind.Shape &&
                    FindDrawingShape(entry.Id) is { } shape &&
                    TryHitDrawingShape(metricLookups, shape, pos, out var shapeHit))
                {
                    return shapeHit;
                }

                if (entry.Kind == SelectionPaneObjectKind.Chart &&
                    FindChart(entry.Id) is { } chart &&
                    TryHitChart(chart, pos, out var orderedChartHit))
                {
                    return orderedChartHit;
                }
            }

            return default;
        }

        if (TextBoxes is not null)
            for (var i = TextBoxes.Count - 1; i >= 0; i--)
            {
                if (TryHitTextBox(metricLookups, TextBoxes[i], pos, out var hit))
                    return hit;
            }

        if (Pictures is not null)
            for (var i = Pictures.Count - 1; i >= 0; i--)
            {
                if (TryHitPicture(metricLookups, Pictures[i], pos, out var hit))
                    return hit;
            }

        if (DrawingShapes is not null)
            for (var i = DrawingShapes.Count - 1; i >= 0; i--)
            {
                if (TryHitDrawingShape(metricLookups, DrawingShapes[i], pos, out var hit))
                    return hit;
            }

        if (TryHitCharts(pos, out var chartHit))
            return chartHit;

        return default;
    }

    private bool TryHitCharts(
        Point pos,
        out (Guid Id, ObjectKind Kind, Rect Rect, CellAddress Anchor) hit)
    {
        if (Charts is not null)
        {
            for (var i = Charts.Count - 1; i >= 0; i--)
            {
                if (TryHitChart(Charts[i], pos, out hit))
                    return true;
            }
        }

        hit = default;
        return false;
    }

    private bool TryHitTextBox(
        RenderMetricLookupCache metricLookups,
        TextBoxModel textBox,
        Point pos,
        out (Guid Id, ObjectKind Kind, Rect Rect, CellAddress Anchor) hit)
    {
        if (textBox.IsVisible &&
            TryCreateAnchoredObjectRect(
                metricLookups,
                textBox.Anchor,
                textBox.Width,
                textBox.Height,
                MinimumTextBoxObjectWidth,
                MinimumTextBoxObjectHeight,
                out var rect,
                textBox.AnchorOffsetX,
                textBox.AnchorOffsetY) &&
            ContainsRotatedInclusive(rect, pos, textBox.RotationDegrees))
        {
            hit = (textBox.Id, ObjectKind.TextBox, rect, textBox.Anchor);
            return true;
        }

        hit = default;
        return false;
    }

    private bool TryHitPicture(
        RenderMetricLookupCache metricLookups,
        PictureModel picture,
        Point pos,
        out (Guid Id, ObjectKind Kind, Rect Rect, CellAddress Anchor) hit)
    {
        if (picture.IsVisible &&
            TryCreateAnchoredObjectRect(
                metricLookups,
                picture.Anchor,
                picture.Width,
                picture.Height,
                MinimumPictureObjectWidth,
                MinimumPictureObjectHeight,
                out var rect,
                picture.AnchorOffsetX,
                picture.AnchorOffsetY) &&
            ContainsRotatedInclusive(rect, pos, picture.RotationDegrees))
        {
            hit = (picture.Id, ObjectKind.Picture, rect, picture.Anchor);
            return true;
        }

        hit = default;
        return false;
    }

    private bool TryHitDrawingShape(
        RenderMetricLookupCache metricLookups,
        DrawingShapeModel shape,
        Point pos,
        out (Guid Id, ObjectKind Kind, Rect Rect, CellAddress Anchor) hit)
    {
        if (shape.IsVisible &&
            TryCreateAnchoredObjectRect(
                metricLookups,
                shape.Anchor,
                shape.Width,
                shape.Height,
                MinimumShapeObjectWidth,
                MinimumShapeObjectHeight,
                out var rect,
                shape.AnchorOffsetX,
                shape.AnchorOffsetY) &&
            ContainsRotatedInclusive(rect, pos, shape.RotationDegrees))
        {
            hit = (shape.Id, ObjectKind.Shape, rect, shape.Anchor);
            return true;
        }

        hit = default;
        return false;
    }

    private bool TryHitChart(
        ChartModel chart,
        Point pos,
        out (Guid Id, ObjectKind Kind, Rect Rect, CellAddress Anchor) hit)
    {
        if (chart.IsVisible)
        {
            var rect = CreateChartRect(chart);
            if (ContainsInclusive(rect, pos))
            {
                hit = (chart.Id, ObjectKind.Chart, rect, GetChartAnchor(chart));
                return true;
            }
        }

        hit = default;
        return false;
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

    private readonly record struct DrawingObjectFlipState(bool Horizontal, bool Vertical);
}
