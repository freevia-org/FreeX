using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.Rendering;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;
using System.Linq;

namespace FreeX.App.UI;

public partial class GridView
{
    private readonly record struct CellTypefaceKey(string FontName, FontStretch Stretch, bool Italic, bool Bold);

    private double GetBorderEffectivePixelsPerDip()
    {
        var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;
        return BorderStrokePixelSnapper.NormalizePixelsPerDip(VisualTreeHelper.GetDpi(this).PixelsPerDip * zoom);
    }

    private static string? GetAptosNarrowCloudFontDir()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "FontCache", "4", "CloudFonts", "Aptos Narrow");
        try
        {
            return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.ttf").Any() ? dir : null;
        }
        catch
        {
            return null;
        }
    }

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

        if (GetAptosNarrowCloudFontDir() is not null)
        {
            names.Add("Aptos Narrow");
        }

        return names;
    });

    private static readonly Lazy<IReadOnlyDictionary<string, FontFamily>> CloudFontFamilies = new(() =>
    {
        var dict = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
        var dir = GetAptosNarrowCloudFontDir();
        if (dir is not null)
        {
            // WPF folder-URI FontFamily: the directory URI must end with a backslash
            var dirUri = new Uri(dir.TrimEnd('\\', '/') + "\\");
            dict["Aptos Narrow"] = new FontFamily(dirUri, "./#Aptos Narrow");
        }
        return dict;
    });

    private static void DrawBorderEdge(
        DrawingContext dc,
        CellBorder border,
        Point p1,
        Point p2,
        Dictionary<CellColor, SolidColorBrush>? brushCache = null,
        Dictionary<CellBorder, Pen>? borderPenCache = null,
        double effectivePixelsPerDip = 1.0)
    {
        if (border.Style == BorderStyle.None) return;

        var rawThickness = GetBorderThickness(border.Style);
        var thickness = BorderStrokePixelSnapper.SnapThickness(rawThickness, effectivePixelsPerDip);

        Pen pen;
        if (borderPenCache is not null &&
            borderPenCache.TryGetValue(border, out var cachedPen) &&
            Math.Abs(cachedPen.Thickness - thickness) < 0.0001)
        {
            pen = cachedPen;
        }
        else
        {
            DashStyle dash = border.Style switch
            {
                BorderStyle.Dashed or BorderStyle.MediumDashed => DashStyles.Dash,
                BorderStyle.Dotted => DashStyles.Dot,
                BorderStyle.DashDot or BorderStyle.MediumDashDot => DashStyles.DashDot,
                BorderStyle.DashDotDot or BorderStyle.MediumDashDotDot => DashStyles.DashDotDot,
                BorderStyle.SlantDashDot => DashStyles.DashDot,   // WPF approximation of slant dash-dot
                BorderStyle.Hair => DashStyles.Solid,
                _ => DashStyles.Solid
            };

            pen = new Pen(BrushForCellColor(border.Color, brushCache), thickness) { DashStyle = dash };
            if (pen.CanFreeze)
                pen.Freeze();
            if (borderPenCache is not null)
                borderPenCache[border] = pen;
        }

        if (border.Style == BorderStyle.Double)
        {
            DrawDoubleBorderLines(dc, pen, p1, p2, rawThickness, effectivePixelsPerDip);
            return;
        }

        var (start, end) = SnapAxisAlignedBorderLine(p1, p2, pen.Thickness, effectivePixelsPerDip);
        dc.DrawLine(pen, start, end);
    }

    private static double GetBorderThickness(BorderStyle style) => style switch
    {
        BorderStyle.Hair => 0.25,
        BorderStyle.Thin => 0.5,
        BorderStyle.Medium or BorderStyle.MediumDashed or BorderStyle.MediumDashDot or BorderStyle.MediumDashDotDot or BorderStyle.SlantDashDot => 1.5,
        BorderStyle.Thick => 2.5,
        _ => 0.5
    };

    private static (Point Start, Point End) SnapAxisAlignedBorderLine(
        Point p1,
        Point p2,
        double snappedThickness,
        double effectivePixelsPerDip)
    {
        if (Math.Abs(p1.Y - p2.Y) < 0.0001)
        {
            var y = BorderStrokePixelSnapper.SnapCenter(p1.Y, snappedThickness, effectivePixelsPerDip);
            return (new Point(p1.X, y), new Point(p2.X, y));
        }

        if (Math.Abs(p1.X - p2.X) < 0.0001)
        {
            var x = BorderStrokePixelSnapper.SnapCenter(p1.X, snappedThickness, effectivePixelsPerDip);
            return (new Point(x, p1.Y), new Point(x, p2.Y));
        }

        return (p1, p2);
    }

    /// <summary>
    /// Renders Excel's "Double" border style as two thin parallel lines separated by a small
    /// gap, straddling the requested edge, instead of a single solid line. Works for horizontal,
    /// vertical, and diagonal edges alike by offsetting perpendicular to the edge direction, so
    /// the same helper serves <see cref="BorderStyle.Double"/> top/bottom/left/right edges as
    /// well as diagonal borders.
    /// </summary>
    private static void DrawDoubleBorderLines(
        DrawingContext dc,
        Pen pen,
        Point p1,
        Point p2,
        double rawThickness,
        double effectivePixelsPerDip)
    {
        if (TryDrawAxisAlignedDoubleBorderLines(dc, pen, p1, p2, rawThickness, effectivePixelsPerDip))
            return;

        const double gap = 1.0; // DIPs between the centerlines of the two strokes

        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6)
        {
            // Degenerate (zero-length) edge: nothing to offset a second line against.
            dc.DrawLine(pen, p1, p2);
            return;
        }

        var offsetX = -dy / length * (gap / 2.0);
        var offsetY = dx / length * (gap / 2.0);

        dc.DrawLine(pen, new Point(p1.X + offsetX, p1.Y + offsetY), new Point(p2.X + offsetX, p2.Y + offsetY));
        dc.DrawLine(pen, new Point(p1.X - offsetX, p1.Y - offsetY), new Point(p2.X - offsetX, p2.Y - offsetY));
    }

    private static bool TryDrawAxisAlignedDoubleBorderLines(
        DrawingContext dc,
        Pen pen,
        Point p1,
        Point p2,
        double rawThickness,
        double effectivePixelsPerDip)
    {
        var scale = BorderStrokePixelSnapper.NormalizePixelsPerDip(effectivePixelsPerDip);
        var linePixels = BorderStrokePixelSnapper.SnapThicknessToDevicePixels(rawThickness, scale);
        if (linePixels <= 0)
            return false;

        var gapPixels = Math.Max(1, (int)Math.Round(CellBorderDoubleGap * scale, MidpointRounding.AwayFromZero));
        var totalThickness = ((linePixels * 2.0) + gapPixels) / scale;
        var offset = (linePixels + gapPixels) / (2.0 * scale);

        if (Math.Abs(p1.Y - p2.Y) < 0.0001)
        {
            var center = BorderStrokePixelSnapper.SnapCenter(p1.Y, totalThickness, scale);
            dc.DrawLine(pen, new Point(p1.X, center - offset), new Point(p2.X, center - offset));
            dc.DrawLine(pen, new Point(p1.X, center + offset), new Point(p2.X, center + offset));
            return true;
        }

        if (Math.Abs(p1.X - p2.X) < 0.0001)
        {
            var center = BorderStrokePixelSnapper.SnapCenter(p1.X, totalThickness, scale);
            dc.DrawLine(pen, new Point(center - offset, p1.Y), new Point(center - offset, p2.Y));
            dc.DrawLine(pen, new Point(center + offset, p1.Y), new Point(center + offset, p2.Y));
            return true;
        }

        return false;
    }

    private const double CellBorderDoubleGap = 1.0;

    private static bool HasVisibleCellBorder(CellStyle style) =>
        style.BorderTop.Style != BorderStyle.None ||
        style.BorderBottom.Style != BorderStyle.None ||
        style.BorderLeft.Style != BorderStyle.None ||
        style.BorderRight.Style != BorderStyle.None ||
        style.BorderDiagonalDown.Style != BorderStyle.None ||
        style.BorderDiagonalUp.Style != BorderStyle.None;

    // theme is accepted (rather than reading style.FillColor alone) because a cell whose fill was
    // set purely via a Theme Color picker (FillThemeColor with no baked FillColor -- see
    // StyleDiff.Apply in CellStyle.cs, which sets FillThemeColor without ever baking FillColor)
    // would otherwise be invisible to this presence check and get silently dropped from
    // BuildRenderCellStyleLookup, so DrawCellSurface would never even be called for it.
    // ResolveFillColor always returns a value once either field is set, for any theme, so this
    // presence result is theme-independent and safe to cache across theme swaps.
    private static bool HasVisibleCellSurface(CellStyle style, WorkbookTheme theme) =>
        style.ResolveFillColor(theme).HasValue ||
        style.FillPatternStyle != CellFillPatternStyle.None ||
        style.GradientFill is not null;

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
        WorkbookTheme theme,
        Dictionary<CellColor, SolidColorBrush>? brushCache = null,
        Dictionary<CellColor, Pen>? fillPatternPenCache = null)
    {
        if (style is null || style.FillPatternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid)
            return;

        // Re-resolve against the CURRENT theme rather than reading the baked FillPatternColor
        // directly: a theme-bound pattern color (FillPatternThemeColor) must repaint after a
        // Theme Colors swap, matching CellStyle.ResolveFillPatternColor's contract and the
        // font/fill-color fix applied throughout this file (see ResolveFontColor/ResolveFillColor
        // call sites in GridView.Rendering.cs).
        var color = style.ResolveFillPatternColor(theme) ?? CellColor.Black;
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

    /// <summary>
    /// Builds a WPF <see cref="Brush"/> for a cell gradient fill.
    /// <para>
    /// Excel's <c>degree</c> attribute measures the angle clockwise from the left edge (3 o'clock position).
    /// WPF <see cref="LinearGradientBrush"/> uses a Start/End point in [0,1]×[0,1] coordinates where (0,0)
    /// is top-left and (1,1) is bottom-right.
    /// </para>
    /// <para>
    /// Conversion: let θ = degree in radians. The gradient axis passes through the cell center (0.5, 0.5).
    /// StartPoint = center − 0.5 × (cos θ, sin θ), EndPoint = center + 0.5 × (cos θ, sin θ).
    /// Y is inverted because WPF's Y axis points down while math convention points up.
    /// </para>
    /// For path gradients we fall back to a radial brush centred on the fill origin insets.
    /// </summary>
    private static Brush BuildCellGradientBrush(CellGradientFill gradient)
    {
        if (gradient.Type == CellGradientFillType.Path)
        {
            // Path gradient: approximate as a radial gradient from the inset origin
            var originX = gradient.Left + (1.0 - gradient.Left - gradient.Right) / 2.0;
            var originY = gradient.Top  + (1.0 - gradient.Top  - gradient.Bottom) / 2.0;
            var brush = new RadialGradientBrush
            {
                Center   = new Point(originX, originY),
                GradientOrigin = new Point(originX, originY),
                RadiusX  = Math.Max(originX, 1.0 - originX),
                RadiusY  = Math.Max(originY, 1.0 - originY),
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
            };
            foreach (var stop in gradient.Stops.OrderBy(s => s.Position))
            {
                brush.GradientStops.Add(new GradientStop(
                    Color.FromRgb(stop.Color.R, stop.Color.G, stop.Color.B),
                    stop.Position));
            }
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }

        // Linear gradient — convert Excel degree to WPF StartPoint/EndPoint.
        // Excel degree: 0 = left→right, 90 = top→bottom, 180 = right→left, 270 = bottom→top.
        // WPF: (0,0)=top-left, (1,1)=bottom-right. Y increases downward.
        // Math (Y-down): angle from +X-right axis clockwise = degree.
        // cos(degree), sin(degree) give the direction vector in Y-down space.
        var radians = gradient.Degree * Math.PI / 180.0;
        var dx = Math.Cos(radians);
        var dy = Math.Sin(radians); // positive = downward in WPF
        var start = new Point(0.5 - 0.5 * dx, 0.5 - 0.5 * dy);
        var end   = new Point(0.5 + 0.5 * dx, 0.5 + 0.5 * dy);

        var lgBrush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var stop in gradient.Stops.OrderBy(s => s.Position))
        {
            lgBrush.GradientStops.Add(new GradientStop(
                Color.FromRgb(stop.Color.R, stop.Color.G, stop.Color.B),
                stop.Position));
        }
        if (lgBrush.CanFreeze) lgBrush.Freeze();
        return lgBrush;
    }

    public static TextDecorationCollection? BuildTextDecorations(CellStyle? style) =>
        CellTextDecorationPlanner.Build(style);

    /// <summary>
    /// Returns the adjusted font size and vertical baseline offset (in WPF device-independent pixels)
    /// to apply when a cell has <see cref="CellStyle.Superscript"/> or <see cref="CellStyle.Subscript"/>
    /// set. A negative <paramref name="baselineOffsetPx"/> shifts the text upward (superscript);
    /// a positive value shifts it downward (subscript). Returns the unchanged values when neither flag is set.
    /// </summary>
    /// <param name="style">The cell style (may be null).</param>
    /// <param name="displayFontSize">The already-resolved display font size (in WPF units) before any super/sub scaling.</param>
    /// <param name="adjustedFontSize">The font size to use when creating <see cref="System.Windows.Media.FormattedText"/>.</param>
    /// <param name="baselineOffsetPx">The Y-axis shift to apply to the text draw point.</param>
    internal static void ResolveSuperSubFontAdjustment(
        CellStyle? style,
        double displayFontSize,
        out double adjustedFontSize,
        out double baselineOffsetPx)
    {
        if (style?.Superscript == true)
        {
            baselineOffsetPx = -displayFontSize * SuperScriptBaselineRatio;
            adjustedFontSize = displayFontSize * SuperSubFontSizeFactor;
        }
        else if (style?.Subscript == true)
        {
            baselineOffsetPx = displayFontSize * SubScriptBaselineRatio;
            adjustedFontSize = displayFontSize * SuperSubFontSizeFactor;
        }
        else
        {
            adjustedFontSize = displayFontSize;
            baselineOffsetPx = 0;
        }
    }

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

        var fontFamily = CloudFontFamilies.Value.TryGetValue(key.FontName, out var cloudFamily)
            ? cloudFamily
            : new FontFamily(key.FontName);

        return new Typeface(fontFamily, fontStyle, fontWeight, key.Stretch);
    }

    /// <summary>
    /// Applies per-run rich-text formatting to an already-constructed <see cref="FormattedText"/>.
    /// Each <see cref="ResolvedCellTextRun"/> is mapped to a contiguous character range
    /// [<c>offset</c>, <c>offset + Text.Length</c>) and the WPF range APIs are invoked to override
    /// font weight, style, size, foreground brush, and text decorations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The method is a no-op when <paramref name="runs"/> is empty or null.
    /// </para>
    /// <para>
    /// <b>Superscript / subscript baseline:</b>
    /// <see cref="FormattedText"/> has no per-range baseline-offset API, so for super/sub runs
    /// only the font size is reduced (to <see cref="ResolvedCellTextRun.RenderedFontSize"/>).
    /// The vertical position of individual glyphs within the line is therefore approximate:
    /// they sit on the same baseline as the rest of the run but at a smaller point size, which
    /// visually reads as raised/lowered relative to full-size neighbours.
    /// For exact per-run baseline shifting, the caller would need to split the FormattedText and
    /// draw each segment at a different Y — deferred to a future wave if needed.
    /// </para>
    /// </remarks>
    /// <param name="text">The <see cref="FormattedText"/> to decorate in-place.</param>
    /// <param name="runs">Resolved runs from <see cref="CellRichRunLayoutPlanner.Resolve"/>.</param>
    /// <param name="brushCache">Shared brush cache; may be null (brushes are created on demand).</param>
    internal static void ApplyRichRunFormatting(
        FormattedText text,
        IReadOnlyList<ResolvedCellTextRun> runs,
        Dictionary<CellColor, SolidColorBrush>? brushCache)
    {
        if (runs.Count == 0) return;

        var offset = 0;
        foreach (var run in runs)
        {
            var len = run.Text.Length;
            if (len == 0) { continue; }

            // Clamp to actual text length (guard against round-trip mismatch).
            var textLen = text.Text.Length;
            if (offset >= textLen) break;
            var safeLen = Math.Min(len, textLen - offset);

            // Font weight and style.
            text.SetFontWeight(run.Bold ? FontWeights.Bold : FontWeights.Normal, offset, safeLen);
            text.SetFontStyle(run.Italic ? FontStyles.Italic : FontStyles.Normal, offset, safeLen);

            // Font size (includes super/sub scaling from the planner).
            text.SetFontSize(run.RenderedFontSize, offset, safeLen);

            // Font family (per-run font name).
            var fontFamily = CloudFontFamilies.Value.TryGetValue(run.FontName, out var cloudFam)
                ? cloudFam
                : new FontFamily(run.FontName);
            text.SetFontFamily(fontFamily, offset, safeLen);

            // Foreground color.
            var brush = BrushForCellColor(run.FontColor, brushCache);
            text.SetForegroundBrush(brush, offset, safeLen);

            // Text decorations (underline, strikethrough).
            var decorations = BuildRunTextDecorations(run);
            if (decorations is not null)
                text.SetTextDecorations(decorations, offset, safeLen);

            offset += len;
        }
    }

    private static TextDecorationCollection? BuildRunTextDecorations(ResolvedCellTextRun run)
    {
        if (!run.Underline && !run.Strikethrough) return null;
        var list = new TextDecorationCollection();
        if (run.Underline)
            list.Add(TextDecorations.Underline);
        if (run.Strikethrough)
            list.Add(TextDecorations.Strikethrough);
        list.Freeze();
        return list;
    }
}
