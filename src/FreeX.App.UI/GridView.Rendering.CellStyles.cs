using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private readonly record struct CellTypefaceKey(string FontName, FontStretch Stretch, bool Italic, bool Bold);
    private static readonly Lazy<HashSet<string>> AvailableCellFontNames = new(() =>
    {
        var names = Fonts.SystemFontFamilies
            .Select(font => font.Source)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows() &&
            File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "ARIALN.TTF")))
        {
            names.Add("Arial Narrow");
        }

        return names;
    });

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

    private static bool HasVisibleCellBorder(CellStyle style) =>
        style.BorderTop.Style != BorderStyle.None ||
        style.BorderBottom.Style != BorderStyle.None ||
        style.BorderLeft.Style != BorderStyle.None ||
        style.BorderRight.Style != BorderStyle.None;

    private static bool HasVisibleCellSurface(CellStyle style) =>
        style.FillColor.HasValue ||
        style.FillPatternStyle != CellFillPatternStyle.None;

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
        Dictionary<CellColor, SolidColorBrush>? brushCache = null,
        Dictionary<CellColor, Pen>? fillPatternPenCache = null)
    {
        if (style is null || style.FillPatternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid)
            return;

        var color = style.FillPatternColor ?? CellColor.Black;
        var pen = FillPatternPenForCellColor(color, brushCache, fillPatternPenCache);
        const double step = 6;

        dc.PushClip(FrozenRectangleGeometry(rect));
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
                DrawHorizontalPattern(dc, rect, pen, step);
                break;
            case CellFillPatternStyle.LightVertical:
            case CellFillPatternStyle.DarkVertical:
                DrawVerticalPattern(dc, rect, pen, step);
                break;
            case CellFillPatternStyle.LightGrid:
            case CellFillPatternStyle.DarkGrid:
                DrawHorizontalPattern(dc, rect, pen, step);
                DrawVerticalPattern(dc, rect, pen, step);
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

    private static Pen FillPatternPenForCellColor(
        CellColor color,
        Dictionary<CellColor, SolidColorBrush>? brushCache,
        Dictionary<CellColor, Pen>? fillPatternPenCache)
    {
        if (fillPatternPenCache is not null && fillPatternPenCache.TryGetValue(color, out var cached))
            return cached;

        var pen = new Pen(BrushForCellColor(color, brushCache), 0.75);
        if (pen.CanFreeze)
            pen.Freeze();
        fillPatternPenCache?.Add(color, pen);
        return pen;
    }

    private static RectangleGeometry FrozenRectangleGeometry(Rect rect)
    {
        var geometry = new RectangleGeometry(rect);
        geometry.Freeze();
        return geometry;
    }

    private static void DrawHorizontalPattern(DrawingContext dc, Rect rect, Pen pen, double step)
    {
        for (var y = rect.Top + step; y < rect.Bottom; y += step)
            dc.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
    }

    private static void DrawVerticalPattern(DrawingContext dc, Rect rect, Pen pen, double step)
    {
        for (var x = rect.Left + step; x < rect.Right; x += step)
            dc.DrawLine(pen, new Point(x, rect.Top), new Point(x, rect.Bottom));
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

    private Typeface CreateCellTypefaceWithTheme(CellStyle? style, Dictionary<CellTypefaceKey, Typeface> typefaceCache)
    {
        return CreateCellTypeface(CreateCellTypefaceKeyWithTheme(style), typefaceCache);
    }

    private static Typeface CreateCellTypeface(
        CellStyle? style,
        Dictionary<CellTypefaceKey, Typeface> typefaceCache)
    {
        return CreateCellTypeface(CreateCellTypefaceKey(style), typefaceCache);
    }

    private static Typeface CreateCellTypeface(
        CellTypefaceKey key,
        Dictionary<CellTypefaceKey, Typeface> typefaceCache)
    {
        if (typefaceCache.TryGetValue(key, out var cached))
            return cached;

        var typeface = CreateCellTypeface(key);
        typefaceCache.Add(key, typeface);
        return typeface;
    }

    private CellTypefaceKey CreateCellTypefaceKeyWithTheme(CellStyle? style)
    {
        var (fontName, stretch) = ResolveEffectiveCellFontForDisplay(style, WorkbookTheme);
        return new CellTypefaceKey(fontName, stretch, style?.Italic == true, style?.Bold == true);
    }

    private static CellTypefaceKey CreateCellTypefaceKey(CellStyle? style)
    {
        var (fontName, stretch) = ResolveCellFontForDisplay(style?.FontName);
        return new CellTypefaceKey(fontName, stretch, style?.Italic == true, style?.Bold == true);
    }

    /// <summary>
    /// Resolves the effective display font name for a cell by first consulting the font scheme
    /// (which may redirect to the workbook theme's minor or major font), then applying the
    /// availability fallback for fonts not installed on the system.
    /// </summary>
    internal static string ResolveEffectiveCellFontName(CellStyle? style, WorkbookTheme theme)
        => ResolveEffectiveCellFontForDisplay(style, theme).FontName;

    internal static string ResolveEffectiveCellFontName(
        CellStyle? style,
        WorkbookTheme theme,
        Func<string, bool> isAvailable)
        => ResolveEffectiveCellFontForDisplay(style, theme, isAvailable).FontName;

    private static (string FontName, FontStretch Stretch) ResolveEffectiveCellFontForDisplay(CellStyle? style, WorkbookTheme theme)
        => ResolveEffectiveCellFontForDisplay(style, theme, AvailableCellFontNames.Value.Contains);

    private static (string FontName, FontStretch Stretch) ResolveEffectiveCellFontForDisplay(
        CellStyle? style,
        WorkbookTheme theme,
        Func<string, bool> isAvailable)
    {
        if (style is null)
            return ResolveCellFontForDisplay(null, isAvailable);

        var rawName = ResolveEffectiveCellRawFontName(style, theme);
        return ResolveCellFontForDisplay(rawName, isAvailable);
    }

    private static string? ResolveEffectiveCellRawFontName(CellStyle style, WorkbookTheme theme)
    {
        var explicitName = string.IsNullOrWhiteSpace(style.FontName) ? null : style.FontName.Trim();
        var schemeName = theme.ResolveSchemeFontName(style.FontScheme);
        if (schemeName is null)
            return explicitName;

        if (explicitName is null)
            return schemeName;

        // Excel-authored workbooks can persist the resolved concrete face together with a theme
        // scheme marker. Preserve that explicit face for modern fonts such as Aptos Narrow; legacy
        // Calibri placeholders still follow the workbook theme so Theme Fonts changes are honored.
        return IsLegacyThemeFontPlaceholder(explicitName) ? schemeName : explicitName;
    }

    private static bool IsLegacyThemeFontPlaceholder(string fontName) =>
        string.Equals(fontName, "Calibri", StringComparison.OrdinalIgnoreCase);

    internal static string ResolveCellFontNameForDisplay(string? fontName) =>
        ResolveCellFontNameForDisplay(fontName, AvailableCellFontNames.Value.Contains);

    internal static string ResolveCellFontNameForDisplay(string? fontName, Func<string, bool> isAvailable)
        => ResolveCellFontForDisplay(fontName, isAvailable).FontName;

    internal static FontStretch ResolveCellFontStretchForDisplay(string? fontName, Func<string, bool> isAvailable)
        => ResolveCellFontForDisplay(fontName, isAvailable).Stretch;

    private static (string FontName, FontStretch Stretch) ResolveCellFontForDisplay(string? fontName) =>
        ResolveCellFontForDisplay(fontName, AvailableCellFontNames.Value.Contains);

    private static (string FontName, FontStretch Stretch) ResolveCellFontForDisplay(
        string? fontName,
        Func<string, bool> isAvailable)
    {
        var requested = string.IsNullOrWhiteSpace(fontName) ? "Calibri" : fontName.Trim();
        if (isAvailable(requested))
            return (requested, FontStretches.Normal);

        if (!string.Equals(requested, "Aptos Narrow", StringComparison.OrdinalIgnoreCase))
            return (requested, FontStretches.Normal);

        // Excel's Aptos Narrow face is not always visible to WPF even when Office can render it.
        // Calibri with condensed stretch better matches Excel's weight than the lighter Arial Narrow face.
        if (isAvailable("Calibri"))
            return ("Calibri", FontStretches.Condensed);

        foreach (var fallback in new[] { "Arial Narrow", "Liberation Sans Narrow", "Nimbus Sans Narrow" })
        {
            if (isAvailable(fallback))
                return (fallback, FontStretches.Normal);
        }

        return (requested, FontStretches.Normal);
    }

    private static Typeface CreateCellTypeface(CellTypefaceKey key)
    {
        var fontStyle = key.Italic ? FontStyles.Italic : FontStyles.Normal;
        var fontWeight = key.Bold ? FontWeights.Bold : FontWeights.Normal;

        return new Typeface(new FontFamily(key.FontName), fontStyle, fontWeight, key.Stretch);
    }
}
