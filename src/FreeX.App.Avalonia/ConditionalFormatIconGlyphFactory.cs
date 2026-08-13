using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.ConditionalFormatting;

using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaLine = Avalonia.Controls.Shapes.Line;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace FreeX.App.Avalonia;

/// <summary>
/// Builds a vector control for a single conditional-format icon-set glyph, mirroring the desktop
/// renderer's shapes (arrows, traffic lights, signs, symbols, flags, ratings, quarters, boxes). The
/// glyph kind, bucket index and fill color are decided by <see cref="ConditionalFormatCellRenderPlanner"/>;
/// the per-glyph geometry comes from the shared <see cref="ConditionalIconGlyphGeometry"/> emitter, so
/// this factory only translates neutral primitive ops into vector controls.
/// </summary>
internal static class ConditionalFormatIconGlyphFactory
{
    private static readonly IBrush OutlineBrush = new ImmutableSolidColorBrush(Color.FromRgb(96, 96, 96));
    private const double OutlineThickness = 0.75;
    private const double WhiteThinThickness = 1.2;
    private const double WhiteMediumThickness = 1.4;

    public static Control Create(CfIconRenderInstruction icon, double size)
    {
        var fill = new SolidColorBrush(Color.Parse(icon.ColorHex));
        var canvas = new Canvas
        {
            Width = size,
            Height = size,
            IsHitTestVisible = false,
        };

        var ops = ConditionalIconGlyphGeometry.Build(
            icon.GlyphKind,
            icon.IconIndex,
            icon.IconCount,
            0,
            0,
            size,
            size);

        foreach (var op in ops)
            canvas.Children.Add(BuildShape(op, fill));

        return canvas;
    }

    private static Control BuildShape(CfGlyphOp op, IBrush iconFill)
    {
        return op.Kind switch
        {
            CfGlyphPrimitiveKind.Ellipse => Ellipse(op, iconFill),
            CfGlyphPrimitiveKind.Line => Line(op),
            CfGlyphPrimitiveKind.Box => Box(op, iconFill),
            CfGlyphPrimitiveKind.Polyline => GeometryPath(PolylineGeometry(op.Points, closed: false), op, iconFill),
            CfGlyphPrimitiveKind.Polygon => GeometryPath(PolylineGeometry(op.Points, closed: true), op, iconFill),
            CfGlyphPrimitiveKind.StarFillFraction => StarFillFraction(op, iconFill),
            _ => GeometryPath(PieGeometry(op), op, iconFill),
        };
    }

    // ── Primitives ───────────────────────────────────────────────────────────

    private static Control Ellipse(CfGlyphOp op, IBrush iconFill)
    {
        var ellipse = new AvaloniaEllipse
        {
            Width = op.RadiusX * 2,
            Height = op.RadiusY * 2,
            Fill = Fill(op.Fill, iconFill),
        };
        ApplyStroke((brush, thickness) => { ellipse.Stroke = brush; ellipse.StrokeThickness = thickness; }, op.Stroke);

        Canvas.SetLeft(ellipse, op.Center.X - op.RadiusX);
        Canvas.SetTop(ellipse, op.Center.Y - op.RadiusY);
        return ellipse;
    }

    private static Control Line(CfGlyphOp op)
    {
        var line = new AvaloniaLine
        {
            StartPoint = ToPoint(op.Points[0]),
            EndPoint = ToPoint(op.Points[1]),
        };
        ApplyStroke((brush, thickness) => { line.Stroke = brush; line.StrokeThickness = thickness; }, op.Stroke);
        return line;
    }

    private static Control Box(CfGlyphOp op, IBrush iconFill)
    {
        var border = new Border
        {
            Width = op.Rect.Width,
            Height = op.Rect.Height,
            Margin = new Thickness(op.Rect.X, op.Rect.Y, 0, 0),
            Background = Fill(op.Fill, iconFill),
        };
        if (op.Stroke == CfGlyphStroke.Outline)
        {
            border.BorderBrush = OutlineBrush;
            border.BorderThickness = new Thickness(OutlineThickness);
        }

        return border;
    }

