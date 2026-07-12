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

        // Combo line/scatter membership may legitimately include series index 0 — Excel can put
        // the <c:lineChart>/<c:scatterChart> series first (e.g. a shaded target-band chart where the
        // Qty line is idx 0 over bar helper columns). Allow idx 0 here — exactly as SanitizeSeriesIndexes
        // now does for the secondary axis (Excel's Format Data Series > Secondary Axis works on ANY
        // series regardless of position, including the first).
        chart.ComboLineSeriesIndexes = SanitizeComboIndexes(chart.ComboLineSeriesIndexes, seriesCount);
        if (!ChartTypeSupport.SupportsComboLineOverlay(chart)
            || (chart.UseComboLineForSecondarySeries && chart.ComboLineSeriesIndexes.Count == 0))
        {
            chart.UseComboLineForSecondarySeries = false;
            chart.ComboLineSeriesIndexes = [];
        }

        chart.ComboScatterSeriesIndexes = SanitizeComboIndexes(chart.ComboScatterSeriesIndexes, seriesCount);
    }

    private static List<int> SanitizeSeriesIndexes(IEnumerable<int> indexes, int seriesCount) =>
        indexes
            // Keep index 0: a secondary-axis assignment is valid for ANY series, including the first
            // (mirrors SanitizeComboIndexes below). R25-chart-axis-series-deep-1 — a `> 0` filter here
            // silently re-stripped series 0 right after the readers recorded it.
            .Where(index => index >= 0 && index < seriesCount)
            .Distinct()
            .Order()
            .ToList();

    private static List<int> SanitizeComboIndexes(IEnumerable<int> indexes, int seriesCount) =>
        indexes
            .Where(index => index >= 0 && index < seriesCount)
            .Distinct()
            .Order()
            .ToList();
}
