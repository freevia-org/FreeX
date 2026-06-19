using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using FreeX.App.Presentation.ConditionalFormatting;

using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaLine = Avalonia.Controls.Shapes.Line;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace FreeX.App.Avalonia;

/// <summary>
/// Builds an Avalonia vector control for a single conditional-format icon-set glyph, mirroring the
/// desktop <c>ConditionalIconGlyphRenderer</c> shapes (arrows, traffic lights, signs, symbols,
/// flags, ratings, quarters, boxes). Pure view construction — the glyph kind, bucket index and
/// fill color are decided by <see cref="ConditionalFormatCellRenderPlanner"/>.
/// </summary>
internal static class ConditionalFormatIconGlyphFactory
{
    private static readonly IBrush OutlineBrush = new SolidColorBrush(Color.FromRgb(96, 96, 96));
    private const double OutlineThickness = 0.75;

    public static Control Create(CfIconRenderInstruction icon, double size)
    {
        var fill = new SolidColorBrush(Color.Parse(icon.ColorHex));
        var canvas = new Canvas
        {
            Width = size,
            Height = size,
            IsHitTestVisible = false,
        };

        foreach (var shape in BuildShapes(icon, new Rect(0, 0, size, size), fill))
            canvas.Children.Add(shape);

        return canvas;
    }

    private static IEnumerable<Control> BuildShapes(CfIconRenderInstruction icon, Rect rect, IBrush fill)
    {
        switch (icon.GlyphKind)
        {
            case ConditionalIconGlyphKind.TrafficLight:
                yield return Ellipse(rect, fill, outline: true);
                break;
            case ConditionalIconGlyphKind.Sign:
                foreach (var s in SignGlyph(icon.IconIndex, rect, fill))
                    yield return s;
                break;
            case ConditionalIconGlyphKind.Symbol:
                foreach (var s in SymbolGlyph(icon.IconIndex, rect, fill))
                    yield return s;
                break;
            case ConditionalIconGlyphKind.Flag:
                yield return GeometryPath(FlagGeometry(rect), fill, outline: true);
                break;
            case ConditionalIconGlyphKind.Rating:
                yield return GeometryPath(StarGeometry(rect), fill, outline: true);
                break;
            case ConditionalIconGlyphKind.Quarter:
                yield return Ellipse(rect, Brushes.White, outline: true);
                yield return GeometryPath(PieGeometry(rect, QuarterSweep(icon)), fill, outline: false);
                yield return Ellipse(rect, Brushes.Transparent, outline: true);
                break;
            case ConditionalIconGlyphKind.Box:
                yield return BoxGlyph(icon, rect, fill);
                break;
            default:
                yield return GeometryPath(ArrowGeometry(rect, icon.IconIndex), fill, outline: true);
                break;
        }
    }

    // ── Composite glyphs ─────────────────────────────────────────────────────

    private static IEnumerable<Control> SignGlyph(int iconIndex, Rect rect, IBrush fill)
    {
        if (iconIndex <= 0)
        {
            yield return Ellipse(rect, fill, outline: true);
            yield return WhiteLine(rect, 0.28, 0.28, 0.72, 0.72, 1.2);
            yield return WhiteLine(rect, 0.72, 0.28, 0.28, 0.72, 1.2);
        }
        else if (iconIndex == 1)
        {
            yield return GeometryPath(TriangleGeometry(rect, pointUp: true), fill, outline: true);
            yield return WhiteLine(rect, 0.5, 0.3, 0.5, 0.62, 1.2);
        }
        else
        {
            yield return Ellipse(rect, fill, outline: true);
            yield return WhiteLine(rect, 0.28, 0.56, 0.44, 0.72, 1.4);
            yield return WhiteLine(rect, 0.44, 0.72, 0.76, 0.3, 1.4);
        }
    }

