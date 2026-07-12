using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts;

/// <summary>
/// UI-free chart data-label text and number formatting shared by renderers and print overlays.
/// Hosts decide where/how to paint the returned text.
/// </summary>
public static class ChartDataLabelTextPlanner
{
    public static bool ShouldRenderPercentageLabels(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return chart.ShowDataLabelPercentage
            && ChartTypeSupport.SupportsPercentageDataLabels(chart.Type);
    }

    public static bool IsPercentStackedChart(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return chart.Type is ChartType.PercentStackedColumn or ChartType.PercentStackedBar;
    }

    public static string GetCategory(IReadOnlyList<string> categories, int index) =>
        index >= 0 && index < categories.Count ? categories[index] : "";

    public static string FormatDataLabel(
        ChartModel chart,
        string seriesName,
        string categoryName,
        double value)
    {
        ArgumentNullException.ThrowIfNull(chart);

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

    public static string FormatPieDataLabel(
        ChartModel chart,
        string seriesName,
        string categoryName,
        double value,
        double fraction)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var separator = GetDataLabelSeparatorText(chart.DataLabelSeparator);
        var hasSeriesName = chart.ShowDataLabelSeriesName && !string.IsNullOrWhiteSpace(seriesName);
        var hasCategoryName = chart.ShowDataLabelCategoryName && !string.IsNullOrWhiteSpace(categoryName);
        var parts = new List<string>(3);
        if (hasSeriesName)
            parts.Add(seriesName);
        if (hasCategoryName)
            parts.Add(categoryName);
        // Excel's fixed data-label order is Series Name, Category Name, Value, Percentage.
        // Value and Percentage are independently toggleable (showVal/showPercent) and can both
        // be set at once, so they must not be treated as mutually exclusive here.
        if (chart.ShowDataLabelValue || (!chart.ShowDataLabelPercentage && parts.Count == 0))
            parts.Add(FormatLabelValue(chart, value));
        if (chart.ShowDataLabelPercentage)
            parts.Add(fraction.ToString("0%", CultureInfo.InvariantCulture));

        return string.Join(separator, parts);
    }

    public static string FormatLabelValue(ChartModel chart, double value)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return ShouldRenderPercentageLabels(chart)
            ? value.ToString("0%", CultureInfo.InvariantCulture)
            : FormatAxisValue(chart.DataLabelNumberFormat, value);
    }

    public static string FormatAxisValue(ChartDataLabelNumberFormat format, double value) =>
        format switch
        {
            ChartDataLabelNumberFormat.Number => value.ToString("0.00", CultureInfo.InvariantCulture),
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
}
