using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public readonly record struct SelectionAdornerRect(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public bool Contains(CanvasGesturePoint point) =>
        DrawingObjectInteractionPlanner.ContainsInclusive(
            ToLayoutRect(),
            new LayoutPoint(point.X, point.Y));

    internal LayoutRect ToLayoutRect() => new(Left, Top, Width, Height);
}

/// <summary>One selected shape and its rotation-aware screen-space frame.</summary>
public sealed record SelectionAdornerSelectionPlan(
    uint ShapeId,
    SelectionAdornerRect ScreenRect);

/// <summary>One crop or preset-geometry edit handle projected into screen space.</summary>
public sealed record SelectionAdornerGeometryHandlePlan(
    string Name,
    CanvasGesturePoint ScreenPosition);

/// <summary>Complete renderer-neutral selection chrome state for one refresh.</summary>
public sealed record SelectionAdornerProjectionPlan(
    IReadOnlyList<SelectionAdornerSelectionPlan> Selections,
    IReadOnlyList<SelectionAdornerGeometryHandlePlan> GeometryHandles)
{
    public static SelectionAdornerProjectionPlan Empty { get; } = new(
        Array.Empty<SelectionAdornerSelectionPlan>(),
        Array.Empty<SelectionAdornerGeometryHandlePlan>());

    public SelectionAdornerRect? SelectionBounds =>
        SelectionAdornerGeometry.GetSelectionBounds(
            Selections.Select(selection => selection.ScreenRect));
}

/// <summary>
/// Owns renderer-neutral selection chrome state while native adorners retain painting and
/// framework coordinate conversion.
/// </summary>
public sealed class SelectionAdornerState
{
    private readonly List<SelectionAdornerSelectionPlan> _selections = new();
    private readonly List<SelectionAdornerGeometryHandlePlan> _geometryHandles = new();
    private IReadOnlyList<CanvasShapeTransformPreview> _transformPreview =
        Array.Empty<CanvasShapeTransformPreview>();

    public IReadOnlyList<SelectionAdornerSelectionPlan> Selections => _selections;

    public SelectionAdornerRect? PreviewRect { get; private set; }

    public double PreviewRotationDeg { get; private set; }

    public IReadOnlyList<CanvasShapeTransformPreview> TransformPreview => _transformPreview;

    public SelectionAdornerRect? MarqueeRect { get; private set; }

    public IReadOnlyList<SnapGuideLine>? SnapGuides { get; private set; }

    public SlideTransformCore SnapTransform { get; private set; } = SlideTransformCore.Identity;

    public IReadOnlyList<SelectionAdornerGeometryHandlePlan> GeometryHandles => _geometryHandles;

    public SelectionAdornerGeometryHandlePlan? GeometryPreview { get; private set; }

    public SelectionAdornerRect? SelectionBounds =>
        SelectionAdornerGeometry.GetSelectionBounds(
            _selections.Select(selection => selection.ScreenRect));

    public bool HasTransientInteractionVisuals =>
        PreviewRect.HasValue ||
        _transformPreview.Count > 0 ||
        MarqueeRect.HasValue ||
        SnapGuides is { Count: > 0 } ||
        GeometryPreview is not null;

    public void UpdateSelection(IEnumerable<SelectionAdornerSelectionPlan> selections)
    {
        _selections.Clear();
        _selections.AddRange(selections);
        PreviewRect = null;
        _transformPreview = Array.Empty<CanvasShapeTransformPreview>();
    }

    public void UpdateGeometryHandles(IEnumerable<SelectionAdornerGeometryHandlePlan> handles)
    {
        _geometryHandles.Clear();
        _geometryHandles.AddRange(handles);
        GeometryPreview = null;
    }

    public void UpdateGeometryPreview(string? name, CanvasGesturePoint? position)
    {
        GeometryPreview = name is not null && position is { } point
            ? new SelectionAdornerGeometryHandlePlan(name, point)
            : null;
    }

    public void UpdatePreview(SelectionAdornerRect? screenRect, double rotationDeg = 0)
    {
        PreviewRect = screenRect;
        PreviewRotationDeg = rotationDeg;
        _transformPreview = Array.Empty<CanvasShapeTransformPreview>();
    }

    public void UpdateTransformPreview(CanvasMultiTransformPlan plan)
    {
        _transformPreview = plan.PreviewShapes;
        PreviewRect = plan.PreviewBounds is { } bounds
            ? new SelectionAdornerRect(bounds.Left, bounds.Top, bounds.Width, bounds.Height)
            : null;
        PreviewRotationDeg = plan.PreviewRotationDeg;
    }

