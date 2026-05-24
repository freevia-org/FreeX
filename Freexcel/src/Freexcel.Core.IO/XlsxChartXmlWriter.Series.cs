using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxChartXmlWriter
{
    private static IEnumerable<XElement> BuildChartSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool>? includeSeries = null,
        bool forceLineShapeProperties = false)
    {
        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var seriesStartCol = chart.FirstColIsCategories ? chart.DataRange.Start.Col + 1 : chart.DataRange.Start.Col;
        var categoryRange = chart.FirstColIsCategories
            ? FormatSheetRange(sheet.Name, dataStartRow, chart.DataRange.Start.Col, chart.DataRange.End.Row, chart.DataRange.Start.Col)
            : null;

        var seriesIndex = 0;
        for (var col = seriesStartCol; col <= chart.DataRange.End.Col; col++)
        {
            if (includeSeries is not null && !includeSeries(seriesIndex))
            {
                seriesIndex++;
                continue;
            }

            var valueRange = FormatSheetRange(sheet.Name, dataStartRow, col, chart.DataRange.End.Row, col);
            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                ToSeriesTitleXml(chart, sheet, col, chartNs),
                chart.Type is ChartType.Line or ChartType.ThreeDLine || forceLineShapeProperties
                    ? ToSeriesLineShapeProperties(chart, seriesIndex, chartNs, drawingNs)
                    : ToSeriesShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                chart.Type is ChartType.Line or ChartType.ThreeDLine || forceLineShapeProperties
                    ? ToSeriesMarkerXml(chart, seriesIndex, chartNs, drawingNs)
                    : null,
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToTrendlineXml(chart, seriesIndex, chartNs, drawingNs),
                ToErrorBarsXml(chart, seriesIndex, chartNs, drawingNs),
                chart.Type is ChartType.Line or ChartType.ThreeDLine || forceLineShapeProperties
                    ? ToSeriesSmoothXml(chart, seriesIndex, chartNs)
                    : null,
                ToCategoryRangeXml(categoryRange, chartNs),
                new XElement(chartNs + "val",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", valueRange))));
            seriesIndex++;
        }
    }

    private static XElement? ToCategoryRangeXml(string? categoryRange, XNamespace chartNs) =>
        string.IsNullOrWhiteSpace(categoryRange)
            ? null
            : new XElement(chartNs + "cat",
                new XElement(chartNs + "strRef",
                    new XElement(chartNs + "f", categoryRange)));

    private static XElement? ToSeriesSmoothXml(ChartModel chart, int seriesIndex, XNamespace chartNs) =>
        GetSeriesFormat(chart, seriesIndex)?.Smooth is { } smooth
            ? new XElement(chartNs + "smooth", new XAttribute("val", smooth ? "1" : "0"))
            : null;

    private static IEnumerable<XElement> BuildScatterChartSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool>? includeSeries = null)
    {
        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var xValueCol = chart.DataRange.Start.Col;
        var seriesStartCol = chart.DataRange.Start.Col + 1;
        var xValueRange = FormatSheetRange(sheet.Name, dataStartRow, xValueCol, chart.DataRange.End.Row, xValueCol);

        var seriesIndex = 0;
        for (var col = seriesStartCol; col <= chart.DataRange.End.Col; col++)
        {
            if (includeSeries is not null && !includeSeries(seriesIndex))
            {
                seriesIndex++;
                continue;
            }

            var yValueRange = FormatSheetRange(sheet.Name, dataStartRow, col, chart.DataRange.End.Row, col);
            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                ToSeriesTitleXml(chart, sheet, col, chartNs),
                ToSeriesLineShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                ToSeriesMarkerXml(chart, seriesIndex, chartNs, drawingNs),
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToTrendlineXml(chart, seriesIndex, chartNs, drawingNs),
                ToErrorBarsXml(chart, seriesIndex, chartNs, drawingNs),
                ToSeriesSmoothXml(chart, seriesIndex, chartNs),
                new XElement(chartNs + "xVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", xValueRange))),
                new XElement(chartNs + "yVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", yValueRange))));
            seriesIndex++;
        }
    }

    private static HashSet<int> GetSecondaryAxisSeriesIndexes(ChartModel chart, int seriesCount)
    {
        if (!chart.ShowSecondaryAxis || !ChartTypeSupport.SupportsSecondaryAxis(chart.Type) || seriesCount < 2)
            return [];

        if (chart.SecondaryAxisSeriesIndexes.Count == 0)
            return Enumerable.Range(1, seriesCount - 1).ToHashSet();

        return chart.SecondaryAxisSeriesIndexes
            .Where(index => index > 0 && index < seriesCount)
            .Distinct()
            .ToHashSet();
    }

    private static HashSet<int> GetComboLineSeriesIndexes(ChartModel chart, int seriesCount)
    {
        if (!chart.UseComboLineForSecondarySeries || !ChartTypeSupport.SupportsComboLineOverlay(chart) || seriesCount < 2)
            return [];

        return chart.ComboLineSeriesIndexes
            .Where(index => index > 0 && index < seriesCount)
            .Distinct()
            .ToHashSet();
    }

    private static IEnumerable<XElement> BuildBubbleChartSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (chart.DataRange.End.Col - chart.DataRange.Start.Col < 2)
            yield break;

        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var xValueCol = chart.DataRange.Start.Col;
        var xValueRange = FormatSheetRange(sheet.Name, dataStartRow, xValueCol, chart.DataRange.End.Row, xValueCol);

        var seriesIndex = 0;
        for (var yValueCol = chart.DataRange.Start.Col + 1; yValueCol < chart.DataRange.End.Col; yValueCol += 2)
        {
            var sizeCol = yValueCol + 1;
            var yValueRange = FormatSheetRange(sheet.Name, dataStartRow, yValueCol, chart.DataRange.End.Row, yValueCol);
            var sizeRange = FormatSheetRange(sheet.Name, dataStartRow, sizeCol, chart.DataRange.End.Row, sizeCol);

            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                ToSeriesTitleXml(chart, sheet, yValueCol, chartNs),
                ToSeriesShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToTrendlineXml(chart, seriesIndex, chartNs, drawingNs),
                ToErrorBarsXml(chart, seriesIndex, chartNs, drawingNs),
                new XElement(chartNs + "xVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", xValueRange))),
                new XElement(chartNs + "yVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", yValueRange))),
                new XElement(chartNs + "bubbleSize",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", sizeRange))));
            seriesIndex++;
        }
    }

    private static IEnumerable<XElement> BuildPieFamilyChartSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (chart.FirstColIsCategories && chart.DataRange.End.Col <= chart.DataRange.Start.Col)
            yield break;

        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var firstValueCol = chart.FirstColIsCategories ? chart.DataRange.Start.Col + 1 : chart.DataRange.Start.Col;
        var categoryRange = chart.FirstColIsCategories
            ? FormatSheetRange(sheet.Name, dataStartRow, chart.DataRange.Start.Col, chart.DataRange.End.Row, chart.DataRange.Start.Col)
            : null;

        var seriesIndex = 0;
        for (var valueCol = firstValueCol; valueCol <= chart.DataRange.End.Col; valueCol++)
        {
            var valueRange = FormatSheetRange(sheet.Name, dataStartRow, valueCol, chart.DataRange.End.Row, valueCol);
            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                ToSeriesTitleXml(chart, sheet, valueCol, chartNs),
                ToSeriesShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                seriesIndex == 0 ? ToExplodedSliceXml(chart, chartNs) : null,
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToCategoryRangeXml(categoryRange, chartNs),
                new XElement(chartNs + "val",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", valueRange))));
            seriesIndex++;
        }
    }

    private static XElement? ToSeriesTitleXml(
        ChartModel chart,
        Sheet sheet,
        uint seriesColumn,
        XNamespace chartNs)
    {
        if (!chart.FirstRowIsHeader)
            return null;

        var titleRange = FormatSheetRange(sheet.Name, chart.DataRange.Start.Row, seriesColumn, chart.DataRange.Start.Row, seriesColumn);
        return new XElement(chartNs + "tx",
            new XElement(chartNs + "strRef",
                new XElement(chartNs + "f", titleRange)));
    }

    private static XElement? ToFirstSliceAngleXml(ChartModel chart, XNamespace chartNs)
    {
        var normalized = chart.FirstSliceAngle % 360;
        if (normalized < 0)
            normalized += 360;

        return normalized == 0
            ? null
            : new XElement(chartNs + "firstSliceAng",
                new XAttribute("val", Math.Clamp((int)Math.Round(normalized), 0, 360)));
    }

    private static XElement? ToExplodedSliceXml(ChartModel chart, XNamespace chartNs)
    {
        var pointCount = ChartTypeSupport.GetDataPointCount(chart);
        if (chart.ExplodedSliceIndex < 0 || chart.ExplodedSliceIndex >= pointCount || chart.ExplodedSliceDistance <= 0)
            return null;

        return new XElement(chartNs + "dPt",
            new XElement(chartNs + "idx", new XAttribute("val", chart.ExplodedSliceIndex)),
            new XElement(chartNs + "explosion",
                new XAttribute("val", Math.Clamp((int)Math.Round(chart.ExplodedSliceDistance * 100), 0, 50))));
    }

    private static XElement? ToSeriesLineShapeProperties(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var format = GetSeriesFormat(chart, seriesIndex);
        if (format is null)
            return null;

        var fill = ToSolidFill(format.StrokeThemeColor, format.StrokeColor, drawingNs);
        var hasLineFormatting = fill is not null ||
            format.StrokeThickness is not null ||
            format.DashStyle is not null;

        return !hasLineFormatting
            ? null
            : new XElement(chartNs + "spPr",
                new XElement(drawingNs + "ln",
                    format.StrokeThickness is { } strokeThickness
                        ? new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(strokeThickness, 0.5, 10) * 12700)))
                        : null,
                    fill,
                    format.DashStyle is { } dashStyle
                        ? ToPresetDash(dashStyle, drawingNs)
                        : null));
    }

    private static XElement? ToSeriesMarkerXml(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (!ChartTypeSupport.SupportsSeriesMarkers(chart.Type))
            return null;

        var format = GetSeriesFormat(chart, seriesIndex);
        if (format is null)
            return null;

        var fill = ToSolidFill(format.FillThemeColor, format.FillColor, drawingNs);
        if (format.MarkerStyle is null && format.MarkerSize is null && fill is null)
            return null;

        return new XElement(chartNs + "marker",
            format.MarkerStyle is { } markerStyle
                ? new XElement(chartNs + "symbol", new XAttribute("val", ToXlsxMarkerStyle(markerStyle)))
                : null,
            format.MarkerSize is { } markerSize
                ? new XElement(chartNs + "size", new XAttribute("val", Math.Clamp((int)Math.Round(markerSize), 1, 30)))
                : null,
            fill is not null
                ? new XElement(chartNs + "spPr", fill)
                : null);
    }

    private static XElement? ToSeriesShapeProperties(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var format = GetSeriesFormat(chart, seriesIndex);
        if (format is null)
            return null;

        var fill = ToSolidFill(format.FillThemeColor, format.FillColor, drawingNs);
        var lineFill = ToSolidFill(format.StrokeThemeColor, format.StrokeColor, drawingNs);
        var hasLineFormatting = lineFill is not null ||
            format.StrokeThickness is not null ||
            format.DashStyle is not null;

        return fill is null && !hasLineFormatting
            ? null
            : new XElement(chartNs + "spPr",
                fill,
                hasLineFormatting
                    ? new XElement(drawingNs + "ln",
                        format.StrokeThickness is { } strokeThickness
                            ? new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(strokeThickness, 0.5, 10) * 12700)))
                            : null,
                        lineFill,
                        format.DashStyle is { } dashStyle
                            ? ToPresetDash(dashStyle, drawingNs)
                            : null)
                    : null);
    }

    private static ChartSeriesFormat? GetSeriesFormat(ChartModel chart, int seriesIndex)
    {
        var format = chart.SeriesFormats.LastOrDefault(item => item.SeriesIndex == seriesIndex);
        return format is null
            ? null
            : format with
            {
                DashStyle = ValidNullableEnumOrNull(format.DashStyle),
                MarkerStyle = ValidNullableEnumOrNull(format.MarkerStyle)
            };
    }

    private static TEnum? ValidNullableEnumOrNull<TEnum>(TEnum? value)
        where TEnum : struct, Enum =>
        value is { } enumValue && Enum.IsDefined(enumValue) ? enumValue : null;

}