    private static IEnumerable<Control> SymbolGlyph(int iconIndex, Rect rect, IBrush fill)
    {
        if (iconIndex <= 0)
        {
            yield return GeometryPath(DiamondGeometry(rect), fill, outline: true);
            yield return WhiteLine(rect, 0.32, 0.32, 0.68, 0.68, 1.2);
            yield return WhiteLine(rect, 0.68, 0.32, 0.32, 0.68, 1.2);
        }
        else if (iconIndex == 1)
        {
            yield return Ellipse(rect, fill, outline: true);
            yield return WhiteLine(rect, 0.3, 0.5, 0.7, 0.5, 1.2);
        }
        else
        {
            yield return Ellipse(rect, fill, outline: true);
            yield return WhiteLine(rect, 0.28, 0.56, 0.44, 0.72, 1.4);
            yield return WhiteLine(rect, 0.44, 0.72, 0.76, 0.3, 1.4);
        }
    }

    private static Control BoxGlyph(CfIconRenderInstruction icon, Rect rect, IBrush fill)
    {
        var inset = Math.Max(0, (icon.IconCount - 1 - icon.IconIndex) * rect.Width * 0.07);
        return new Border
        {
            Width = Math.Max(1, rect.Width - inset * 2),
            Height = Math.Max(1, rect.Height - inset * 2),
            Margin = new Thickness(inset, inset, 0, 0),
            Background = fill,
            BorderBrush = OutlineBrush,
            BorderThickness = new Thickness(OutlineThickness),
        };
    }

    // ── Primitives ───────────────────────────────────────────────────────────

    private static Control Ellipse(Rect rect, IBrush fill, bool outline)
    {
        var ellipse = new AvaloniaEllipse
        {
            Width = rect.Width,
            Height = rect.Height,
            Fill = fill,
        };
        if (outline)
        {
            ellipse.Stroke = OutlineBrush;
            ellipse.StrokeThickness = OutlineThickness;
        }

        Canvas.SetLeft(ellipse, rect.Left);
        Canvas.SetTop(ellipse, rect.Top);
        return ellipse;
    }

    private static Control WhiteLine(Rect rect, double x1, double y1, double x2, double y2, double thickness)
    {
        var line = new AvaloniaLine
        {
            StartPoint = new Point(rect.Left + rect.Width * x1, rect.Top + rect.Height * y1),
            EndPoint = new Point(rect.Left + rect.Width * x2, rect.Top + rect.Height * y2),
            Stroke = Brushes.White,
            StrokeThickness = thickness,
        };
        return line;
    }

    private static Control GeometryPath(Geometry geometry, IBrush fill, bool outline)
    {
        var path = new AvaloniaPath
        {
            Data = geometry,
            Fill = fill,
        };
        if (outline)
        {
            path.Stroke = OutlineBrush;
            path.StrokeThickness = OutlineThickness;
        }

        return path;
    }

    private static double QuarterSweep(CfIconRenderInstruction icon) =>
        Math.Max(1, icon.IconIndex + 1) / Math.Max(1d, icon.IconCount);

    // ── Geometry builders (mirror the desktop StreamGeometry shapes) ──────────

    private static Geometry ArrowGeometry(Rect rect, int iconIndex)
    {
        var figure = new PathFigure { IsClosed = true, IsFilled = true };
        if (iconIndex == 1)
        {
            figure.StartPoint = new Point(rect.Left, rect.Top + rect.Height / 2);
            AddLines(figure,
                new Point(rect.Right - 3, rect.Top + rect.Height / 2),
                new Point(rect.Right - 3, rect.Top + 2),
                new Point(rect.Right, rect.Top + rect.Height / 2),
                new Point(rect.Right - 3, rect.Bottom - 2),
                new Point(rect.Right - 3, rect.Top + rect.Height / 2));
        }
        else if (iconIndex == 0)
        {
            figure.StartPoint = new Point(rect.Left + rect.Width / 2, rect.Bottom);
            AddLines(figure,
                new Point(rect.Left + rect.Width / 2, rect.Top + 3),
                new Point(rect.Left + 2, rect.Top + 3),
                new Point(rect.Left + rect.Width / 2, rect.Top),
                new Point(rect.Right - 2, rect.Top + 3),
                new Point(rect.Left + rect.Width / 2, rect.Top + 3));
        }
        else
        {
            figure.StartPoint = new Point(rect.Left + rect.Width / 2, rect.Top);
            AddLines(figure,
                new Point(rect.Left + rect.Width / 2, rect.Bottom - 3),
                new Point(rect.Left + 2, rect.Bottom - 3),
                new Point(rect.Left + rect.Width / 2, rect.Bottom),
                new Point(rect.Right - 2, rect.Bottom - 3),
                new Point(rect.Left + rect.Width / 2, rect.Bottom - 3));
        }

        return Figure(figure);
    }