    public void UpdateMarquee(SelectionAdornerRect? screenRect) => MarqueeRect = screenRect;

    public void UpdateSnapGuides(
        IReadOnlyList<SnapGuideLine>? guides,
        SlideTransformCore transform)
    {
        SnapGuides = guides;
        SnapTransform = transform;
    }
}

public static class SelectionAdornerGeometry
{
    public const double HandleSize = 8.0;
    public const double RotateHandleRadius = 4.0;
    public const double RotateHandleOffset = 18.0;
    public const double HandleHitRadius = 8.0;
    public const double GeometryHandleHitRadius = 9.0;

    /// <summary>Projects selected frames and optional edit handles into screen space.</summary>
    public static SelectionAdornerProjectionPlan BuildProjection(
        Slide slide,
        Presentation presentation,
        IReadOnlyList<uint> selectedShapeIds,
        SlideTransformCore transform,
        bool editPointsEnabled)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(selectedShapeIds);
        ArgumentNullException.ThrowIfNull(transform);

        var selections = new List<SelectionAdornerSelectionPlan>();
        foreach (uint shapeId in selectedShapeIds)
        {
            var shape = ShapeHitTester.FindShape(slide, shapeId);
            if (shape is null)
                continue;

            var bounds = SlideCanvasGeometryPlanner.ShapeVisualBoundsToScreen(
                shape,
                slide,
                presentation,
                transform);
            selections.Add(new SelectionAdornerSelectionPlan(
                shapeId,
                ToSelectionRect(bounds)));
        }

