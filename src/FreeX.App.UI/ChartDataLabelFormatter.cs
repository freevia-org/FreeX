using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static class ChartDataLabelFormatter
{
    public static bool ShouldRenderPercentageLabels(ChartModel chart) =>
        ChartDataLabelTextPlanner.ShouldRenderPercentageLabels(chart);

    public static bool IsPercentStackedChart(ChartModel chart) =>
        ChartDataLabelTextPlanner.IsPercentStackedChart(chart);

    public static string GetCategory(IReadOnlyList<string> categories, int index) =>
        ChartDataLabelTextPlanner.GetCategory(categories, index);

    public static string FormatDataLabel(ChartModel chart, string seriesName, string categoryName, double value) =>
        ChartDataLabelTextPlanner.FormatDataLabel(chart, seriesName, categoryName, value);

    public static string GetPieLabelFormat(ChartModel chart, string seriesName)
    {
        var separator = ChartDataLabelTextPlanner.GetDataLabelSeparatorText(chart.DataLabelSeparator);
        // The result is consumed by OxyPlot as a composite format string ({1}=label, {2}=percentage,
        // {0}=value), so literal braces in the user-controlled series name must be escaped or OxyPlot
        // would parse them as placeholders (malformed label or FormatException).
        var safeSeriesName = seriesName.Replace("{", "{{").Replace("}", "}}");
        var nameParts = (chart.ShowDataLabelSeriesName, chart.ShowDataLabelCategoryName) switch
        {
            (true, true) => $"{safeSeriesName}{separator}{{1}}",
            (true, false) => safeSeriesName,
            (false, true) => "{1}",
            _ => ""
        };
        // Excel treats showVal and showPercent as independent flags (e.g. the built-in "Value,
        // Percentage" preset sets both), so the value and percentage placeholders must compose
        // rather than the percentage silently displacing the value.
        var valuePart = (chart.ShowDataLabelValue, chart.ShowDataLabelPercentage) switch
        {
            (true, true) => $"{GetPieValueFormat(chart.DataLabelNumberFormat)}{separator}{{2:0%}}",
            (true, false) => GetPieValueFormat(chart.DataLabelNumberFormat),
            (false, true) => "{2:0%}",
            _ => ""
        };

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
        ChartDataLabelTextPlanner.FormatLabelValue(chart, value);

    public static string GetDataLabelSeparatorText(ChartDataLabelSeparator separator) =>
        ChartDataLabelTextPlanner.GetDataLabelSeparatorText(separator);

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