    private static Geometry TriangleGeometry(Rect rect, bool pointUp)
    {
        var figure = new PathFigure { IsClosed = true, IsFilled = true };
        if (pointUp)
        {
            figure.StartPoint = new Point(rect.Left + rect.Width / 2, rect.Top);
            AddLines(figure, new Point(rect.Right, rect.Bottom), new Point(rect.Left, rect.Bottom));
        }
        else
        {
            figure.StartPoint = new Point(rect.Left, rect.Top);
            AddLines(figure, new Point(rect.Right, rect.Top), new Point(rect.Left + rect.Width / 2, rect.Bottom));
        }

        return Figure(figure);
    }

    private static Geometry DiamondGeometry(Rect rect)
    {
        var figure = new PathFigure { IsClosed = true, IsFilled = true, StartPoint = new Point(rect.Left + rect.Width / 2, rect.Top) };
        AddLines(figure,
            new Point(rect.Right, rect.Top + rect.Height / 2),
            new Point(rect.Left + rect.Width / 2, rect.Bottom),
            new Point(rect.Left, rect.Top + rect.Height / 2));
        return Figure(figure);
    }

    private static Geometry FlagGeometry(Rect rect)
    {
        var poleX = rect.Left + rect.Width * 0.25;
        var figure = new PathFigure
        {
            IsClosed = true,
            IsFilled = true,
            StartPoint = new Point(poleX, rect.Top + rect.Height * 0.08),
        };
        AddLines(figure,
            new Point(rect.Right, rect.Top + rect.Height * 0.18),
            new Point(rect.Right - rect.Width * 0.18, rect.Top + rect.Height * 0.46),
            new Point(poleX, rect.Top + rect.Height * 0.38));
        return Figure(figure);
    }

    private static Geometry StarGeometry(Rect rect)
    {
        var center = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        var outer = Math.Min(rect.Width, rect.Height) / 2;
        var inner = outer * 0.45;
        var figure = new PathFigure { IsClosed = true, IsFilled = true };
        for (var i = 0; i < 10; i++)
        {
            var radius = i % 2 == 0 ? outer : inner;
            var angle = -Math.PI / 2 + i * Math.PI / 5;
            var point = new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
            if (i == 0)
                figure.StartPoint = point;
            else
                figure.Segments!.Add(new LineSegment { Point = point });
        }

        return Figure(figure);
    }

    private static Geometry PieGeometry(Rect rect, double sweepFraction)
    {
        var center = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        var radiusX = rect.Width / 2;
        var radiusY = rect.Height / 2;
        var sweep = Math.Clamp(sweepFraction, 0d, 1d) * Math.PI * 2;
        var start = -Math.PI / 2;
        var end = start + sweep;
        var startPoint = new Point(center.X, rect.Top);
        var endPoint = new Point(center.X + Math.Cos(end) * radiusX, center.Y + Math.Sin(end) * radiusY);

        var figure = new PathFigure { IsClosed = true, IsFilled = true, StartPoint = center };
        figure.Segments!.Add(new LineSegment { Point = startPoint });
        figure.Segments!.Add(new ArcSegment
        {
            Point = endPoint,
            Size = new Size(radiusX, radiusY),
            RotationAngle = 0,
            IsLargeArc = sweep > Math.PI,
            SweepDirection = SweepDirection.Clockwise,
        });
        return Figure(figure);
    }

    private static void AddLines(PathFigure figure, params Point[] points)
    {
        foreach (var point in points)
            figure.Segments!.Add(new LineSegment { Point = point });
    }

    private static Geometry Figure(PathFigure figure)
    {
        var geometry = new PathGeometry();
        geometry.Figures!.Add(figure);
        return geometry;
    }
}
