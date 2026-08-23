using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class AccessibilityCheckerService
{
    private static void AddLowContrastChartTextIssues(
        List<AccessibilityIssue> issues,
        Workbook workbook,
        Sheet sheet,
        ChartModel chart)
    {
        var chartBackground = chart.ResolveChartAreaFillColor(workbook.Theme) ?? CellColor.White;
        var plotBackground = chart.ResolvePlotAreaFillColor(workbook.Theme) ?? chartBackground;
        var defaultText = chart.ChartDefaultTextThemeColor?.Resolve(workbook.Theme) ??
            chart.ChartDefaultTextColor ??
            CellColor.Black;

        AddLowContrastChartTextIssue(
            issues,
            sheet,
            chart,
            "Chart title",
            chart.Title,
            chart.ResolveChartTitleTextColor(workbook.Theme) ?? defaultText,
            chartBackground,
            chart.ChartTitleFontSize);

        AddLowContrastChartTextIssue(
            issues,
            sheet,
            chart,
            "X-axis title",
            chart.XAxisTitle,
            chart.ResolveAxisTitleTextColor(workbook.Theme) ?? defaultText,
            chartBackground,
            chart.AxisTitleFontSize);

        AddLowContrastChartTextIssue(
            issues,
            sheet,
            chart,
            "Y-axis title",
            chart.YAxisTitle,
            chart.ResolveAxisTitleTextColor(workbook.Theme) ?? defaultText,
            chartBackground,
            chart.AxisTitleFontSize);

        if (!chart.HideXAxis && chart.ShowXAxisLabels)
        {
            AddLowContrastChartTextIssue(
                issues,
                sheet,
                chart,
                "X-axis labels",
                "Axis labels",
                chart.ResolveXAxisLabelTextColor(workbook.Theme) ?? defaultText,
                chartBackground,
                chart.XAxisLabelFontSize);
        }

        if (!chart.HideYAxis && chart.ShowYAxisLabels)
        {
            AddLowContrastChartTextIssue(
                issues,
                sheet,
                chart,
                "Y-axis labels",
                "Axis labels",
                chart.ResolveYAxisLabelTextColor(workbook.Theme) ?? defaultText,
                chartBackground,
                chart.YAxisLabelFontSize);
        }

        if (chart.ShowLegend)
        {
            AddLowContrastChartTextIssue(
                issues,
                sheet,
                chart,
                "Legend text",
                "Legend",
                chart.ResolveLegendTextColor(workbook.Theme) ?? defaultText,
                chart.ResolveLegendFillColor(workbook.Theme) ?? chartBackground,
                chart.LegendFontSize);
        }

        if (chart.ShowDataLabels)
        {
            var dataLabelTextColor = chart.ResolveDataLabelTextColor(workbook.Theme) ?? defaultText;
            var dataLabelFillColor = chart.ResolveDataLabelFillColor(workbook.Theme) ?? plotBackground;

            AddLowContrastChartTextIssue(
                issues,
                sheet,
                chart,
                "Data label text",
                "Data labels",
                dataLabelTextColor,
                dataLabelFillColor,
                chart.DataLabelFontSize);

            AddLowContrastChartDataLabelOverrideIssues(
                issues,
                workbook,
                sheet,
                chart,
                dataLabelTextColor,
                dataLabelFillColor);
        }

        if (chart.DataTable is { } dataTable)
        {
            AddLowContrastChartTextIssue(
                issues,
                sheet,
                chart,
                "Chart data table text",
                "Data table",
                dataTable.TextThemeColor?.Resolve(workbook.Theme) ?? dataTable.TextColor ?? defaultText,
                dataTable.FillThemeColor?.Resolve(workbook.Theme) ?? dataTable.FillColor ?? chartBackground,
                dataTable.FontSize ?? chart.ChartDefaultFontSize);
        }

        if (chart.ShowLinearTrendline && (chart.ShowTrendlineEquation || chart.ShowTrendlineRSquared))
        {
            AddLowContrastChartTextIssue(
                issues,
                sheet,
                chart,
                "Trendline label text",
                "Trendline label",
                chart.TrendlineLabelTextThemeColor?.Resolve(workbook.Theme) ?? chart.TrendlineLabelTextColor ?? defaultText,
                chart.TrendlineLabelFillThemeColor?.Resolve(workbook.Theme) ?? chart.TrendlineLabelFillColor ?? chartBackground,
                chart.TrendlineLabelFontSize ?? chart.ChartDefaultFontSize);
        }
    }

    private static void AddLowContrastChartDataLabelOverrideIssues(
        List<AccessibilityIssue> issues,
        Workbook workbook,
        Sheet sheet,
        ChartModel chart,
        CellColor dataLabelTextColor,
        CellColor dataLabelFillColor)
    {
        var seriesFormatsByIndex = chart.SeriesDataLabelFormats
            .GroupBy(format => format.SeriesIndex)
            .ToDictionary(group => group.Key, group => group.Last());

        foreach (var seriesFormat in seriesFormatsByIndex.Values.OrderBy(format => format.SeriesIndex))
        {
            if (!HasSeriesDataLabelContrastOverride(seriesFormat) ||
                !IsSeriesDataLabelVisible(chart, seriesFormat))
            {
                continue;
            }

            AddLowContrastChartTextIssue(
                issues,
                sheet,
                chart,
                "Series data label text",
                "Data labels",
                seriesFormat.ResolveTextColor(workbook.Theme) ?? dataLabelTextColor,
                seriesFormat.ResolveFillColor(workbook.Theme) ?? dataLabelFillColor,
                seriesFormat.FontSize ?? chart.DataLabelFontSize);
        }

        foreach (var pointFormat in chart.PointDataLabelFormats
            .GroupBy(format => (format.SeriesIndex, format.PointIndex))
            .Select(group => group.Last())
            .OrderBy(format => format.SeriesIndex)
            .ThenBy(format => format.PointIndex))
        {
            if (pointFormat.IsDeleted == true ||
                !HasPointDataLabelContrastOverride(pointFormat) ||
                !IsPointDataLabelVisible(chart, pointFormat, seriesFormatsByIndex.GetValueOrDefault(pointFormat.SeriesIndex)))
            {
                continue;
            }

            seriesFormatsByIndex.TryGetValue(pointFormat.SeriesIndex, out var seriesFormat);
            var inheritedTextColor = seriesFormat?.ResolveTextColor(workbook.Theme) ?? dataLabelTextColor;
            var inheritedFillColor = seriesFormat?.ResolveFillColor(workbook.Theme) ?? dataLabelFillColor;
            var inheritedFontSize = seriesFormat?.FontSize ?? chart.DataLabelFontSize;

            AddLowContrastChartTextIssue(
                issues,
                sheet,
                chart,
                "Point data label text",
                "Data labels",
                pointFormat.ResolveTextColor(workbook.Theme) ?? inheritedTextColor,
                pointFormat.ResolveFillColor(workbook.Theme) ?? inheritedFillColor,
                pointFormat.FontSize ?? inheritedFontSize);
        }
    }

    private static bool HasSeriesDataLabelContrastOverride(ChartSeriesDataLabelFormat format) =>
        format.TextColor is not null ||
        format.TextThemeColor is not null ||
        format.FillColor is not null ||
        format.FillThemeColor is not null ||
        format.FontSize is not null;

    private static bool HasPointDataLabelContrastOverride(ChartPointDataLabelFormat format) =>
        format.TextColor is not null ||
        format.TextThemeColor is not null ||
        format.FillColor is not null ||
        format.FillThemeColor is not null ||
        format.FontSize is not null;

    private static bool IsSeriesDataLabelVisible(ChartModel chart, ChartSeriesDataLabelFormat format) =>
        (format.ShowValue ?? chart.ShowDataLabelValue) ||
        (format.ShowCategoryName ?? chart.ShowDataLabelCategoryName) ||
        (format.ShowSeriesName ?? chart.ShowDataLabelSeriesName) ||
        (format.ShowLegendKey ?? chart.ShowDataLabelLegendKey) ||
        (format.ShowPercentage ?? chart.ShowDataLabelPercentage) ||
        (format.ShowBubbleSize ?? chart.ShowDataLabelBubbleSize);

    private static bool IsPointDataLabelVisible(
        ChartModel chart,
        ChartPointDataLabelFormat format,
        ChartSeriesDataLabelFormat? seriesFormat) =>
        (format.ShowValue ?? seriesFormat?.ShowValue ?? chart.ShowDataLabelValue) ||
        (format.ShowCategoryName ?? seriesFormat?.ShowCategoryName ?? chart.ShowDataLabelCategoryName) ||
        (format.ShowSeriesName ?? seriesFormat?.ShowSeriesName ?? chart.ShowDataLabelSeriesName) ||
        (format.ShowLegendKey ?? seriesFormat?.ShowLegendKey ?? chart.ShowDataLabelLegendKey) ||
        (format.ShowPercentage ?? seriesFormat?.ShowPercentage ?? chart.ShowDataLabelPercentage) ||
        (format.ShowBubbleSize ?? seriesFormat?.ShowBubbleSize ?? chart.ShowDataLabelBubbleSize);

    private static void AddLowContrastChartTextIssue(
        List<AccessibilityIssue> issues,
        Sheet sheet,
        ChartModel chart,
        string textArea,
        string? text,
        CellColor textColor,
        CellColor background,
        double fontSize)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var minimumContrastRatio = MinimumTextContrastRatio(fontSize, bold: false);
        if (ContrastRatio(textColor, background) >= minimumContrastRatio)
            return;

        issues.Add(new AccessibilityIssue(
            AccessibilityIssueKind.LowContrastChartText,
            sheet.Id,
            sheet.Name,
            FormatRange(chart.DataRange),
            $"{textArea} should have at least {minimumContrastRatio:0.0}:1 contrast against its background."));
    }

    private static void AddLowContrastCellTextIssues(List<AccessibilityIssue> issues, Workbook workbook, Sheet sheet)
    {
        var occupiedCells = sheet.GetOccupiedCellMap();
        var conditionalFormats = new ConditionalFormatEvaluationSession(sheet, workbook, occupiedCells);
        Dictionary<StyleId, CellStyle>? workbookStyleCache = null;
        Dictionary<CellStyle, CellContrastCheck>? contrastCache = null;
        foreach (var entry in occupiedCells)
        {
            var (row, col) = entry.Key;
            var cell = entry.Value;
            if (!HasVisibleCellText(cell.Value))
                continue;

            var address = new CellAddress(sheet.Id, row, col);
            var baseStyle = GetCachedWorkbookStyle(workbook, ref workbookStyleCache, cell.StyleId);
            var style = conditionalFormats.EvaluateEffectiveStyle(address, cell.Value, baseStyle);
            var contrast = GetCellContrastCheck(style, workbook.Theme, ref contrastCache);
            if (contrast.HasSufficientContrast)
                continue;

            issues.Add(new AccessibilityIssue(
                AccessibilityIssueKind.LowContrastCellText,
                sheet.Id,
                sheet.Name,
                address.ToA1(),
                $"Cell text should have at least {contrast.MinimumContrastRatio:0.0}:1 contrast against its fill."));
        }
    }

    private static void AddLowContrastTextBoxTextIssue(
        List<AccessibilityIssue> issues,
        Workbook workbook,
        Sheet sheet,
        TextBoxModel textBox)
    {
        if (string.IsNullOrWhiteSpace(textBox.Text))
            return;

        // R131: a text box's own explicit/theme text-color override (Format Text Box > Font Color)
        // takes precedence over the workbook-wide object-default text color -- checking contrast
        // against the default while ignoring an authored override evaluates the wrong foreground
        // entirely, which can both miss a genuinely low-contrast override and falsely flag a
        // correctly-chosen override that merely differs from the default.
        var textColor = textBox.ResolveTextColor(workbook.Theme) ?? ResolveDefaultObjectTextColor(workbook.Theme);
        var background = textBox.GetEffectiveFillColor(workbook.Theme, ResolveDefaultObjectFillColor(workbook.Theme));
        var minimumContrastRatio = MinimumTextContrastRatio(DefaultObjectTextFontSize, bold: false);
        if (ContrastRatio(textColor, background) >= minimumContrastRatio)
            return;

        issues.Add(new AccessibilityIssue(
            AccessibilityIssueKind.LowContrastObjectText,
            sheet.Id,
            sheet.Name,
            textBox.Anchor.ToA1(),
            $"Text box text should have at least {minimumContrastRatio:0.0}:1 contrast against its fill."));
    }

    private static void AddLowContrastShapeTextIssue(
        List<AccessibilityIssue> issues,
        Workbook workbook,
        Sheet sheet,
        DrawingShapeModel shape)
    {
        // R131: the low-contrast rule previously never looked at DrawingShapeModel.ShapeText at
        // all -- the whole shape-text/color family was unreachable. Two guards keep this addition
        // from flooding the report with false positives (DO-NOT-WIDEN-PAST-THE-GUARD):
        //  - a shape with no text has nothing to check, exactly like the alt-text rule already
        //    skips it for its own purposes. HasShapeText only tests for an empty string (it also
        //    gates txBody serialization/rendering, where a whitespace-only run is a distinct
        //    state from "no text"), so a whitespace-only ShapeText is checked explicitly here,
        //    mirroring AddLowContrastTextBoxTextIssue's IsNullOrWhiteSpace guard above;
        //  - a shape with HasFill == false has no fixed on-screen background at all (Excel renders
        //    whatever is behind it -- grid, another shape, a picture); grading its text against a
        //    fabricated default fill would produce unfixable false positives, so it is exempt.
        if (string.IsNullOrWhiteSpace(shape.ShapeText) || !shape.HasFill)
            return;

        var textColor = shape.ResolveShapeTextColor(workbook.Theme) ?? ResolveDefaultObjectTextColor(workbook.Theme);
        var fillColor = shape.GetEffectiveFillColor(workbook.Theme, ResolveDefaultObjectFillColor(workbook.Theme));

        // Mirrors the cell-gradient-fill worst-stop rule: a shape gradient fill only stores its two
        // endpoints (no intermediate stops), so grade against whichever endpoint has the worse
        // (lower) contrast with the text color.
        var background = fillColor;
        if (shape.GradientFillEndColor is { } gradientEndColor &&
            ContrastRatio(textColor, gradientEndColor) < ContrastRatio(textColor, fillColor))
        {
            background = gradientEndColor;
        }

        var fontSize = shape.ShapeTextFontSizePoints > 0 ? shape.ShapeTextFontSizePoints : DefaultObjectTextFontSize;
        var minimumContrastRatio = MinimumTextContrastRatio(fontSize, shape.ShapeTextBold);
        if (ContrastRatio(textColor, background) >= minimumContrastRatio)
            return;

        issues.Add(new AccessibilityIssue(
            AccessibilityIssueKind.LowContrastObjectText,
            sheet.Id,
            sheet.Name,
            shape.Anchor.ToA1(),
            $"Shape text should have at least {minimumContrastRatio:0.0}:1 contrast against its fill."));
    }

    private static CellColor ResolveDefaultObjectTextColor(WorkbookTheme theme) =>
        theme.ObjectDefaults?.Text?.TextThemeColor?.Resolve(theme) ??
        theme.ObjectDefaults?.Text?.TextColor ??
        CellColor.Black;

    private static CellColor ResolveDefaultObjectFillColor(WorkbookTheme theme) =>
        theme.ObjectDefaults?.Shape?.FillThemeColor?.Resolve(theme) ??
        theme.ObjectDefaults?.Shape?.FillColor ??
        CellColor.White;

    private static CellStyle GetCachedWorkbookStyle(
        Workbook workbook,
        ref Dictionary<StyleId, CellStyle>? styleCache,
        StyleId styleId)
    {
        styleCache ??= [];
        if (!styleCache.TryGetValue(styleId, out var style))
        {
            style = workbook.GetStyle(styleId);
            styleCache[styleId] = style;
        }

        return style;
    }

    private static CellContrastCheck GetCellContrastCheck(
        CellStyle style,
        WorkbookTheme theme,
        ref Dictionary<CellStyle, CellContrastCheck>? contrastCache)
    {
        contrastCache ??= new Dictionary<CellStyle, CellContrastCheck>(CellStyleReferenceComparer.Instance);
        if (!contrastCache.TryGetValue(style, out var check))
        {
            var minimumContrastRatio = MinimumTextContrastRatio(style);
            check = new CellContrastCheck(
                minimumContrastRatio,
                HasSufficientCellTextContrast(style, theme, minimumContrastRatio));
            contrastCache[style] = check;
        }

        return check;
    }

    private static bool HasVisibleCellText(ScalarValue value) =>
        value switch
        {
            TextValue text => !string.IsNullOrWhiteSpace(text.Value),
            NumberValue or BoolValue or DateTimeValue or ErrorValue => true,
            _ => false
        };

    private static double ContrastRatio(CellColor first, CellColor second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double MinimumTextContrastRatio(CellStyle style) =>
        MinimumTextContrastRatio(style.FontSize, style.Bold);

    private static double MinimumTextContrastRatio(double fontSize, bool bold) =>
        fontSize >= 18 || (bold && fontSize >= 14)
            ? 3.0
            : 4.5;

    private static bool HasSufficientCellTextContrast(
        CellStyle style,
        WorkbookTheme theme,
        double minimumContrastRatio)
    {
        var fontColor = style.ResolveFontColor(theme);

        // R131: a gradient fill (OOXML <gradientFill>) paints a smooth blend of colors across the
        // cell rather than one solid background, and CellStyle.ResolveFillColor only understands
        // solid/theme fills -- it returns null for a gradient cell (FillColor/FillThemeColor are
        // both unset whenever GradientFill is populated; the two are mutually exclusive in OOXML).
        // That null previously fell straight through to a fabricated CellColor.White regardless of
        // what the gradient actually contains, so e.g. dark text on an all-dark gradient would be
        // silently reported as passing. Since text sitting anywhere in the cell must stay legible,
        // the defensible rule here is to grade against whichever gradient stop has the WORST (lowest)
        // contrast with the font color: if the text clears the bar against every stop it is legible
        // across the whole blend, and if it fails against the worst stop the cell genuinely has an
        // illegible patch somewhere on screen.
        if (style.GradientFill is { Stops.Count: > 0 } gradientFill)
        {
            var worstStopContrast = double.MaxValue;
            foreach (var stop in gradientFill.Stops)
            {
                var stopContrast = ContrastRatio(fontColor, stop.Color);
                if (stopContrast < worstStopContrast)
                    worstStopContrast = stopContrast;
            }

            return worstStopContrast >= minimumContrastRatio;
        }

        var baseFill = style.ResolveFillColor(theme) ?? CellColor.White;
        if (ContrastRatio(fontColor, baseFill) < minimumContrastRatio)
            return false;

        if (style.FillPatternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid)
            return true;

        var patternColor = style.ResolveFillPatternColor(theme) ?? CellColor.Black;
        if (TryGetGrayPatternOpacity(style.FillPatternStyle, out var opacity))
            return ContrastRatio(fontColor, Blend(patternColor, baseFill, opacity)) >= minimumContrastRatio;

        return ContrastRatio(fontColor, patternColor) >= minimumContrastRatio;
    }

    private static bool TryGetGrayPatternOpacity(CellFillPatternStyle patternStyle, out double opacity)
    {
        opacity = patternStyle switch
        {
            CellFillPatternStyle.Gray0625 => 0.12,
            CellFillPatternStyle.Gray125 => 0.18,
            CellFillPatternStyle.LightGray => 0.28,
            CellFillPatternStyle.MediumGray => 0.45,
            CellFillPatternStyle.DarkGray => 0.62,
            _ => double.NaN
        };
        return !double.IsNaN(opacity);
    }

    private static CellColor Blend(CellColor foreground, CellColor background, double opacity) => new(
        BlendChannel(foreground.R, background.R, opacity),
        BlendChannel(foreground.G, background.G, opacity),
        BlendChannel(foreground.B, background.B, opacity));

    private static byte BlendChannel(byte foreground, byte background, double opacity) =>
        (byte)Math.Round(foreground * opacity + background * (1 - opacity));

    private static double RelativeLuminance(CellColor color) =>
        0.2126 * LinearRgb(color.R) +
        0.7152 * LinearRgb(color.G) +
        0.0722 * LinearRgb(color.B);

    private static double LinearRgb(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.03928
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private readonly record struct CellContrastCheck(double MinimumContrastRatio, bool HasSufficientContrast);

    private sealed class CellStyleReferenceComparer : IEqualityComparer<CellStyle>
    {
        public static readonly CellStyleReferenceComparer Instance = new();

        public bool Equals(CellStyle? x, CellStyle? y) => ReferenceEquals(x, y);

        public int GetHashCode(CellStyle obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
