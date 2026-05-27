using System.Windows;
using System.Windows.Media;

using Freexcel.Core.Model;

namespace Freexcel.App.UI;

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

    public static void Draw(DrawingContext dc, ConditionalFormatIcon icon, Rect rect)
    {
        var brush = BrushForResolvedColor(ConditionalIconLayoutPlanner.ResolveColor(icon));
        var outline = OutlinePen;

        switch (ConditionalIconLayoutPlanner.ResolveGlyphKind(icon))
        {
            case ConditionalIconGlyphKind.TrafficLight:
                dc.DrawEllipse(brush, outline, Center(rect), rect.Width / 2, rect.Height / 2);
                break;
            case ConditionalIconGlyphKind.Sign:
                DrawSignIcon(dc, icon, rect, brush, outline);
                break;
            case ConditionalIconGlyphKind.Symbol:
                DrawSymbolIcon(dc, icon, rect, brush, outline);
                break;
            case ConditionalIconGlyphKind.Flag:
                dc.DrawGeometry(brush, outline, CreateFlagGeometry(rect));
                break;
            case ConditionalIconGlyphKind.Rating:
                dc.DrawGeometry(brush, outline, CreateStarGeometry(rect));
                break;
            case ConditionalIconGlyphKind.Quarter:
                DrawQuarterIcon(dc, icon, rect, brush, outline);
                break;
            case ConditionalIconGlyphKind.Box:
                DrawBoxIcon(dc, icon, rect, brush, outline);
                break;
            default:
                dc.DrawGeometry(brush, outline, CreateArrowGeometry(rect, icon.IconIndex));
                break;
        }
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

    private static Point Center(Rect rect) =>
        new(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);

    private static void DrawSignIcon(DrawingContext dc, ConditionalFormatIcon icon, Rect rect, Brush brush, Pen outline)
    {
        if (icon.IconIndex <= 0)
        {
            dc.DrawEllipse(brush, outline, Center(rect), rect.Width / 2, rect.Height / 2);
            dc.DrawLine(WhiteThinPen, new Point(rect.Left + rect.Width * 0.28, rect.Top + rect.Height * 0.28), new Point(rect.Right - rect.Width * 0.28, rect.Bottom - rect.Height * 0.28));
            dc.DrawLine(WhiteThinPen, new Point(rect.Right - rect.Width * 0.28, rect.Top + rect.Height * 0.28), new Point(rect.Left + rect.Width * 0.28, rect.Bottom - rect.Height * 0.28));
        }
        else if (icon.IconIndex == 1)
        {
            dc.DrawGeometry(brush, outline, CreateTriangleGeometry(rect, pointUp: true));
            dc.DrawLine(WhiteThinPen, new Point(rect.Left + rect.Width * 0.5, rect.Top + rect.Height * 0.3), new Point(rect.Left + rect.Width * 0.5, rect.Top + rect.Height * 0.62));
            dc.DrawEllipse(Brushes.White, null, new Point(rect.Left + rect.Width * 0.5, rect.Top + rect.Height * 0.75), 0.9, 0.9);
        }
        else
        {
            dc.DrawEllipse(brush, outline, Center(rect), rect.Width / 2, rect.Height / 2);
            dc.DrawLine(WhiteMediumPen, new Point(rect.Left + rect.Width * 0.28, rect.Top + rect.Height * 0.56), new Point(rect.Left + rect.Width * 0.44, rect.Top + rect.Height * 0.72));
            dc.DrawLine(WhiteMediumPen, new Point(rect.Left + rect.Width * 0.44, rect.Top + rect.Height * 0.72), new Point(rect.Right - rect.Width * 0.24, rect.Top + rect.Height * 0.3));
        }
    }

    private static void DrawSymbolIcon(DrawingContext dc, ConditionalFormatIcon icon, Rect rect, Brush brush, Pen outline)
    {
        if (icon.IconIndex <= 0)
        {
            dc.DrawGeometry(brush, outline, CreateDiamondGeometry(rect));
            dc.DrawLine(WhiteThinPen, new Point(rect.Left + rect.Width * 0.32, rect.Top + rect.Height * 0.32), new Point(rect.Right - rect.Width * 0.32, rect.Bottom - rect.Height * 0.32));
            dc.DrawLine(WhiteThinPen, new Point(rect.Right - rect.Width * 0.32, rect.Top + rect.Height * 0.32), new Point(rect.Left + rect.Width * 0.32, rect.Bottom - rect.Height * 0.32));
        }
        else if (icon.IconIndex == 1)
        {
            dc.DrawEllipse(brush, outline, Center(rect), rect.Width / 2, rect.Height / 2);
            dc.DrawLine(WhiteThinPen, new Point(rect.Left + rect.Width * 0.3, rect.Top + rect.Height * 0.5), new Point(rect.Right - rect.Width * 0.3, rect.Top + rect.Height * 0.5));
        }
        else
        {
            dc.DrawEllipse(brush, outline, Center(rect), rect.Width / 2, rect.Height / 2);
            dc.DrawLine(WhiteMediumPen, new Point(rect.Left + rect.Width * 0.28, rect.Top + rect.Height * 0.56), new Point(rect.Left + rect.Width * 0.44, rect.Top + rect.Height * 0.72));
            dc.DrawLine(WhiteMediumPen, new Point(rect.Left + rect.Width * 0.44, rect.Top + rect.Height * 0.72), new Point(rect.Right - rect.Width * 0.24, rect.Top + rect.Height * 0.3));
        }
    }

    private static void DrawQuarterIcon(DrawingContext dc, ConditionalFormatIcon icon, Rect rect, Brush brush, Pen outline)
    {
        dc.DrawEllipse(Brushes.White, outline, Center(rect), rect.Width / 2, rect.Height / 2);
        var sweep = Math.Max(1, icon.IconIndex + 1) / Math.Max(1d, icon.IconCount);
        dc.DrawGeometry(brush, null, CreatePieGeometry(rect, sweep));
        dc.DrawEllipse(null, outline, Center(rect), rect.Width / 2, rect.Height / 2);
    }

    private static void DrawBoxIcon(DrawingContext dc, ConditionalFormatIcon icon, Rect rect, Brush brush, Pen outline)
    {
        var inset = Math.Max(0, (icon.IconCount - 1 - icon.IconIndex) * rect.Width * 0.07);
        dc.DrawRectangle(brush, outline, new Rect(rect.Left + inset, rect.Top + inset, Math.Max(1, rect.Width - inset * 2), Math.Max(1, rect.Height - inset * 2)));
    }

    private static StreamGeometry CreateArrowGeometry(Rect rect, int iconIndex)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        if (iconIndex == 1)
        {
            context.BeginFigure(new Point(rect.Left, rect.Top + rect.Height / 2), true, true);
            context.LineTo(new Point(rect.Right - 3, rect.Top + rect.Height / 2), true, false);
            context.LineTo(new Point(rect.Right - 3, rect.Top + 2), true, false);
            context.LineTo(new Point(rect.Right, rect.Top + rect.Height / 2), true, false);
            context.LineTo(new Point(rect.Right - 3, rect.Bottom - 2), true, false);
            context.LineTo(new Point(rect.Right - 3, rect.Top + rect.Height / 2), true, false);
        }
        else if (iconIndex == 0)
        {
            context.BeginFigure(new Point(rect.Left + rect.Width / 2, rect.Bottom), true, true);
            context.LineTo(new Point(rect.Left + rect.Width / 2, rect.Top + 3), true, false);
            context.LineTo(new Point(rect.Left + 2, rect.Top + 3), true, false);
            context.LineTo(new Point(rect.Left + rect.Width / 2, rect.Top), true, false);
            context.LineTo(new Point(rect.Right - 2, rect.Top + 3), true, false);
            context.LineTo(new Point(rect.Left + rect.Width / 2, rect.Top + 3), true, false);
        }
        else
        {
            context.BeginFigure(new Point(rect.Left + rect.Width / 2, rect.Top), true, true);
            context.LineTo(new Point(rect.Left + rect.Width / 2, rect.Bottom - 3), true, false);
            context.LineTo(new Point(rect.Left + 2, rect.Bottom - 3), true, false);
            context.LineTo(new Point(rect.Left + rect.Width / 2, rect.Bottom), true, false);
            context.LineTo(new Point(rect.Right - 2, rect.Bottom - 3), true, false);
            context.LineTo(new Point(rect.Left + rect.Width / 2, rect.Bottom - 3), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreateTriangleGeometry(Rect rect, bool pointUp)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        if (pointUp)
        {
            context.BeginFigure(new Point(rect.Left + rect.Width / 2, rect.Top), true, true);
            context.LineTo(new Point(rect.Right, rect.Bottom), true, false);
            context.LineTo(new Point(rect.Left, rect.Bottom), true, false);
        }
        else
        {
            context.BeginFigure(new Point(rect.Left, rect.Top), true, true);
            context.LineTo(new Point(rect.Right, rect.Top), true, false);
            context.LineTo(new Point(rect.Left + rect.Width / 2, rect.Bottom), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreateDiamondGeometry(Rect rect)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(new Point(rect.Left + rect.Width / 2, rect.Top), true, true);
        context.LineTo(new Point(rect.Right, rect.Top + rect.Height / 2), true, false);
        context.LineTo(new Point(rect.Left + rect.Width / 2, rect.Bottom), true, false);
        context.LineTo(new Point(rect.Left, rect.Top + rect.Height / 2), true, false);
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreateFlagGeometry(Rect rect)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        var poleX = rect.Left + rect.Width * 0.25;
        context.BeginFigure(new Point(poleX, rect.Bottom), false, false);
        context.LineTo(new Point(poleX, rect.Top), true, false);
        context.BeginFigure(new Point(poleX, rect.Top + rect.Height * 0.08), true, true);
        context.LineTo(new Point(rect.Right, rect.Top + rect.Height * 0.18), true, false);
        context.LineTo(new Point(rect.Right - rect.Width * 0.18, rect.Top + rect.Height * 0.46), true, false);
        context.LineTo(new Point(poleX, rect.Top + rect.Height * 0.38), true, false);
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreateStarGeometry(Rect rect)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        var center = Center(rect);
        var outer = Math.Min(rect.Width, rect.Height) / 2;
        var inner = outer * 0.45;
        for (var i = 0; i < 10; i++)
        {
            var radius = i % 2 == 0 ? outer : inner;
            var angle = -Math.PI / 2 + i * Math.PI / 5;
            var point = new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
            if (i == 0)
                context.BeginFigure(point, true, true);
            else
                context.LineTo(point, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreatePieGeometry(Rect rect, double sweepFraction)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        var center = Center(rect);
        var radiusX = rect.Width / 2;
        var radiusY = rect.Height / 2;
        var sweep = Math.Clamp(sweepFraction, 0d, 1d) * Math.PI * 2;
        var start = -Math.PI / 2;
        var end = start + sweep;
        var startPoint = new Point(center.X, rect.Top);
        var endPoint = new Point(center.X + Math.Cos(end) * radiusX, center.Y + Math.Sin(end) * radiusY);

        context.BeginFigure(center, true, true);
        context.LineTo(startPoint, true, false);
        context.ArcTo(
            endPoint,
            new Size(radiusX, radiusY),
            0,
            sweep > Math.PI,
            SweepDirection.Clockwise,
            true,
            false);
        geometry.Freeze();
        return geometry;
    }
}