        return new SelectionAdornerProjectionPlan(
            selections,
            BuildGeometryHandles(
                slide,
                presentation,
                selectedShapeIds,
                transform,
                editPointsEnabled));
    }

    public static IReadOnlyList<CanvasGesturePoint> GetHandleCenters(SelectionAdornerRect rect)
        => DrawingObjectInteractionPlanner.GetResizeHandleCenters(rect.ToLayoutRect())
            .Select(ToCanvasPoint)
            .ToArray();

    public static CanvasGesturePoint GetRotateHandleCenter(SelectionAdornerRect rect)
        => ToCanvasPoint(DrawingObjectInteractionPlanner.GetRotateHandleCenter(
            rect.ToLayoutRect(),
            RotateHandleOffset));

    public static CanvasGestureHandleKind HitTestHandle(
        SelectionAdornerRect selectionRect,
        CanvasGesturePoint screenPoint)
    {
        var hit = DrawingObjectInteractionPlanner.HitTestHandleCenters(
            selectionRect.ToLayoutRect(),
            new LayoutPoint(screenPoint.X, screenPoint.Y),
            HandleHitRadius,
            RotateHandleOffset);
        return ToCanvasHandle(hit);
    }

    /// <summary>Returns the union of all selection frames, or null for an empty selection.</summary>
    public static SelectionAdornerRect? GetSelectionBounds(
        IEnumerable<SelectionAdornerRect> selectionRects)
    {
        ArgumentNullException.ThrowIfNull(selectionRects);

        using var enumerator = selectionRects.GetEnumerator();
        if (!enumerator.MoveNext())
            return null;

        var first = enumerator.Current;
        double left = first.Left;
        double top = first.Top;
        double right = first.Right;
        double bottom = first.Bottom;
        while (enumerator.MoveNext())
        {
            var rect = enumerator.Current;
            left = Math.Min(left, rect.Left);
            top = Math.Min(top, rect.Top);
            right = Math.Max(right, rect.Right);
            bottom = Math.Max(bottom, rect.Bottom);
        }

        return new SelectionAdornerRect(left, top, right - left, bottom - top);
    }

    /// <summary>Returns the first edit handle within the inclusive screen-space radius.</summary>
    public static string? HitTestGeometryHandle(
        IEnumerable<SelectionAdornerGeometryHandlePlan> handles,
        CanvasGesturePoint screenPoint,
        double hitRadius = GeometryHandleHitRadius)
    {
        ArgumentNullException.ThrowIfNull(handles);
        ArgumentOutOfRangeException.ThrowIfNegative(hitRadius);

        double hitRadiusSquared = hitRadius * hitRadius;
        foreach (var handle in handles)
        {
            double dx = screenPoint.X - handle.ScreenPosition.X;
            double dy = screenPoint.Y - handle.ScreenPosition.Y;
            if (dx * dx + dy * dy <= hitRadiusSquared)
                return handle.Name;
        }

        return null;
    }

    private static IReadOnlyList<SelectionAdornerGeometryHandlePlan> BuildGeometryHandles(
        Slide slide,
        Presentation presentation,
        IReadOnlyList<uint> selectedShapeIds,
        SlideTransformCore transform,
        bool editPointsEnabled)
    {
        if (!editPointsEnabled || selectedShapeIds.Count != 1)
            return Array.Empty<SelectionAdornerGeometryHandlePlan>();

        // Keep the existing refresh behavior: edit handles are projected only for top-level shapes.
        uint shapeId = selectedShapeIds[0];
        var shape = slide.Shapes.FirstOrDefault(candidate => candidate.Id == shapeId);
        if (shape is null)
            return Array.Empty<SelectionAdornerGeometryHandlePlan>();

        var bounds = ShapeHitTester.GetShapeBoundsDip(shape, slide, presentation).ToLayoutRect();
        if (shape.Kind == SlideShapeKind.Picture)
        {
            var cropPlan = PictureCropAuthoringPlanner.Build(shape, bounds);
            return cropPlan.CanEdit
                ? cropPlan.Handles
                    .Select(handle => ProjectHandle(handle.Name, handle.PositionDip, bounds, shape.RotationDeg, transform))
                    .ToArray()
                : Array.Empty<SelectionAdornerGeometryHandlePlan>();
        }

        var adjustmentPlan = ShapeGeometryAdjustmentPlanner.Build(shape, bounds);
        return adjustmentPlan.CanEdit
            ? adjustmentPlan.Handles
                .Select(handle => ProjectHandle(handle.Name, handle.PositionDip, bounds, shape.RotationDeg, transform))
                .ToArray()
            : Array.Empty<SelectionAdornerGeometryHandlePlan>();
    }

    /// <summary>
    /// Projects one un-rotated local-frame handle position into screen space, rotating it
    /// about the shape's un-rotated bounds center first so it lands on the same rotated edge
    /// the compositor actually paints (matching SlideCanvasGeometryPlanner.OrientedBoundsToScreen's
    /// corner rotation for the selection outline of the same shape).
    /// </summary>
    private static SelectionAdornerGeometryHandlePlan ProjectHandle(
        string name,
        LayoutPoint positionDip,
        LayoutRect boundsDip,
        double rotationDeg,
        SlideTransformCore transform)
    {
        var rotatedDip = RotateAroundCenter(positionDip, boundsDip.Center, rotationDeg);
        var screenPosition = transform.SlideToScreen(rotatedDip.X, rotatedDip.Y);
        return new SelectionAdornerGeometryHandlePlan(
            name,
            new CanvasGesturePoint(screenPosition.X, screenPosition.Y));
    }

    private static LayoutPoint RotateAroundCenter(LayoutPoint point, LayoutPoint center, double rotationDeg)
    {
        if (rotationDeg % 360.0 == 0.0)
            return point;

        double radians = rotationDeg * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double dx = point.X - center.X;
        double dy = point.Y - center.Y;
        return new LayoutPoint(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos);
    }

    private static SelectionAdornerRect ToSelectionRect(SlideScreenRect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static CanvasGesturePoint ToCanvasPoint(LayoutPoint point) =>
        new(point.X, point.Y);

    private static CanvasGestureHandleKind ToCanvasHandle(DrawingObjectInteractionKind kind) =>
        kind switch
        {
            DrawingObjectInteractionKind.Body => CanvasGestureHandleKind.Body,
            DrawingObjectInteractionKind.ResizeN => CanvasGestureHandleKind.ResizeN,
            DrawingObjectInteractionKind.ResizeNE => CanvasGestureHandleKind.ResizeNE,
            DrawingObjectInteractionKind.ResizeE => CanvasGestureHandleKind.ResizeE,
            DrawingObjectInteractionKind.ResizeSE => CanvasGestureHandleKind.ResizeSE,
            DrawingObjectInteractionKind.ResizeS => CanvasGestureHandleKind.ResizeS,
            DrawingObjectInteractionKind.ResizeSW => CanvasGestureHandleKind.ResizeSW,
            DrawingObjectInteractionKind.ResizeW => CanvasGestureHandleKind.ResizeW,
            DrawingObjectInteractionKind.ResizeNW => CanvasGestureHandleKind.ResizeNW,
            DrawingObjectInteractionKind.Rotate => CanvasGestureHandleKind.Rotate,
            _ => CanvasGestureHandleKind.None
        };
}
