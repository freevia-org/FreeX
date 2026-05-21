using Freexcel.Core.Model;
using System.Globalization;

namespace Freexcel.App.UI;

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
        var parts = new List<string>();
        if (chart.ShowDataLabelSeriesName && !string.IsNullOrWhiteSpace(seriesName))
            parts.Add(seriesName);
        if (chart.ShowDataLabelCategoryName && !string.IsNullOrWhiteSpace(categoryName))
            parts.Add(categoryName);
        parts.Add(FormatLabelValue(chart, value));
        return string.Join(GetDataLabelSeparatorText(chart.DataLabelSeparator), parts);
    }

    public static string GetPieLabelFormat(ChartModel chart, string seriesName)
    {
        var valuePart = chart.ShowDataLabelPercentage
            ? "{2:0%}"
            : GetPieValueFormat(chart.DataLabelNumberFormat);
        var separator = GetDataLabelSeparatorText(chart.DataLabelSeparator);
        if (chart.ShowDataLabelSeriesName && chart.ShowDataLabelCategoryName)
            return $"{seriesName}{separator}{{1}}{separator}{valuePart}";
        if (chart.ShowDataLabelSeriesName)
            return $"{seriesName}{separator}{valuePart}";
        if (chart.ShowDataLabelCategoryName)
            return $"{{1}}{separator}{valuePart}";
        return valuePart;
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
            && !chart.ShowDataLabelCategoryName
            && !chart.ShowDataLabelSeriesName
            && !ShouldRenderPercentageLabels(chart)
            && !IsPercentStackedChart(chart)
            && !RequiresDataLabelAnnotationFormatting(chart);

    public static bool ShouldUseAnnotationLabels(ChartModel chart) =>
        chart.ShowDataLabels
            && (chart.ShowDataLabelCategoryName
                || chart.ShowDataLabelSeriesName
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
