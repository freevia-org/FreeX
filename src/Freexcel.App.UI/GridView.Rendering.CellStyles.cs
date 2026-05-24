using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public partial class GridView
{
    private readonly record struct CellTypefaceKey(string FontName, bool Italic, bool Bold);

    private static void DrawBorderEdge(
        DrawingContext dc,
        CellBorder border,
        Point p1,
        Point p2,
        Dictionary<CellColor, SolidColorBrush>? brushCache = null,
        Dictionary<CellBorder, Pen>? borderPenCache = null)
    {
        if (border.Style == BorderStyle.None) return;

        if (borderPenCache is not null && borderPenCache.TryGetValue(border, out var cachedPen))
        {
            dc.DrawLine(cachedPen, p1, p2);
            return;
        }

        double thickness = border.Style switch
        {
            BorderStyle.Thin => 0.5,
            BorderStyle.Medium => 1.5,
            BorderStyle.Thick => 2.5,
            _ => 0.5
        };

        DashStyle dash = border.Style switch
        {
            BorderStyle.Dashed => DashStyles.Dash,
            BorderStyle.Dotted => DashStyles.Dot,
            _ => DashStyles.Solid
        };

        var pen = new Pen(BrushForCellColor(border.Color, brushCache), thickness) { DashStyle = dash };
        if (pen.CanFreeze)
            pen.Freeze();
        borderPenCache?.Add(border, pen);

        dc.DrawLine(pen, p1, p2);
    }

    private static SolidColorBrush BrushForCellColor(
        CellColor color,
        Dictionary<CellColor, SolidColorBrush>? brushCache = null)
    {
        if (brushCache is not null && brushCache.TryGetValue(color, out var cached))
            return cached;

        var brush = MakeBrush(color.R, color.G, color.B);
        brushCache?.Add(color, brush);
        return brush;
    }

    private static void DrawFillPattern(
        DrawingContext dc,
        Rect rect,
        CellStyle? style,
        Dictionary<CellColor, SolidColorBrush>? brushCache = null)
    {
        if (style is null || style.FillPatternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid)
            return;

        var color = style.FillPatternColor ?? CellColor.Black;
        var pen = new Pen(BrushForCellColor(color, brushCache), 0.75);
        const double step = 6;

        dc.PushClip(new RectangleGeometry(rect));
        switch (style.FillPatternStyle)
        {
            case CellFillPatternStyle.Gray0625:
            case CellFillPatternStyle.Gray125:
            case CellFillPatternStyle.LightGray:
            case CellFillPatternStyle.MediumGray:
            case CellFillPatternStyle.DarkGray:
                var opacity = style.FillPatternStyle switch
                {
                    CellFillPatternStyle.Gray0625 => 0.12,
                    CellFillPatternStyle.Gray125 => 0.18,
                    CellFillPatternStyle.LightGray => 0.28,
                    CellFillPatternStyle.MediumGray => 0.45,
                    _ => 0.62
                };
                dc.DrawRectangle(MakeBrushAlpha((byte)(opacity * 255), color.R, color.G, color.B), null, rect);
                break;
            case CellFillPatternStyle.LightHorizontal:
            case CellFillPatternStyle.DarkHorizontal:
                for (var y = rect.Top + step; y < rect.Bottom; y += step)
                    dc.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
                break;
            case CellFillPatternStyle.LightVertical:
            case CellFillPatternStyle.DarkVertical:
                for (var x = rect.Left + step; x < rect.Right; x += step)
                    dc.DrawLine(pen, new Point(x, rect.Top), new Point(x, rect.Bottom));
                break;
            case CellFillPatternStyle.LightGrid:
            case CellFillPatternStyle.DarkGrid:
                DrawFillPattern(dc, rect, new CellStyle { FillPatternStyle = CellFillPatternStyle.LightHorizontal, FillPatternColor = color }, brushCache);
                DrawFillPattern(dc, rect, new CellStyle { FillPatternStyle = CellFillPatternStyle.LightVertical, FillPatternColor = color }, brushCache);
                break;
            case CellFillPatternStyle.LightDown:
            case CellFillPatternStyle.DarkDown:
                DrawDiagonalPattern(dc, rect, pen, descending: true);
                break;
            case CellFillPatternStyle.LightUp:
            case CellFillPatternStyle.DarkUp:
                DrawDiagonalPattern(dc, rect, pen, descending: false);
                break;
            case CellFillPatternStyle.LightTrellis:
            case CellFillPatternStyle.DarkTrellis:
                DrawDiagonalPattern(dc, rect, pen, descending: true);
                DrawDiagonalPattern(dc, rect, pen, descending: false);
                break;
        }
        dc.Pop();
    }

    private static void DrawDiagonalPattern(DrawingContext dc, Rect rect, Pen pen, bool descending)
    {
        const double step = 8;
        for (var offset = -rect.Height; offset < rect.Width; offset += step)
        {
            var start = descending
                ? new Point(rect.Left + offset, rect.Top)
                : new Point(rect.Left + offset, rect.Bottom);
            var end = descending
                ? new Point(rect.Left + offset + rect.Height, rect.Bottom)
                : new Point(rect.Left + offset + rect.Height, rect.Top);
            dc.DrawLine(pen, start, end);
        }
    }

    public static TextDecorationCollection? BuildTextDecorations(CellStyle? style) =>
        CellTextDecorationPlanner.Build(style);

    public static Typeface CreateCellTypeface(CellStyle? style)
    {
        var key = CreateCellTypefaceKey(style);
        return CreateCellTypeface(key);
    }

    private static Typeface CreateCellTypeface(
        CellStyle? style,
        Dictionary<CellTypefaceKey, Typeface> typefaceCache)
    {
        var key = CreateCellTypefaceKey(style);
        if (typefaceCache.TryGetValue(key, out var cached))
            return cached;

        var typeface = CreateCellTypeface(key);
        typefaceCache.Add(key, typeface);
        return typeface;
    }

    private static CellTypefaceKey CreateCellTypefaceKey(CellStyle? style)
    {
        var fontName = string.IsNullOrWhiteSpace(style?.FontName)
            ? "Calibri"
            : style!.FontName;
        return new CellTypefaceKey(fontName, style?.Italic == true, style?.Bold == true);
    }

    private static Typeface CreateCellTypeface(CellTypefaceKey key)
    {
        var fontStyle = key.Italic ? FontStyles.Italic : FontStyles.Normal;
        var fontWeight = key.Bold ? FontWeights.Bold : FontWeights.Normal;

        return new Typeface(new FontFamily(key.FontName), fontStyle, fontWeight, FontStretches.Normal);
    }
}
