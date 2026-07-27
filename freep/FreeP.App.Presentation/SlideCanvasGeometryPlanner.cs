using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public readonly record struct SlideScreenRect(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

public readonly record struct InCanvasEditorPlacement(
    double Left,
    double Top,
    double Width,
    double Height);

public static class SlideCanvasGeometryPlanner
{
    public static SlideScreenRect DipBoundsToScreen(
        double left,
        double top,
        double width,
        double height,
        SlideTransformCore transform)
    {
        ArgumentNullException.ThrowIfNull(transform);

        var origin = transform.SlideToScreen(left, top);
        return new SlideScreenRect(
            origin.X,
            origin.Y,
            Math.Max(0, transform.ScaleDipToScreen(width)),
            Math.Max(0, transform.ScaleDipToScreen(height)));
    }

    public static SlideScreenRect DipBoundsToScreen(ShapeBoundsDip bounds, SlideTransformCore transform) =>
        DipBoundsToScreen(bounds.Left, bounds.Top, bounds.Width, bounds.Height, transform);

    public static SlideScreenRect DipBoundsToScreen(CellRectDip bounds, SlideTransformCore transform) =>
        DipBoundsToScreen(bounds.X, bounds.Y, bounds.Width, bounds.Height, transform);

    public static SlideScreenRect DipBoundsToScreen(LayoutRect bounds, SlideTransformCore transform) =>
        DipBoundsToScreen(bounds.X, bounds.Y, bounds.Width, bounds.Height, transform);

    public static SlideScreenRect EmuBoundsToScreen(
        long offsetXEmu,
        long offsetYEmu,
        long extentCxEmu,
        long extentCyEmu,
        SlideTransformCore transform) =>
        DipBoundsToScreen(
            SlideTransformCore.EmuToDip(offsetXEmu),
            SlideTransformCore.EmuToDip(offsetYEmu),
            SlideTransformCore.EmuToDip(extentCxEmu),
            SlideTransformCore.EmuToDip(extentCyEmu),
            transform);

    public static SlideScreenRect? ShapeBoundsToScreen(
        Slide slide,
        Presentation presentation,
        uint shapeId,
        SlideTransformCore transform)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(presentation);

        var shape = ShapeHitTester.FindShape(slide, shapeId);
        return shape is null
            ? null
            : ShapeBoundsToScreen(shape, presentation, transform);
    }

    public static SlideScreenRect ShapeBoundsToScreen(
        SlideShape shape,
        Presentation presentation,
        SlideTransformCore transform)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(presentation);

        return DipBoundsToScreen(ShapeHitTester.GetShapeBoundsDip(shape, presentation), transform);
    }

    public static SlideScreenRect ScreenRectBetween(
        CanvasGesturePoint start,
        CanvasGesturePoint current)
    {
        double left = Math.Min(start.X, current.X);
        double top = Math.Min(start.Y, current.Y);
        double right = Math.Max(start.X, current.X);
        double bottom = Math.Max(start.Y, current.Y);
        return new SlideScreenRect(left, top, right - left, bottom - top);
    }

    public static InCanvasEditorPlacement PlanEditorPlacement(
        SlideScreenRect screenRect,
        double minimumWidth,
        double minimumHeight) =>
        new(
            screenRect.Left,
            screenRect.Top,
            Math.Max(minimumWidth, screenRect.Width),
            Math.Max(minimumHeight, screenRect.Height));

    public static SlideScreenRect? Union(IEnumerable<SlideScreenRect> rects)
    {
        ArgumentNullException.ThrowIfNull(rects);

        using var enumerator = rects.GetEnumerator();
        if (!enumerator.MoveNext())
            return null;

        double left = enumerator.Current.Left;
        double top = enumerator.Current.Top;
        double right = enumerator.Current.Right;
        double bottom = enumerator.Current.Bottom;

        while (enumerator.MoveNext())
        {
            var rect = enumerator.Current;
            left = Math.Min(left, rect.Left);
            top = Math.Min(top, rect.Top);
            right = Math.Max(right, rect.Right);
            bottom = Math.Max(bottom, rect.Bottom);
        }

        return new SlideScreenRect(left, top, right - left, bottom - top);
    }

    public static double SnapGuideToScreenPosition(SnapGuideLine guide, SlideTransformCore transform)
    {
        ArgumentNullException.ThrowIfNull(transform);

        var point = guide.IsHorizontal
            ? transform.SlideToScreen(0, guide.Position)
            : transform.SlideToScreen(guide.Position, 0);
        return guide.IsHorizontal ? point.Y : point.X;
    }
}
