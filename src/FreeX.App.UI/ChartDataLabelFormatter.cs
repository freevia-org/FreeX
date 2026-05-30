using FreeX.Core.Model;
using System.Globalization;

namespace FreeX.App.UI;

public static class ChartDataLabelFormatter
{
    public static bool ShouldRenderPercentageLabels(ChartModel chart) =>
        chart.ShowDataLabelPercentage
            && ChartTypeSupport.SupportsPercentageDataLabels(chart.Type);

    public static bool IsPercentStackedChart(ChartModel chart) =>
        chart.Type is ChartType.PercentStackedColumn or ChartType.PercentStackedBar;

    public static string GetCategory(IReadOnlyList<string> categories, int index) =>
        index >= 0 && index < categories.Count ? categories[index] : "";

    public static string FormatDataLabel(ChartModel chart, string seriesName, string categoryName, double value)
    {
        var hasSeriesName = chart.ShowDataLabelSeriesName && !string.IsNullOrWhiteSpace(seriesName);
        var hasCategoryName = chart.ShowDataLabelCategoryName && !string.IsNullOrWhiteSpace(categoryName);
        var hasValue = chart.ShowDataLabelValue || (!hasSeriesName && !hasCategoryName);
        var valueText = hasValue ? FormatLabelValue(chart, value) : "";
        var separator = GetDataLabelSeparatorText(chart.DataLabelSeparator);

        return (hasSeriesName, hasCategoryName, hasValue) switch
        {
            (true, true, true) => $"{seriesName}{separator}{categoryName}{separator}{valueText}",
            (true, true, false) => $"{seriesName}{separator}{categoryName}",
            (true, false, true) => $"{seriesName}{separator}{valueText}",
            (true, false, false) => seriesName,
            (false, true, true) => $"{categoryName}{separator}{valueText}",
            (false, true, false) => categoryName,
            _ => valueText
        };
    }

    public static string GetPieLabelFormat(ChartModel chart, string seriesName)
    {
        var separator = GetDataLabelSeparatorText(chart.DataLabelSeparator);
        var nameParts = (chart.ShowDataLabelSeriesName, chart.ShowDataLabelCategoryName) switch
        {
            (true, true) => $"{seriesName}{separator}{{1}}",
            (true, false) => seriesName,
            (false, true) => "{1}",
            _ => ""
        };
        var valuePart = chart.ShowDataLabelPercentage
            ? "{2:0%}"
            : chart.ShowDataLabelValue
                ? GetPieValueFormat(chart.DataLabelNumberFormat)
                : "";

        if (valuePart.Length == 0 && nameParts.Length == 0)
            return GetPieValueFormat(chart.DataLabelNumberFormat);
        if (valuePart.Length == 0)
            return nameParts;
        if (nameParts.Length == 0)
            return valuePart;
        return $"{nameParts}{separator}{valuePart}";
    }

    public static string? GetNativeValueLabelFormat(ChartModel chart, int valueIndex)
    {
        if (!ShouldUseNativeValueLabels(chart))
            return null;

        var format = chart.DataLabelNumberFormat switch
        {
            ChartDataLabelNumberFormat.Number => ":0.00",
            ChartDataLabelNumberFormat.Currency => ":$#,##0.00",
            ChartDataLabelNumberFormat.Percent => ":0%",
            _ => ""
        };
        return $"{{{valueIndex}{format}}}";
    }

    public static bool ShouldUseNativeValueLabels(ChartModel chart) =>
        chart.ShowDataLabels
            && chart.ShowDataLabelValue
            && !chart.ShowDataLabelCategoryName
            && !chart.ShowDataLabelSeriesName
            && !ShouldRenderPercentageLabels(chart)
            && !IsPercentStackedChart(chart)
            && !RequiresDataLabelAnnotationFormatting(chart);

    public static bool ShouldUseAnnotationLabels(ChartModel chart) =>
        chart.ShowDataLabels
            && (chart.ShowDataLabelCategoryName
                || chart.ShowDataLabelSeriesName
                || !chart.ShowDataLabelValue
                || ShouldRenderPercentageLabels(chart)
                || IsPercentStackedChart(chart)
                || RequiresDataLabelAnnotationFormatting(chart));

    public static string FormatLabelValue(ChartModel chart, double value) =>
        ShouldRenderPercentageLabels(chart)
            ? value.ToString("0%", CultureInfo.InvariantCulture)
            : chart.DataLabelNumberFormat switch
            {
                ChartDataLabelNumberFormat.Number => value.ToString("0.00"),
                ChartDataLabelNumberFormat.Currency => value.ToString("$#,##0.00", CultureInfo.InvariantCulture),
                ChartDataLabelNumberFormat.Percent => value.ToString("0%", CultureInfo.InvariantCulture),
                _ => value.ToString("0.###", CultureInfo.InvariantCulture)
            };

    public static string GetDataLabelSeparatorText(ChartDataLabelSeparator separator) =>
        separator switch
        {
            ChartDataLabelSeparator.Semicolon => "; ",
            ChartDataLabelSeparator.NewLine => Environment.NewLine,
            ChartDataLabelSeparator.Space => " ",
            _ => ", "
        };

    private static string GetPieValueFormat(ChartDataLabelNumberFormat format) =>
        format switch
        {
            ChartDataLabelNumberFormat.Number => "{0:0.00}",
            ChartDataLabelNumberFormat.Currency => "{0:$#,##0.00}",
            ChartDataLabelNumberFormat.Percent => "{0:0%}",
            _ => "{0}"
        };

    private static bool RequiresDataLabelAnnotationFormatting(ChartModel chart) =>
        chart.ShowDataLabelCallouts
            || chart.DataLabelFillColor is not null
            || chart.DataLabelFillThemeColor is not null
            || chart.DataLabelBorderColor is not null
            || chart.DataLabelBorderThemeColor is not null
            || chart.DataLabelBorderThickness > 0
            || Math.Abs(chart.DataLabelAngle) > 0.5;
}
