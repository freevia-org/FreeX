using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
    private const int MaxPrintedChartLegendEntries = 12;
    private const int MaxPrintedChartDataLabelOverlays = 80;

    private readonly record struct PrintedChartSeries(string Name, int Index, IReadOnlyList<PrintedChartPoint> Points);

    private readonly record struct PrintedChartPoint(string Category, int Index, double Value);

    private static void AddPrintedChartNonTitleTextOverlays(
        ICollection<PdfTextOverlay> textOverlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        Rect chartRect,
        ViewportModel viewport,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> pageCellLookup)
    {
        if (!SupportsPrintedChartNonTitleTextOverlays(chart.Type))
            return;

        var cellLookup = BuildPrintedChartCellLookup(chart, viewport, pageCellLookup);
        var categories = BuildPrintedChartCategories(chart, cellLookup);
        var series = BuildPrintedChartSeries(chart, cellLookup, categories);
        if (series.Count == 0)
            return;

        var plotRect = EstimatePrintedChartPlotRect(chart, chartRect);
        AddPrintedChartLegendEntryOverlays(textOverlays, chart, workbookTheme, chartRect, series);
        AddPrintedChartCategoryTickLabelOverlays(textOverlays, chart, workbookTheme, chartRect, plotRect, categories);
        AddPrintedChartDataLabelOverlays(textOverlays, chart, workbookTheme, plotRect, series);
    }

    private static bool SupportsPrintedChartNonTitleTextOverlays(ChartType chartType) =>
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

    private static Dictionary<(uint Row, uint Col), DisplayCell> BuildPrintedChartCellLookup(
        ChartModel chart,
        ViewportModel viewport,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> pageCellLookup)
    {
        var lookup = new Dictionary<(uint Row, uint Col), DisplayCell>(pageCellLookup);
        if (viewport.ChartDataCells is not { Count: > 0 })
            return lookup;

        var sheetId = chart.DataRange.Start.Sheet;
        foreach (var cell in viewport.ChartDataCells)
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

    private static IReadOnlyList<string> BuildPrintedChartCategories(
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

    private static IReadOnlyList<PrintedChartSeries> BuildPrintedChartSeries(
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
                    !TryGetPrintedChartNumericValue(cell, out var value))
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
                GetPrintedChartSeriesName(chart, cellLookup, column, seriesIndex),
                seriesIndex,
                points));
        }

        return seriesList;
    }

    private static string GetPrintedChartSeriesName(
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

    private static bool TryGetPrintedChartNumericValue(DisplayCell cell, out double value)
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

    private static Rect EstimatePrintedChartPlotRect(ChartModel chart, Rect chartRect)
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
        return new Rect(chartRect.Left + leftReserve, chartRect.Top + topReserve, width, height);
    }

    private static void AddPrintedChartLegendEntryOverlays(
        ICollection<PdfTextOverlay> textOverlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        Rect chartRect,
        IReadOnlyList<PrintedChartSeries> series)
    {
        if (!chart.ShowLegend || chart.LegendPosition == ChartLegendPosition.None)
            return;

        var legendEntries = series
            .Where(item => !IsPrintedChartLegendEntryDeleted(chart, item.Index))
            .Select(item => item.Name)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Take(MaxPrintedChartLegendEntries)
            .ToList();
        if (legendEntries.Count == 0)
            return;

        var fontSize = NormalizePrintedChartFontSize(chart.LegendFontSize, chart.ChartDefaultFontSize);
        var color = chart.ResolveLegendTextColor(workbookTheme) ?? ResolveChartDefaultOverlayColor(chart, workbookTheme);
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
                var bounded = BoundPrintedChartOverlayText(legendEntries[i], maxItemWidth - 4, fontSize);
                if (bounded.Length == 0)
                    continue;

                textOverlays.Add(CreatePrintedChartTextOverlay(
                    bounded,
                    chartRect.Left + inset + i * maxItemWidth,
                    y,
                    fontSize,
                    color,
                    rotationDegrees: 0));
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
            var bounded = BoundPrintedChartOverlayText(legendEntries[i], legendWidth, fontSize);
            if (bounded.Length == 0)
                continue;

            textOverlays.Add(CreatePrintedChartTextOverlay(
                bounded,
                x,
                yStart + i * lineHeight,
                fontSize,
                color,
                rotationDegrees: 0));
        }
    }

    private static bool IsPrintedChartLegendEntryDeleted(ChartModel chart, int index) =>
        chart.LegendEntries.FirstOrDefault(entry => entry.Index == index) is { IsDeleted: true };

    private static void AddPrintedChartCategoryTickLabelOverlays(
        ICollection<PdfTextOverlay> textOverlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        Rect chartRect,
        Rect plotRect,
        IReadOnlyList<string> categories)
    {
        if (categories.Count == 0)
            return;

        if (IsPrintedChartHorizontalBar(chart.Type))
            AddPrintedChartBarCategoryTickLabelOverlays(textOverlays, chart, workbookTheme, chartRect, plotRect, categories);
        else
            AddPrintedChartBottomCategoryTickLabelOverlays(textOverlays, chart, workbookTheme, plotRect, categories);
    }

    private static void AddPrintedChartBottomCategoryTickLabelOverlays(
        ICollection<PdfTextOverlay> textOverlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        Rect plotRect,
        IReadOnlyList<string> categories)
    {
        if (!chart.ShowXAxisLabels)
            return;

        var fontSize = NormalizePrintedChartFontSize(chart.XAxisLabelFontSize, chart.ChartDefaultFontSize);
        var color = chart.ResolveXAxisLabelTextColor(workbookTheme) ?? ResolveChartDefaultOverlayColor(chart, workbookTheme);
        var maxLabels = Math.Max(1, (int)Math.Floor(plotRect.Width / 42));
        var skip = Math.Max(1, (int)Math.Ceiling(categories.Count / (double)maxLabels));
        var slotWidth = Math.Max(1, plotRect.Width / categories.Count * skip);
        for (var i = 0; i < categories.Count; i += skip)
        {
            AddPrintedChartCenteredOverlay(
                textOverlays,
                categories[i],
                plotRect.Left + (i + 0.5) * plotRect.Width / categories.Count,
                plotRect.Bottom + 3,
                slotWidth,
                fontSize,
                color,
                chart.XAxisLabelAngle);
        }
    }

    private static void AddPrintedChartBarCategoryTickLabelOverlays(
        ICollection<PdfTextOverlay> textOverlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        Rect chartRect,
        Rect plotRect,
        IReadOnlyList<string> categories)
    {
        if (!chart.ShowYAxisLabels)
            return;

        var fontSize = NormalizePrintedChartFontSize(chart.YAxisLabelFontSize, chart.ChartDefaultFontSize);
        var color = chart.ResolveYAxisLabelTextColor(workbookTheme) ?? ResolveChartDefaultOverlayColor(chart, workbookTheme);
        var maxLabels = Math.Max(1, (int)Math.Floor(plotRect.Height / Math.Max(12, fontSize * 1.5)));
        var skip = Math.Max(1, (int)Math.Ceiling(categories.Count / (double)maxLabels));
        var maxWidth = Math.Max(1, plotRect.Left - chartRect.Left - 8);
        for (var i = 0; i < categories.Count; i += skip)
        {
            var bounded = BoundPrintedChartOverlayText(categories[i], maxWidth, fontSize);
            if (bounded.Length == 0)
                continue;

            var textWidth = MeasurePrintedChartText(bounded, fontSize).WidthIncludingTrailingWhitespace;
            textOverlays.Add(CreatePrintedChartTextOverlay(
                bounded,
                plotRect.Left - textWidth - 4,
                plotRect.Top + (i + 0.5) * plotRect.Height / categories.Count - fontSize / 2,
                fontSize,
                color,
                chart.YAxisLabelAngle));
        }
    }

    private static void AddPrintedChartDataLabelOverlays(
        ICollection<PdfTextOverlay> textOverlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        Rect plotRect,
        IReadOnlyList<PrintedChartSeries> series)
    {
        if (!chart.ShowDataLabels || chart.ShowDataLabelPercentage)
            return;

        var allValues = series.SelectMany(item => item.Points).Select(point => point.Value).ToList();
        if (allValues.Count == 0)
            return;

        var min = Math.Min(0, allValues.Min());
        var max = Math.Max(0, allValues.Max());
        if (Math.Abs(max - min) < 0.000001)
        {
            max += 1;
            min -= 1;
        }

        var fontSize = NormalizePrintedChartFontSize(chart.DataLabelFontSize, chart.ChartDefaultFontSize);
        var color = chart.ResolveDataLabelTextColor(workbookTheme) ?? ResolveChartDefaultOverlayColor(chart, workbookTheme);
        var pointCount = Math.Max(1, series.Max(item => item.Points.Count == 0 ? 0 : item.Points.Max(point => point.Index) + 1));
        var seriesCount = Math.Max(1, series.Count);
        var labelCount = 0;
        foreach (var item in series)
        {
            var offset = (item.Index - (seriesCount - 1) / 2.0) * Math.Min(10, plotRect.Width / Math.Max(1, pointCount * seriesCount + 1));
            foreach (var point in item.Points)
            {
                if (labelCount++ >= MaxPrintedChartDataLabelOverlays)
                    return;

                var text = ChartDataLabelFormatter.FormatDataLabel(chart, item.Name, point.Category, point.Value);
                var bounded = BoundPrintedChartOverlayText(text, 86, fontSize);
                if (bounded.Length == 0)
                    continue;

                var (x, y) = IsPrintedChartHorizontalBar(chart.Type)
                    ? GetPrintedChartHorizontalDataLabelPoint(plotRect, point, pointCount, min, max, fontSize)
                    : GetPrintedChartVerticalDataLabelPoint(plotRect, point, pointCount, min, max, fontSize, offset);

                if (!plotRect.Contains(new Point(Math.Clamp(x, plotRect.Left, plotRect.Right), Math.Clamp(y, plotRect.Top, plotRect.Bottom))))
                    continue;

                textOverlays.Add(CreatePrintedChartTextOverlay(
                    bounded,
                    Math.Clamp(x, plotRect.Left, Math.Max(plotRect.Left, plotRect.Right - 4)),
                    Math.Clamp(y, plotRect.Top, Math.Max(plotRect.Top, plotRect.Bottom - fontSize)),
                    fontSize,
                    color,
                    chart.DataLabelAngle));
            }
        }
    }

    private static (double X, double Y) GetPrintedChartVerticalDataLabelPoint(
        Rect plotRect,
        PrintedChartPoint point,
        int pointCount,
        double min,
        double max,
        double fontSize,
        double seriesOffset)
    {
        var x = plotRect.Left + (point.Index + 0.5) * plotRect.Width / pointCount + seriesOffset;
        var y = plotRect.Bottom - NormalizePrintedChartValue(point.Value, min, max) * plotRect.Height - fontSize - 2;
        return (x, y);
    }

    private static (double X, double Y) GetPrintedChartHorizontalDataLabelPoint(
        Rect plotRect,
        PrintedChartPoint point,
        int pointCount,
        double min,
        double max,
        double fontSize)
    {
        var x = plotRect.Left + NormalizePrintedChartValue(point.Value, min, max) * plotRect.Width + 3;
        var y = plotRect.Top + (point.Index + 0.5) * plotRect.Height / pointCount - fontSize / 2;
        return (x, y);
    }

    private static double NormalizePrintedChartValue(double value, double min, double max) =>
        Math.Clamp((value - min) / Math.Max(0.000001, max - min), 0, 1);

    private static bool IsPrintedChartHorizontalBar(ChartType chartType) =>
        chartType is ChartType.Bar or ChartType.ThreeDBar or ChartType.StackedBar or ChartType.PercentStackedBar;
}
