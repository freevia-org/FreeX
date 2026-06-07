using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class ChartSeriesIndexSanitizer
{
    public static void SanitizeSecondaryAxisAndComboLineIndexes(ChartModel chart, int seriesCount)
    {
        chart.SecondaryAxisSeriesIndexes = SanitizeSeriesIndexes(chart.SecondaryAxisSeriesIndexes, seriesCount);
        if (!ChartTypeSupport.SupportsSecondaryAxis(chart.Type)
            || (chart.ShowSecondaryAxis && chart.SecondaryAxisSeriesIndexes.Count == 0))
        {
            chart.ShowSecondaryAxis = false;
            chart.SecondaryAxisSeriesIndexes = [];
        }

        chart.ComboLineSeriesIndexes = SanitizeSeriesIndexes(chart.ComboLineSeriesIndexes, seriesCount);
        if (!ChartTypeSupport.SupportsComboLineOverlay(chart)
            || (chart.UseComboLineForSecondarySeries && chart.ComboLineSeriesIndexes.Count == 0))
        {
            chart.UseComboLineForSecondarySeries = false;
            chart.ComboLineSeriesIndexes = [];
        }
    }

    private static List<int> SanitizeSeriesIndexes(IEnumerable<int> indexes, int seriesCount) =>
        indexes
            .Where(index => index > 0 && index < seriesCount)
            .Distinct()
            .Order()
            .ToList();
}
