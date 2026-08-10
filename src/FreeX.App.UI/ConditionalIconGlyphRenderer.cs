using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.UI;

internal static class ConditionalIconGlyphRenderer
{
    private static readonly SolidColorBrush IconDarkRedBrush = FrozenBrush(0xC0, 0x00, 0x00);
    private static readonly SolidColorBrush IconOrangeBrush = FrozenBrush(0xED, 0x7D, 0x31);
    private static readonly SolidColorBrush IconYellowBrush = FrozenBrush(0xFF, 0xC0, 0x00);
    private static readonly SolidColorBrush IconLightGreenBrush = FrozenBrush(0x92, 0xD0, 0x50);
    private static readonly SolidColorBrush IconGreenBrush = FrozenBrush(0x00, 0xB0, 0x50);
    private static readonly SolidColorBrush IconGrayBrush = FrozenBrush(0x66, 0x66, 0x66);
    private static readonly Pen OutlinePen = FrozenPen(FrozenBrush(96, 96, 96), 0.75);
    private static readonly Pen WhiteThinPen = FrozenPen(Brushes.White, 1.2);
    private static readonly Pen WhiteMediumPen = FrozenPen(Brushes.White, 1.4);
    private static readonly ConcurrentDictionary<ConditionalIconAppearanceKey, ConditionalIconAppearance> AppearanceCache = new();

    public static void Draw(DrawingContext dc, ConditionalFormatIcon icon, Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var appearance = ResolveAppearance(icon);
        var ops = ConditionalIconGlyphGeometry.Build(
            appearance.GlyphKind,
            icon.IconIndex,
            icon.IconCount,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height);

        foreach (var op in ops)
            DrawOp(dc, op, appearance.Brush);
    }

    private static void DrawOp(DrawingContext dc, CfGlyphOp op, Brush iconBrush)
    {
        var brush = FillBrush(op.Fill, iconBrush);
        var pen = StrokePen(op.Stroke);

        switch (op.Kind)
        {
            case CfGlyphPrimitiveKind.Ellipse:
                dc.DrawEllipse(brush, pen, ToPoint(op.Center), op.RadiusX, op.RadiusY);
                break;
            case CfGlyphPrimitiveKind.Line:
                dc.DrawLine(pen, ToPoint(op.Points[0]), ToPoint(op.Points[1]));
                break;
            case CfGlyphPrimitiveKind.Box:
                dc.DrawRectangle(brush, pen, new Rect(op.Rect.X, op.Rect.Y, op.Rect.Width, op.Rect.Height));
                break;
            case CfGlyphPrimitiveKind.Polyline:
                dc.DrawGeometry(null, pen, PolylineGeometry(op.Points, closed: false));
                break;
            case CfGlyphPrimitiveKind.Polygon:
                dc.DrawGeometry(brush, pen, PolylineGeometry(op.Points, closed: true));
                break;
            case CfGlyphPrimitiveKind.Pie:
                dc.DrawGeometry(brush, pen, PieGeometry(op));
                break;
            case CfGlyphPrimitiveKind.StarFillFraction:
                DrawStarFillFraction(dc, op, iconBrush);
                break;
        }
    }

    private static Brush? FillBrush(CfGlyphFill fill, Brush iconBrush) => fill switch
    {
        CfGlyphFill.Icon => iconBrush,
        CfGlyphFill.White => Brushes.White,
        _ => null,
    };

    private static Pen? StrokePen(CfGlyphStroke stroke) => stroke switch
    {
        CfGlyphStroke.Outline => OutlinePen,
        CfGlyphStroke.WhiteThin => WhiteThinPen,
        CfGlyphStroke.WhiteMedium => WhiteMediumPen,
        _ => null,
    };

    private static Point ToPoint(LayoutPoint p) => new(p.X, p.Y);

    /// <summary>
    /// Draws a star polygon with a horizontal clip so the left <c>fillFraction</c> portion of the
    /// star bounding box is filled with the icon brush, and the remainder is outline-only. This
    /// matches Excel's partial-star appearance for the Stars icon sets.
    /// </summary>
    private static void DrawStarFillFraction(DrawingContext dc, CfGlyphOp op, Brush iconBrush)
    {
        var plan = ConditionalIconGlyphGeometry.PlanStarFill(op);
        var pen = StrokePen(op.Stroke);
        var starGeom = PolylineGeometry(plan.Points, closed: true);

        if (plan.ShouldFill)
        {
            var clipRect = new Rect(
                plan.ClipRect.X,
                plan.ClipRect.Y,
                plan.ClipRect.Width,
                plan.ClipRect.Height);
            dc.PushClip(new RectangleGeometry(clipRect));
            dc.DrawGeometry(iconBrush, null, starGeom);
            dc.Pop();
        }

        // Always draw the full star outline.
        dc.DrawGeometry(null, pen, starGeom);
    }

    private static StreamGeometry PolylineGeometry(IReadOnlyList<LayoutPoint> points, bool closed)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToPoint(points[0]), isFilled: closed, isClosed: closed);
            for (var i = 1; i < points.Count; i++)
                context.LineTo(ToPoint(points[i]), isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry PieGeometry(CfGlyphOp op)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToPoint(op.Center), isFilled: true, isClosed: true);
            context.LineTo(ToPoint(op.Points[0]), isStroked: true, isSmoothJoin: false);
            context.ArcTo(
                ToPoint(op.Points[1]),
                new Size(op.RadiusX, op.RadiusY),
                0,
                op.LargeArc,
                SweepDirection.Clockwise,
                isStroked: true,
                isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static ConditionalIconAppearance ResolveAppearance(ConditionalFormatIcon icon)
    {
        var key = new ConditionalIconAppearanceKey(
            icon.Style ?? "",
            icon.IconIndex,
            icon.IconCount);
        return AppearanceCache.GetOrAdd(key, static cacheKey =>
        {
            var icon = new ConditionalFormatIcon(
                cacheKey.Style,
                cacheKey.IconIndex,
                cacheKey.IconCount,
                ShowValue: true);
            return new ConditionalIconAppearance(
                ConditionalIconLayoutPlanner.ResolveGlyphKind(icon),
                BrushForResolvedColor(ConditionalIconLayoutPlanner.ResolveColor(icon)));
        });
    }

    private static SolidColorBrush BrushForResolvedColor(string color) => color switch
    {
        "#C00000" => IconDarkRedBrush,
        "#ED7D31" => IconOrangeBrush,
        "#FFC000" => IconYellowBrush,
        "#92D050" => IconLightGreenBrush,
        "#00B050" => IconGreenBrush,
        "#666666" => IconGrayBrush,
        _ => IconGreenBrush,
    };

    private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private readonly record struct ConditionalIconAppearanceKey(
        string Style,
        int IconIndex,
        int IconCount);

    private readonly record struct ConditionalIconAppearance(
        ConditionalIconGlyphKind GlyphKind,
        SolidColorBrush Brush);
}
