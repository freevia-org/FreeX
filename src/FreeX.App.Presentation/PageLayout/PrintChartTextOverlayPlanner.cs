using System.Globalization;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>The text measurement result needed to make print-chart overlay placement decisions.</summary>
public readonly record struct PrintChartOverlayTextMetrics(
    double Width,
    double WidthIncludingTrailingWhitespace);

/// <summary>Measures one print-chart overlay text run at the requested font size.</summary>
public delegate PrintChartOverlayTextMetrics PrintChartTextMeasure(string text, double fontSize);

public enum PrintChartTextOverlayRole
{
    Unknown,
    ChartTitle,
    CategoryAxisTitle,
    ValueAxisTitle,
    LegendEntry,
    CategoryTickLabel,
    ValueTickLabel,
    DataLabel
}

/// <summary>One selectable text overlay for a printed chart, in page-space device-independent units.</summary>
public sealed record PrintChartTextOverlayPlan(
    string Text,
    double X,
    double Y,
    double FontSize,
    PresentationRgb Color,
    double RotationDegrees,
    PrintChartTextOverlayRole Role = PrintChartTextOverlayRole.Unknown);

/// <summary>
/// UI-free planning for selectable text overlays over printed chart bitmaps. Renderers still own
/// chart bitmap rendering, platform text measurement, and PDF/native overlay realization; this
/// planner owns the content, chart-family branching, truncation, and placement policy.
/// </summary>
public static class PrintChartTextOverlayPlanner
{
    public const string FontFamily = "Segoe UI";
    public const int MaxLegendEntries = 12;
    public const int MaxValueAxisTickLabels = 6;
    public const int MaxDataLabelOverlays = 80;
    public const string Ellipsis = "\u2026";

    private static readonly PresentationRgb Black = new(0, 0, 0);

    private readonly record struct PrintedChartSeries(string Name, int Index, IReadOnlyList<PrintedChartPoint> Points);

    private readonly record struct PrintedChartPoint(string Category, int Index, double Value);

    private readonly record struct PrintedChartValueRange(double Minimum, double Maximum);

    public static IReadOnlyList<PrintChartTextOverlayPlan> Build(
        ChartModel chart,
        WorkbookTheme workbookTheme,
        LayoutRect chartRect,
        IReadOnlyList<ChartDataCell>? chartDataCells,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> pageCellLookup,
        PrintChartTextMeasure measureText,
        Sheet? dataSheet)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(workbookTheme);
        ArgumentNullException.ThrowIfNull(pageCellLookup);
        ArgumentNullException.ThrowIfNull(measureText);

