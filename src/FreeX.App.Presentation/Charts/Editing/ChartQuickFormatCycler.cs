using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>
/// Portable (no UI) cycling math for the chart contextual-tab "quick" format buttons that step a single value
/// per click rather than opening a dialog: the Text group's title/axis/legend/data-label color and font-size
/// buttons, the Shape Styles group's series dash and marker size, and the Type group's combo-series toggle.
/// Mirrors the WPF host's <c>ChartOptionCycler</c> step values exactly so repeated clicks walk the same
/// sequence on both shells, but keeps the math in the shared presentation layer so the cross-platform shell
/// can reuse it. Every value here feeds an existing field on <see cref="ChartModel"/> applied through the
/// Core <c>SetChartLayoutCommand</c>; no Core change is needed.
/// </summary>
public static class ChartQuickFormatCycler
{
    /// <summary>
    /// The next color in the cohesive Okabe-Ito-style palette the WPF host uses (blue → orange → green →
    /// blue). A null current color starts at blue.
    /// </summary>
    public static CellColor NextSeriesColor(CellColor? current)
    {
        if (current is null)
            return new CellColor(0, 114, 178);
        if (current.Value.R == 0 && current.Value.G == 114 && current.Value.B == 178)
            return new CellColor(213, 94, 0);
        if (current.Value.R == 213 && current.Value.G == 94 && current.Value.B == 0)
            return new CellColor(0, 158, 115);
        return new CellColor(0, 114, 178);
    }

    /// <summary>Chart-title font-size step: +2pt up to 24, then wrap back to 12 (matches WPF).</summary>
    public static double NextChartTitleFontSize(double current) => current >= 24 ? 12 : current + 2;

    /// <summary>Axis-title font-size step: +1pt up to 18, then wrap back to 9 (matches WPF).</summary>
    public static double NextAxisTitleFontSize(double current) => current >= 18 ? 9 : current + 1;

    /// <summary>Legend font-size step: +1pt up to 16, then wrap back to 9 (matches WPF).</summary>
    public static double NextLegendFontSize(double current) => current >= 16 ? 9 : current + 1;

    /// <summary>Data-label border thickness step: +0.75pt up to 3, then wrap back to 0.75 (matches WPF).</summary>
    public static double NextDataLabelBorderThickness(double current) => current >= 3 ? 0.75 : current + 0.75;

    /// <summary>Series dash-style cycle: none → dash → dot → solid → none (matches WPF).</summary>
    public static ChartLineDashStyle? NextSeriesDash(ChartLineDashStyle? current) =>
        current switch
        {
            null => ChartLineDashStyle.Dash,
            ChartLineDashStyle.Dash => ChartLineDashStyle.Dot,
            ChartLineDashStyle.Dot => ChartLineDashStyle.Solid,
            _ => null,
        };

    /// <summary>Series marker-size step: +2pt up to 12, then wrap back to 5 (matches WPF). Null starts at 5.</summary>
    public static double NextMarkerSize(double? current) => current is null or >= 12 ? 5 : current.Value + 2;

    /// <summary>
    /// Reads the first data series' (index 0) current format, or a fresh empty format if none is stored.
    /// The quick Shape-Styles buttons always edit series 0, matching the WPF host.
    /// </summary>
    public static ChartSeriesFormat ReadFirstSeriesFormat(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        foreach (var format in chart.SeriesFormats)
        {
            if (format.SeriesIndex == 0)
                return format;
        }

        return new ChartSeriesFormat(0);
    }

    /// <summary>
    /// Merges an updated series-0 format into the chart's series-format list (replacing or appending) and
    /// returns the resulting list, so a quick dash/marker change preserves every other series.
    /// </summary>
    public static IReadOnlyList<ChartSeriesFormat> MergeFirstSeriesFormat(ChartModel chart, ChartSeriesFormat updated)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var formats = new List<ChartSeriesFormat>(chart.SeriesFormats);
        for (var index = 0; index < formats.Count; index++)
        {
            if (formats[index].SeriesIndex == 0)
            {
                formats[index] = updated;
                return formats;
            }
        }

        formats.Add(updated);
        return formats;
    }

    /// <summary>
    /// The next combo-line series set when stepping the combo-series quick button: starts at {1}, advances the
    /// single marked series by one each click, then clears (matches the WPF host's <c>GetNextComboLineSeries</c>).
    /// </summary>
    public static IReadOnlyList<int> NextComboLineSeries(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var seriesCount = ChartTypeSupport.GetDataSeriesCount(chart);
        if (!chart.UseComboLineForSecondarySeries || chart.ComboLineSeriesIndexes.Count == 0)
            return [1];

        var current = chart.ComboLineSeriesIndexes.Min();
        if (current + 1 < seriesCount)
            return [current + 1];

        return [];
    }
}