    private static Control GeometryPath(Geometry geometry, CfGlyphOp op, IBrush iconFill)
    {
        var path = new AvaloniaPath
        {
            Data = geometry,
            Fill = Fill(op.Fill, iconFill),
        };
        ApplyStroke((brush, thickness) => { path.Stroke = brush; path.StrokeThickness = thickness; }, op.Stroke);
        return path;
    }

    /// <summary>
    /// Renders a star with a horizontal partial fill. The star polygon is drawn as an outline (gray),
    /// and the left <c>fillFraction</c> portion of its bounding box is clipped to show the icon fill
    /// color. This matches Excel's partial-star appearance for the Stars icon sets.
    /// </summary>
    private static Control StarFillFraction(CfGlyphOp op, IBrush iconFill)
    {
        var plan = ConditionalIconGlyphGeometry.PlanStarFill(op);

        // Build the star geometry (used for both the clip-filled path and the outline).
        var starGeometry = PolylineGeometry(plan.Points, closed: true);

        // The filled portion: a path with the star geometry, clipped to the left fillFraction strip.
        var filledPath = new AvaloniaPath
        {
            Data = starGeometry,
            Fill = iconFill,
            // Clip to a rectangle covering only the left fillFraction of the star bounding box.
            Clip = !plan.RequiresClip
                ? null
                : new RectangleGeometry(new Rect(
                    plan.ClipRect.X,
                    plan.ClipRect.Y,
                    Math.Max(0, plan.ClipRect.Width),
                    plan.ClipRect.Height)),
        };

        // The outline star drawn over the fill (always full outline regardless of fill fraction).
        var outlinePath = new AvaloniaPath
        {
            Data = starGeometry,
            Fill = null,
        };
        ApplyStroke((brush, thickness) => { outlinePath.Stroke = brush; outlinePath.StrokeThickness = thickness; }, op.Stroke);

        // Composite: filled star (clipped) + outline star, inside a shared canvas.
        var canvas = new Canvas
        {
            IsHitTestVisible = false,
        };
        canvas.Children.Add(filledPath);
        canvas.Children.Add(outlinePath);
        return canvas;
    }

    private static IBrush? Fill(CfGlyphFill fill, IBrush iconFill) => fill switch
    {
        CfGlyphFill.Icon => iconFill,
        CfGlyphFill.White => Brushes.White,
        _ => null,
    };

    private static void ApplyStroke(System.Action<IBrush, double> set, CfGlyphStroke stroke)
    {
        switch (stroke)
        {
            case CfGlyphStroke.Outline:
                set(OutlineBrush, OutlineThickness);
                break;
            case CfGlyphStroke.WhiteThin:
                set(Brushes.White, WhiteThinThickness);
                break;
            case CfGlyphStroke.WhiteMedium:
                set(Brushes.White, WhiteMediumThickness);
                break;
        }
    }

    private static Point ToPoint(LayoutPoint p) => new(p.X, p.Y);

    // ── Geometry builders ─────────────────────────────────────────────────────

    private static Geometry PolylineGeometry(IReadOnlyList<LayoutPoint> points, bool closed)
    {
        var figure = new PathFigure
        {
            IsClosed = closed,
            IsFilled = closed,
            StartPoint = ToPoint(points[0]),
        };
        for (var i = 1; i < points.Count; i++)
            figure.Segments!.Add(new LineSegment { Point = ToPoint(points[i]) });
        return Figure(figure);
    }

    private static Geometry PieGeometry(CfGlyphOp op)
    {
        var figure = new PathFigure { IsClosed = true, IsFilled = true, StartPoint = ToPoint(op.Center) };
        figure.Segments!.Add(new LineSegment { Point = ToPoint(op.Points[0]) });
        figure.Segments!.Add(new ArcSegment
        {
            Point = ToPoint(op.Points[1]),
            Size = new Size(op.RadiusX, op.RadiusY),
            RotationAngle = 0,
            IsLargeArc = op.LargeArc,
            SweepDirection = SweepDirection.Clockwise,
        });
        return Figure(figure);
    }

    private static Geometry Figure(PathFigure figure)
    {
        var geometry = new PathGeometry();
        geometry.Figures!.Add(figure);
        return geometry;
    }
}