        var overlays = new List<PrintChartTextOverlayPlan>();
        AddTitleAndAxisOverlays(overlays, chart, workbookTheme, chartRect, measureText);
        AddNonTitleOverlays(overlays, chart, workbookTheme, chartRect, chartDataCells, pageCellLookup, measureText, dataSheet);
        return overlays;
    }

    private static void AddTitleAndAxisOverlays(
        ICollection<PrintChartTextOverlayPlan> overlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        LayoutRect chartRect,
        PrintChartTextMeasure measureText)
    {
        var textInset = Math.Min(8, Math.Max(0, chartRect.Width / 20));
        AddCenteredOverlay(
            overlays,
            chart.Title,
            chartRect.Left + chartRect.Width / 2,
            chartRect.Top + textInset,
            Math.Max(1, chartRect.Width - textInset * 2),
            NormalizeFontSize(chart.ChartTitleFontSize, 16),
            ResolveChartTitleOverlayColor(chart, workbookTheme),
            rotationDegrees: 0,
            role: PrintChartTextOverlayRole.ChartTitle,
            measureText);

        if (!ChartTypeSupport.SupportsAxes(chart.Type))
            return;

        var axisFontSize = NormalizeFontSize(chart.AxisTitleFontSize, 12);
        var axisColor = ResolveAxisTitleOverlayColor(chart, workbookTheme);
        if (!chart.HideXAxis)
        {
            AddCenteredOverlay(
                overlays,
                chart.XAxisTitle,
                chartRect.Left + chartRect.Width / 2,
                chartRect.Bottom - axisFontSize - textInset,
                Math.Max(1, chartRect.Width - textInset * 2),
                axisFontSize,
                axisColor,
                rotationDegrees: 0,
                role: PrintChartTextOverlayRole.CategoryAxisTitle,
                measureText);
        }

        if (!chart.HideYAxis)
        {
            AddVerticalAxisOverlay(
                overlays,
                chart.YAxisTitle,
                chartRect.Left + textInset,
                chartRect.Top + chartRect.Height / 2,
                Math.Max(1, chartRect.Height - textInset * 2),
                axisFontSize,
                axisColor,
                PrintChartTextOverlayRole.ValueAxisTitle,
                measureText);
        }
    }

    private static void AddNonTitleOverlays(
        ICollection<PrintChartTextOverlayPlan> overlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        LayoutRect chartRect,
        IReadOnlyList<ChartDataCell>? chartDataCells,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> pageCellLookup,
        PrintChartTextMeasure measureText,
        Sheet? dataSheet)
    {
        if (IsPieFamily(chart.Type))
        {
            AddPieFamilyTextOverlays(overlays, chart, workbookTheme, chartRect, chartDataCells, pageCellLookup, measureText, dataSheet);
            return;
        }

        if (!SupportsNonTitleTextOverlays(chart.Type))
            return;

        var cellLookup = BuildCellLookup(chart, chartDataCells, pageCellLookup, dataSheet);
        var categories = BuildCategories(chart, cellLookup);
        var series = BuildSeries(chart, cellLookup, categories);
        if (series.Count == 0)
            return;

        var plotRect = EstimatePlotRect(chart, chartRect);
        var valueRange = GetValueRange(chart, series);
        AddLegendEntryOverlays(overlays, chart, workbookTheme, chartRect, series, measureText);
        AddCategoryTickLabelOverlays(overlays, chart, workbookTheme, chartRect, plotRect, categories, measureText);
        if (valueRange is { } range)
        {
            AddValueAxisTickLabelOverlays(overlays, chart, workbookTheme, chartRect, plotRect, range, measureText);
            AddDataLabelOverlays(overlays, chart, workbookTheme, plotRect, range, series, measureText);
        }
    }

    private static bool SupportsNonTitleTextOverlays(ChartType chartType) =>
        chartType is ChartType.Column
            or ChartType.ThreeDColumn
            or ChartType.StackedColumn
            or ChartType.PercentStackedColumn
            or ChartType.Line
            or ChartType.ThreeDLine
            or ChartType.Area
            or ChartType.ThreeDArea
            or ChartType.Bar
            or ChartType.ThreeDBar
            or ChartType.StackedBar
            or ChartType.PercentStackedBar;

    private static bool IsPieFamily(ChartType chartType) =>
        chartType is ChartType.Pie or ChartType.ThreeDPie or ChartType.Doughnut;

    private static void AddPieFamilyTextOverlays(
        ICollection<PrintChartTextOverlayPlan> overlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        LayoutRect chartRect,
        IReadOnlyList<ChartDataCell>? chartDataCells,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> pageCellLookup,
        PrintChartTextMeasure measureText,
        Sheet? dataSheet)
    {
        var cellLookup = BuildCellLookup(chart, chartDataCells, pageCellLookup, dataSheet);
        var categories = BuildCategories(chart, cellLookup);
        if (BuildPieSeries(chart, cellLookup, categories) is not { } pieSeries)
            return;

        var plotRect = EstimatePlotRect(chart, chartRect);
        var legendEntries = pieSeries.Points
            .Select(point => new PrintedChartSeries(point.Category, point.Index, [point]))
            .ToList();
        AddLegendEntryOverlays(overlays, chart, workbookTheme, chartRect, legendEntries, measureText);
        AddPieDataLabelOverlays(overlays, chart, workbookTheme, plotRect, pieSeries, measureText);
    }

    private static PrintedChartSeries? BuildPieSeries(
        ChartModel chart,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyList<string> categories)
    {
        var valueColumns = ChartTypeSupport.GetYAxisValueColumns(chart);
        if (valueColumns.Count == 0)
            return null;

        var valueColumn = valueColumns[0];
        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        if (dataStartRow > chart.DataRange.End.Row)
            return null;

        var points = new List<PrintedChartPoint>();
        var pointIndex = 0;
        for (var row = dataStartRow; row <= chart.DataRange.End.Row; row++, pointIndex++)
        {
            if (!cellLookup.TryGetValue((row, valueColumn), out var cell) ||
                !TryGetNumericValue(cell, out var value))
            {
                continue;
            }

            var category = pointIndex < categories.Count && !string.IsNullOrWhiteSpace(categories[pointIndex])
                ? categories[pointIndex]
                : string.Create(CultureInfo.InvariantCulture, $"Slice {pointIndex + 1}");
            points.Add(new PrintedChartPoint(category, pointIndex, value));
        }

        return points.Count == 0
            ? null
            : new PrintedChartSeries(GetSeriesName(chart, cellLookup, valueColumn, 0), 0, points);
    }

    /// <summary>
    /// Seeds the overlay lookup from the printed page's own cells, then overlays the viewport's
    /// authoritative <see cref="ChartDataCell"/> values on top.
    ///
    /// <paramref name="pageCellLookup"/> is built for the PAGE, not for the chart, so it carries real
    /// values for cells the chart itself must not read: the printed page's hidden merge-anchor rows
    /// (<c>ViewportService.BuildRowMetrics</c> deliberately keeps those in <c>ViewportModel.Cells</c>,
    /// the WPF <c>PrintRenderer</c> seed) and -- in the portable page-model path -- every cell of the
    /// chart's DataRange, hidden or not (<c>PageContentRenderModelBuilder.BuildChartCellLookup</c>).
    /// <paramref name="chartDataCells"/> already honors <see cref="ChartModel.ShowDataInHiddenRowsAndColumns"/>,
    /// but it does so by OMITTING hidden cells (<c>ViewportService.BuildChartDataCells</c>), and an
    /// omission silently falls through to the un-filtered page value rather than suppressing it -- so
    /// without the filter below, printed/exported data labels, tick labels and legend entries can show
    /// hidden-row/column data that the on-screen chart suppresses. Filtering the page seed with the same
    /// predicate <c>BuildChartDataCells</c> uses closes that for every seed source at once.
    /// </summary>
    private static Dictionary<(uint Row, uint Col), DisplayCell> BuildCellLookup(
        ChartModel chart,
        IReadOnlyList<ChartDataCell>? chartDataCells,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> pageCellLookup,
        Sheet? dataSheet)
    {
        var sheetId = chart.DataRange.Start.Sheet;
        Dictionary<(uint Row, uint Col), DisplayCell> lookup;
        if (!chart.ShowDataInHiddenRowsAndColumns && dataSheet is not null && dataSheet.Id == sheetId)
        {
            lookup = new Dictionary<(uint Row, uint Col), DisplayCell>(pageCellLookup.Count);
            foreach (var entry in pageCellLookup)
            {
                if (dataSheet.IsRowEffectivelyHidden(entry.Key.Row) || dataSheet.IsColEffectivelyHidden(entry.Key.Col))
                    continue;

                lookup[entry.Key] = entry.Value;
            }
        }
        else
        {
            lookup = new Dictionary<(uint Row, uint Col), DisplayCell>(pageCellLookup);
        }

        if (chartDataCells is not { Count: > 0 })
            return lookup;

        foreach (var cell in chartDataCells)
        {
            if (cell.SheetId != sheetId)
                continue;

            lookup[(cell.Row, cell.Col)] = new DisplayCell(
                cell.Row,
                cell.Col,
                cell.RawValue,
                cell.DisplayText,
                null,
                StyleId.Default,
                null);
        }

        return lookup;
    }

    private static IReadOnlyList<string> BuildCategories(
        ChartModel chart,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup)
    {
        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        if (dataStartRow > chart.DataRange.End.Row)
            return [];

        var categories = new List<string>();
        for (var row = dataStartRow; row <= chart.DataRange.End.Row; row++)
        {
            var fallback = (categories.Count + 1).ToString(CultureInfo.InvariantCulture);
            if (!chart.FirstColIsCategories ||
                !cellLookup.TryGetValue((row, chart.DataRange.Start.Col), out var cell) ||
                string.IsNullOrWhiteSpace(cell.DisplayText))
            {
                categories.Add(fallback);
                continue;
            }

            categories.Add(cell.DisplayText.Trim());
        }

        return categories;
    }

    private static IReadOnlyList<PrintedChartSeries> BuildSeries(
        ChartModel chart,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyList<string> categories)
    {
        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        if (dataStartRow > chart.DataRange.End.Row)
            return [];

        var seriesList = new List<PrintedChartSeries>();
        foreach (var column in ChartTypeSupport.GetYAxisValueColumns(chart))
        {
            var seriesIndex = seriesList.Count;
            var points = new List<PrintedChartPoint>();
            var pointIndex = 0;
            for (var row = dataStartRow; row <= chart.DataRange.End.Row; row++, pointIndex++)
            {
                if (!cellLookup.TryGetValue((row, column), out var cell) ||
                    !TryGetNumericValue(cell, out var value))
                {
                    continue;
                }

                var category = pointIndex < categories.Count
                    ? categories[pointIndex]
                    : (pointIndex + 1).ToString(CultureInfo.InvariantCulture);
                points.Add(new PrintedChartPoint(category, pointIndex, value));
            }

            if (points.Count == 0)
                continue;

            seriesList.Add(new PrintedChartSeries(
                GetSeriesName(chart, cellLookup, column, seriesIndex),
                seriesIndex,
                points));
        }

        return seriesList;
    }

    private static string GetSeriesName(
        ChartModel chart,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        uint column,
        int seriesIndex)
    {
        if (chart.FirstRowIsHeader &&
            cellLookup.TryGetValue((chart.DataRange.Start.Row, column), out var header) &&
            !string.IsNullOrWhiteSpace(header.DisplayText))
        {
            return header.DisplayText.Trim();
        }

        return string.Create(CultureInfo.InvariantCulture, $"Series {seriesIndex + 1}");
    }

    private static bool TryGetNumericValue(DisplayCell cell, out double value)
    {
        switch (cell.RawValue)
        {
            case NumberValue number:
                value = number.Value;
                return double.IsFinite(value);
            case DateTimeValue dateTime:
                value = dateTime.Value;
                return double.IsFinite(value);
            case BoolValue boolean:
                value = boolean.Value ? 1 : 0;
                return true;
        }

        return double.TryParse(cell.DisplayText, NumberStyles.Any, CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value);
    }

    private static LayoutRect EstimatePlotRect(ChartModel chart, LayoutRect chartRect)
    {
        var leftReserve = Math.Max(34, chartRect.Width * 0.14);
        var rightReserve = chart.ShowLegend && chart.LegendPosition == ChartLegendPosition.Right
            ? Math.Max(76, chartRect.Width * 0.25)
            : Math.Max(14, chartRect.Width * 0.05);
        var topReserve = string.IsNullOrWhiteSpace(chart.Title)
            ? Math.Max(12, chartRect.Height * 0.06)
            : Math.Max(28, chartRect.Height * 0.14);
        var bottomReserve = Math.Max(30, chartRect.Height * 0.17);

        if (chart.LegendPosition == ChartLegendPosition.Left && chart.ShowLegend)
            leftReserve = Math.Max(leftReserve, Math.Max(76, chartRect.Width * 0.25));
        if (chart.LegendPosition == ChartLegendPosition.Top && chart.ShowLegend)
            topReserve = Math.Max(topReserve, Math.Max(32, chartRect.Height * 0.16));
        if (chart.LegendPosition == ChartLegendPosition.Bottom && chart.ShowLegend)
            bottomReserve = Math.Max(bottomReserve, Math.Max(42, chartRect.Height * 0.22));

        var width = Math.Max(1, chartRect.Width - leftReserve - rightReserve);
        var height = Math.Max(1, chartRect.Height - topReserve - bottomReserve);
        return new LayoutRect(chartRect.Left + leftReserve, chartRect.Top + topReserve, width, height);
    }

    private static PrintedChartValueRange? GetValueRange(
        ChartModel chart,
        IReadOnlyList<PrintedChartSeries> series)
    {
        var allValues = series.SelectMany(item => item.Points).Select(point => point.Value).ToList();
        if (allValues.Count == 0)
            return null;

        var isHorizontalValueAxis = IsHorizontalBar(chart.Type);
        var axisMinimum = isHorizontalValueAxis ? chart.XAxisMinimum : chart.YAxisMinimum;
        var axisMaximum = isHorizontalValueAxis ? chart.XAxisMaximum : chart.YAxisMaximum;
        var minimum = axisMinimum is { } explicitMinimum && double.IsFinite(explicitMinimum)
            ? explicitMinimum
            : Math.Min(0, allValues.Min());
        var maximum = axisMaximum is { } explicitMaximum && double.IsFinite(explicitMaximum)
            ? explicitMaximum
            : Math.Max(0, allValues.Max());

        if (!double.IsFinite(minimum) || !double.IsFinite(maximum))
            return null;
        if (maximum < minimum)
            (minimum, maximum) = (maximum, minimum);
        if (Math.Abs(maximum - minimum) < 0.000001)
        {
            maximum += 1;
            minimum -= 1;
        }

        return new PrintedChartValueRange(minimum, maximum);
    }

    private static void AddLegendEntryOverlays(
        ICollection<PrintChartTextOverlayPlan> overlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        LayoutRect chartRect,
        IReadOnlyList<PrintedChartSeries> series,
        PrintChartTextMeasure measureText)
    {
        if (!chart.ShowLegend || chart.LegendPosition == ChartLegendPosition.None)
            return;

        var legendEntries = series
            .Where(item => !IsLegendEntryDeleted(chart, item.Index))
            .Select(item => item.Name)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Take(MaxLegendEntries)
            .ToList();
        if (legendEntries.Count == 0)
            return;

        var fontSize = NormalizeFontSize(chart.LegendFontSize, chart.ChartDefaultFontSize);
        var color = ResolveLegendOverlayColor(chart, workbookTheme);
        var lineHeight = Math.Max(fontSize + 2, fontSize * 1.25);
        var inset = Math.Min(8, Math.Max(3, chartRect.Width / 40));
        if (chart.LegendPosition is ChartLegendPosition.Top or ChartLegendPosition.Bottom)
        {
            var maxItemWidth = Math.Max(28, (chartRect.Width - inset * 2) / legendEntries.Count);
            var y = chart.LegendPosition == ChartLegendPosition.Top
                ? chartRect.Top + Math.Max(fontSize + 6, chartRect.Height * 0.12)
                : chartRect.Bottom - lineHeight - inset;
            for (var i = 0; i < legendEntries.Count; i++)
            {
                var bounded = BoundOverlayText(legendEntries[i], maxItemWidth - 4, fontSize, measureText);
                if (bounded.Length == 0)
                    continue;

                overlays.Add(CreateOverlay(
                    bounded,
                    chartRect.Left + inset + i * maxItemWidth,
                    y,
                    fontSize,
                    color,
                    rotationDegrees: 0,
                    role: PrintChartTextOverlayRole.LegendEntry));
            }

            return;
        }

        var legendWidth = Math.Min(Math.Max(70, chartRect.Width * 0.24), Math.Max(70, chartRect.Width - inset * 2));
        var x = chart.LegendPosition == ChartLegendPosition.Left
            ? chartRect.Left + inset
            : chartRect.Right - legendWidth - inset;
        var yStart = chartRect.Top + Math.Max(30, chartRect.Height * 0.24);
        for (var i = 0; i < legendEntries.Count; i++)
        {
            var bounded = BoundOverlayText(legendEntries[i], legendWidth, fontSize, measureText);
            if (bounded.Length == 0)
                continue;

            overlays.Add(CreateOverlay(
                bounded,
                x,
                yStart + i * lineHeight,
                fontSize,
                color,
                rotationDegrees: 0,
                role: PrintChartTextOverlayRole.LegendEntry));
        }
    }

    private static bool IsLegendEntryDeleted(ChartModel chart, int index)
    {
        foreach (var entry in chart.LegendEntries)
        {
            if (entry.Index == index)
                return entry.IsDeleted == true;
        }

        return false;
    }

    private static void AddValueAxisTickLabelOverlays(
        ICollection<PrintChartTextOverlayPlan> overlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        LayoutRect chartRect,
        LayoutRect plotRect,
        PrintedChartValueRange valueRange,
        PrintChartTextMeasure measureText)
    {
        if (IsHorizontalBar(chart.Type))
            AddHorizontalValueAxisTickLabelOverlays(overlays, chart, workbookTheme, plotRect, valueRange, measureText);
        else
            AddVerticalValueAxisTickLabelOverlays(overlays, chart, workbookTheme, chartRect, plotRect, valueRange, measureText);
    }

    private static void AddVerticalValueAxisTickLabelOverlays(
        ICollection<PrintChartTextOverlayPlan> overlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        LayoutRect chartRect,
        LayoutRect plotRect,
        PrintedChartValueRange valueRange,
        PrintChartTextMeasure measureText)
    {
        if (chart.HideYAxis || !chart.ShowYAxisLabels || chart.YAxisLogScale)
            return;

        var fontSize = NormalizeFontSize(chart.YAxisLabelFontSize, chart.ChartDefaultFontSize);
        var color = ResolveYAxisLabelOverlayColor(chart, workbookTheme);
        var maxWidth = Math.Max(1, plotRect.Left - chartRect.Left - 8);
        foreach (var tick in BuildValueTicks(chart, valueRange, useHorizontalAxis: false))
        {
            var text = ChartDataLabelTextPlanner.FormatAxisValue(chart.YAxisNumberFormat, tick);
            var bounded = BoundOverlayText(text, maxWidth, fontSize, measureText);
            if (bounded.Length == 0)
                continue;

            var textWidth = measureText(bounded, fontSize).WidthIncludingTrailingWhitespace;
            var normalized = NormalizeValue(tick, valueRange.Minimum, valueRange.Maximum);
            if (chart.YAxisReverseOrder)
                normalized = 1 - normalized;

            overlays.Add(CreateOverlay(
                bounded,
                plotRect.Left - textWidth - 4,
                plotRect.Bottom - normalized * plotRect.Height - fontSize / 2,
                fontSize,
                color,
                chart.YAxisLabelAngle,
                PrintChartTextOverlayRole.ValueTickLabel));
        }
    }

    private static void AddHorizontalValueAxisTickLabelOverlays(
        ICollection<PrintChartTextOverlayPlan> overlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        LayoutRect plotRect,
        PrintedChartValueRange valueRange,
        PrintChartTextMeasure measureText)
    {
        if (chart.HideXAxis || !chart.ShowXAxisLabels || chart.XAxisLogScale)
            return;

        var ticks = BuildValueTicks(chart, valueRange, useHorizontalAxis: true);
        if (ticks.Count == 0)
            return;

        var fontSize = NormalizeFontSize(chart.XAxisLabelFontSize, chart.ChartDefaultFontSize);
        var color = ResolveXAxisLabelOverlayColor(chart, workbookTheme);
        var slotWidth = Math.Max(1, plotRect.Width / ticks.Count);
        foreach (var tick in ticks)
        {
            var normalized = NormalizeValue(tick, valueRange.Minimum, valueRange.Maximum);
            if (chart.XAxisReverseOrder)
                normalized = 1 - normalized;

            AddCenteredOverlay(
                overlays,
                ChartDataLabelTextPlanner.FormatAxisValue(chart.XAxisNumberFormat, tick),
                plotRect.Left + normalized * plotRect.Width,
                plotRect.Bottom + 3,
                slotWidth,
                fontSize,
                color,
                chart.XAxisLabelAngle,
                PrintChartTextOverlayRole.ValueTickLabel,
                measureText);
        }
    }

    private static IReadOnlyList<double> BuildValueTicks(
        ChartModel chart,
        PrintedChartValueRange valueRange,
        bool useHorizontalAxis)
    {
        var majorUnit = useHorizontalAxis ? chart.XAxisMajorUnit : chart.YAxisMajorUnit;
        if (majorUnit is { } explicitMajorUnit &&
            double.IsFinite(explicitMajorUnit) &&
            explicitMajorUnit > 0.000001)
        {
            var ticks = new List<double>();
            for (var value = valueRange.Minimum;
                 value <= valueRange.Maximum + 0.000001 && ticks.Count < MaxValueAxisTickLabels;
                 value += explicitMajorUnit)
            {
                ticks.Add(NormalizeAxisZero(value));
            }

            if (ticks.Count == 0 ||
                (Math.Abs(ticks[^1] - valueRange.Maximum) > 0.000001 &&
                 ticks.Count < MaxValueAxisTickLabels))
            {
                ticks.Add(NormalizeAxisZero(valueRange.Maximum));
            }

            return ticks;
        }

        var intervalCount = Math.Min(MaxValueAxisTickLabels - 1, 4);
        var step = (valueRange.Maximum - valueRange.Minimum) / intervalCount;
        var automaticTicks = new List<double>(intervalCount + 1);
        for (var i = 0; i <= intervalCount; i++)
            automaticTicks.Add(NormalizeAxisZero(valueRange.Minimum + step * i));
        return automaticTicks;
    }

    private static double NormalizeAxisZero(double value) =>
        Math.Abs(value) < 0.000001 ? 0 : value;

    private static void AddCategoryTickLabelOverlays(
        ICollection<PrintChartTextOverlayPlan> overlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        LayoutRect chartRect,
        LayoutRect plotRect,
        IReadOnlyList<string> categories,
        PrintChartTextMeasure measureText)
    {
        if (categories.Count == 0)
            return;

        if (IsHorizontalBar(chart.Type))
            AddBarCategoryTickLabelOverlays(overlays, chart, workbookTheme, chartRect, plotRect, categories, measureText);
        else
            AddBottomCategoryTickLabelOverlays(overlays, chart, workbookTheme, plotRect, categories, measureText);
    }

    private static void AddBottomCategoryTickLabelOverlays(
        ICollection<PrintChartTextOverlayPlan> overlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        LayoutRect plotRect,
        IReadOnlyList<string> categories,
        PrintChartTextMeasure measureText)
    {
        if (!chart.ShowXAxisLabels)
            return;

        var fontSize = NormalizeFontSize(chart.XAxisLabelFontSize, chart.ChartDefaultFontSize);
        var color = ResolveXAxisLabelOverlayColor(chart, workbookTheme);
        var maxLabels = Math.Max(1, (int)Math.Floor(plotRect.Width / 42));
        var skip = Math.Max(1, (int)Math.Ceiling(categories.Count / (double)maxLabels));
        var slotWidth = Math.Max(1, plotRect.Width / categories.Count * skip);
        for (var i = 0; i < categories.Count; i += skip)
        {
            AddCenteredOverlay(
                overlays,
                categories[i],
                plotRect.Left + (i + 0.5) * plotRect.Width / categories.Count,
                plotRect.Bottom + 3,
                slotWidth,
                fontSize,
                color,
                chart.XAxisLabelAngle,
                PrintChartTextOverlayRole.CategoryTickLabel,
                measureText);
        }
    }

    private static void AddBarCategoryTickLabelOverlays(
        ICollection<PrintChartTextOverlayPlan> overlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        LayoutRect chartRect,
        LayoutRect plotRect,
        IReadOnlyList<string> categories,
        PrintChartTextMeasure measureText)
    {
        if (!chart.ShowYAxisLabels)
            return;

        var fontSize = NormalizeFontSize(chart.YAxisLabelFontSize, chart.ChartDefaultFontSize);
        var color = ResolveYAxisLabelOverlayColor(chart, workbookTheme);
        var maxLabels = Math.Max(1, (int)Math.Floor(plotRect.Height / Math.Max(12, fontSize * 1.5)));
        var skip = Math.Max(1, (int)Math.Ceiling(categories.Count / (double)maxLabels));
        var maxWidth = Math.Max(1, plotRect.Left - chartRect.Left - 8);
        for (var i = 0; i < categories.Count; i += skip)
        {
            var bounded = BoundOverlayText(categories[i], maxWidth, fontSize, measureText);
            if (bounded.Length == 0)
                continue;

            var textWidth = measureText(bounded, fontSize).WidthIncludingTrailingWhitespace;
            overlays.Add(CreateOverlay(
                bounded,
                plotRect.Left - textWidth - 4,
                plotRect.Top + (i + 0.5) * plotRect.Height / categories.Count - fontSize / 2,
                fontSize,
                color,
                chart.YAxisLabelAngle,
                PrintChartTextOverlayRole.CategoryTickLabel));
        }
    }

    private static void AddDataLabelOverlays(
        ICollection<PrintChartTextOverlayPlan> overlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        LayoutRect plotRect,
        PrintedChartValueRange valueRange,
        IReadOnlyList<PrintedChartSeries> series,
        PrintChartTextMeasure measureText)
    {
        if (!chart.ShowDataLabels || chart.ShowDataLabelPercentage)
            return;

        var fontSize = NormalizeFontSize(chart.DataLabelFontSize, chart.ChartDefaultFontSize);
        var color = ResolveDataLabelOverlayColor(chart, workbookTheme);
        var pointCount = Math.Max(1, series.Max(item => item.Points.Count == 0 ? 0 : item.Points.Max(point => point.Index) + 1));
        var seriesCount = Math.Max(1, series.Count);
        var labelCount = 0;
        foreach (var item in series)
        {
            var offset = (item.Index - (seriesCount - 1) / 2.0) * Math.Min(10, plotRect.Width / Math.Max(1, pointCount * seriesCount + 1));
            foreach (var point in item.Points)
            {
                if (labelCount++ >= MaxDataLabelOverlays)
                    return;

                var text = ChartDataLabelTextPlanner.FormatDataLabel(chart, item.Name, point.Category, point.Value);
                var bounded = BoundOverlayText(text, 86, fontSize, measureText);
                if (bounded.Length == 0)
                    continue;

                var (x, y) = IsHorizontalBar(chart.Type)
                    ? GetHorizontalDataLabelPoint(plotRect, point, pointCount, valueRange.Minimum, valueRange.Maximum, fontSize)
                    : GetVerticalDataLabelPoint(plotRect, point, pointCount, valueRange.Minimum, valueRange.Maximum, fontSize, offset);
                if (!double.IsFinite(x) || !double.IsFinite(y))
                    continue;

                overlays.Add(CreateOverlay(
                    bounded,
                    Math.Clamp(x, plotRect.Left, Math.Max(plotRect.Left, plotRect.Right - 4)),
                    Math.Clamp(y, plotRect.Top, Math.Max(plotRect.Top, plotRect.Bottom - fontSize)),
                    fontSize,
                    color,
                    chart.DataLabelAngle,
                    PrintChartTextOverlayRole.DataLabel));
            }
        }
    }

    private static void AddPieDataLabelOverlays(
        ICollection<PrintChartTextOverlayPlan> overlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        LayoutRect plotRect,
        PrintedChartSeries pieSeries,
        PrintChartTextMeasure measureText)
    {
        if (!chart.ShowDataLabels)
            return;

        var total = 0d;
        foreach (var point in pieSeries.Points)
            total += Math.Max(0, point.Value);
        if (total <= 0)
            return;

        var fontSize = NormalizeFontSize(chart.DataLabelFontSize, chart.ChartDefaultFontSize);
        var color = ResolveDataLabelOverlayColor(chart, workbookTheme);
        var maxWidth = Math.Max(42, Math.Min(110, plotRect.Width * 0.42));
        var accumulatedAngle = chart.FirstSliceAngle;
        var labelCount = 0;
        foreach (var point in pieSeries.Points)
        {
            if (labelCount++ >= MaxDataLabelOverlays)
                return;

            var positiveValue = Math.Max(0, point.Value);
            var sweep = positiveValue / total * 360.0;
            var midAngle = accumulatedAngle + sweep / 2.0;
            accumulatedAngle += sweep;

            var value = ChartDataLabelTextPlanner.ShouldRenderPercentageLabels(chart)
                ? point.Value / total
                : point.Value;
            var text = ChartDataLabelTextPlanner.FormatDataLabel(chart, pieSeries.Name, point.Category, value);
            var bounded = BoundOverlayText(text, maxWidth, fontSize, measureText);
            if (bounded.Length == 0)
                continue;

            var position = GetPieDataLabelPoint(chart, plotRect, midAngle, fontSize);
            AddCenteredOverlay(
                overlays,
                bounded,
                Math.Clamp(position.X, plotRect.Left, plotRect.Right),
                Math.Clamp(position.Y, plotRect.Top, Math.Max(plotRect.Top, plotRect.Bottom - fontSize)),
                maxWidth,
                fontSize,
                color,
                chart.DataLabelAngle,
                PrintChartTextOverlayRole.DataLabel,
                measureText);
        }
    }

    private static LayoutPoint GetPieDataLabelPoint(
        ChartModel chart,
        LayoutRect plotRect,
        double angleDegrees,
        double fontSize)
    {
        var radiusFactor = chart.DataLabelPosition switch
        {
            ChartDataLabelPosition.Center => chart.Type == ChartType.Doughnut ? 0.48 : 0.36,
            ChartDataLabelPosition.OutsideEnd => 0.98,
            ChartDataLabelPosition.InsideEnd => 0.68,
            _ => 0.68
        };
        var radius = Math.Min(plotRect.Width, plotRect.Height) * 0.5 * radiusFactor;
        var radians = Math.PI * angleDegrees / 180.0;
        return new LayoutPoint(
            plotRect.Left + plotRect.Width / 2.0 + Math.Cos(radians) * radius,
            plotRect.Top + plotRect.Height / 2.0 - Math.Sin(radians) * radius - fontSize / 2.0);
    }

    private static (double X, double Y) GetVerticalDataLabelPoint(
        LayoutRect plotRect,
        PrintedChartPoint point,
        int pointCount,
        double min,
        double max,
        double fontSize,
        double seriesOffset)
    {
        var x = plotRect.Left + (point.Index + 0.5) * plotRect.Width / pointCount + seriesOffset;
        var y = plotRect.Bottom - NormalizeValue(point.Value, min, max) * plotRect.Height - fontSize - 2;
        return (x, y);
    }

    private static (double X, double Y) GetHorizontalDataLabelPoint(
        LayoutRect plotRect,
        PrintedChartPoint point,
        int pointCount,
        double min,
        double max,
        double fontSize)
    {
        var x = plotRect.Left + NormalizeValue(point.Value, min, max) * plotRect.Width + 3;
        var y = plotRect.Top + (point.Index + 0.5) * plotRect.Height / pointCount - fontSize / 2;
        return (x, y);
    }

    private static void AddCenteredOverlay(
        ICollection<PrintChartTextOverlayPlan> overlays,
        string? text,
        double centerX,
        double y,
        double maxWidth,
        double fontSize,
        PresentationRgb color,
        double rotationDegrees,
        PrintChartTextOverlayRole role,
        PrintChartTextMeasure measureText)
    {
        var bounded = BoundOverlayText(text, maxWidth, fontSize, measureText);
        if (bounded.Length == 0)
            return;

        var textWidth = measureText(bounded, fontSize).WidthIncludingTrailingWhitespace;
        var x = centerX - textWidth / 2;
        overlays.Add(CreateOverlay(bounded, x, y, fontSize, color, rotationDegrees, role));
    }

    private static void AddVerticalAxisOverlay(
        ICollection<PrintChartTextOverlayPlan> overlays,
        string? text,
        double x,
        double centerY,
        double maxWidth,
        double fontSize,
        PresentationRgb color,
        PrintChartTextOverlayRole role,
        PrintChartTextMeasure measureText)
    {
        var bounded = BoundOverlayText(text, maxWidth, fontSize, measureText);
        if (bounded.Length == 0)
            return;

        var textWidth = measureText(bounded, fontSize).WidthIncludingTrailingWhitespace;
        overlays.Add(CreateOverlay(
            bounded,
            x,
            centerY + textWidth / 2 - fontSize,
            fontSize,
            color,
            rotationDegrees: -90,
            role: role));
    }

    private static PrintChartTextOverlayPlan CreateOverlay(
        string text,
        double x,
        double y,
        double fontSize,
        PresentationRgb color,
        double rotationDegrees,
        PrintChartTextOverlayRole role) =>
        new(text, x, y, fontSize, color, rotationDegrees, role);

    public static string BoundOverlayText(
        string? text,
        double maxWidth,
        double fontSize,
        PrintChartTextMeasure measureText)
    {
        ArgumentNullException.ThrowIfNull(measureText);

        if (string.IsNullOrWhiteSpace(text))
            return "";

        var boundedWidth = Math.Max(1, maxWidth);
        var candidate = text.Trim().TrimEnd();
        if (FitsVisibleWidth(candidate, boundedWidth, fontSize, measureText))
            return candidate;

        while (candidate.Length > 0 && !FitsOverlayWidth(candidate + Ellipsis, boundedWidth, fontSize, measureText))
            candidate = candidate[..^1].TrimEnd();

        return candidate.Length == 0 ? Ellipsis : candidate + Ellipsis;
    }

    private static bool FitsVisibleWidth(
        string text,
        double maxWidth,
        double fontSize,
        PrintChartTextMeasure measureText) =>
        measureText(text, fontSize).Width <= Math.Max(1, maxWidth);

    private static bool FitsOverlayWidth(
        string text,
        double maxWidth,
        double fontSize,
        PrintChartTextMeasure measureText) =>
        measureText(text, fontSize).WidthIncludingTrailingWhitespace <= Math.Max(1, maxWidth);

    private static double NormalizeFontSize(double fontSize, double fallback) =>
        double.IsFinite(fontSize) && fontSize > 0 ? fontSize : fallback;

    private static PresentationRgb ResolveChartTitleOverlayColor(ChartModel chart, WorkbookTheme workbookTheme) =>
        ToPresentationColor(
            chart.ResolveChartTitleTextColor(workbookTheme) ??
            ResolveDefaultOverlayColor(chart, workbookTheme));

    private static PresentationRgb ResolveAxisTitleOverlayColor(ChartModel chart, WorkbookTheme workbookTheme) =>
        ToPresentationColor(
            chart.ResolveAxisTitleTextColor(workbookTheme) ??
            ResolveDefaultOverlayColor(chart, workbookTheme));

    private static PresentationRgb ResolveLegendOverlayColor(ChartModel chart, WorkbookTheme workbookTheme) =>
        ToPresentationColor(
            chart.ResolveLegendTextColor(workbookTheme) ??
            ResolveDefaultOverlayColor(chart, workbookTheme));

    private static PresentationRgb ResolveXAxisLabelOverlayColor(ChartModel chart, WorkbookTheme workbookTheme) =>
        ToPresentationColor(
            chart.ResolveXAxisLabelTextColor(workbookTheme) ??
            ResolveDefaultOverlayColor(chart, workbookTheme));

    private static PresentationRgb ResolveYAxisLabelOverlayColor(ChartModel chart, WorkbookTheme workbookTheme) =>
        ToPresentationColor(
            chart.ResolveYAxisLabelTextColor(workbookTheme) ??
            ResolveDefaultOverlayColor(chart, workbookTheme));

    private static PresentationRgb ResolveDataLabelOverlayColor(ChartModel chart, WorkbookTheme workbookTheme) =>
        ToPresentationColor(
            chart.ResolveDataLabelTextColor(workbookTheme) ??
            ResolveDefaultOverlayColor(chart, workbookTheme));

    private static CellColor ResolveDefaultOverlayColor(ChartModel chart, WorkbookTheme workbookTheme) =>
        chart.ChartDefaultTextThemeColor?.Resolve(workbookTheme) ??
        chart.ChartDefaultTextColor ??
        CellColor.Black;

    private static PresentationRgb ToPresentationColor(CellColor? color) =>
        color is { } value ? PresentationRgb.FromCellColor(value) : Black;

    private static double NormalizeValue(double value, double min, double max) =>
        Math.Clamp((value - min) / Math.Max(0.000001, max - min), 0, 1);

    private static bool IsHorizontalBar(ChartType chartType) =>
        chartType is ChartType.Bar or ChartType.ThreeDBar or ChartType.StackedBar or ChartType.PercentStackedBar;
}
