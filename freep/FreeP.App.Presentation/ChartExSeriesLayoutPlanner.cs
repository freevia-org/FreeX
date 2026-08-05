using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartExSeriesLayoutOption(
    int SeriesIndex,
    string SeriesName,
    string LayoutId,
    string Label);

public sealed record ChartExSeriesLayoutCommitPlan(int SeriesIndex, string LayoutId);

/// <summary>Plans safe edits to layout IDs already present in a native ChartEx payload.</summary>
public static class ChartExSeriesLayoutPlanner
{
    public const string CommandId = "freep.chart.edit-chartex-series-layout";
    public const string DialogTitle = "ChartEx Series Layout";
    public const string SeriesLabel = "Series";
    public const string LayoutLabel = "Layout";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";

    public static bool CanEdit(ChartShape? chart)
    {
        return chart is { IsChartEx: true }
            && !string.IsNullOrWhiteSpace(chart.PreservedChartExXml)
            && chart.Series.Any(series => !string.IsNullOrWhiteSpace(series.ChartExLayoutId));
    }

    public static IReadOnlyList<ChartExSeriesLayoutOption> BuildOptions(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (!chart.IsChartEx || string.IsNullOrWhiteSpace(chart.PreservedChartExXml))
            return Array.Empty<ChartExSeriesLayoutOption>();

        return chart.Series
            .Select((series, index) => (series, index))
            .Where(item => !string.IsNullOrWhiteSpace(item.series.ChartExLayoutId))
            .Select(item =>
            {
                var layoutId = item.series.ChartExLayoutId!.Trim();
                var name = string.IsNullOrWhiteSpace(item.series.Name)
                    ? $"Series {item.index + 1}"
                    : item.series.Name;
                return new ChartExSeriesLayoutOption(
                    item.index,
                    name,
                    layoutId,
                    $"{name}: {FormatLayoutLabel(layoutId)}");
            })
            .ToArray();
    }

    public static IReadOnlyList<string> BuildLayoutChoices(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return chart.Series
            .Select(series => series.ChartExLayoutId?.Trim())
            .Where(layoutId => !string.IsNullOrWhiteSpace(layoutId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(layoutId => layoutId!)
            .ToArray();
    }

    public static ChartExSeriesLayoutCommitPlan BuildCommitPlan(
        ChartShape chart,
        int seriesIndex,
        string layoutId)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (!CanEdit(chart))
            throw new InvalidOperationException("The selected chart does not expose an editable native ChartEx payload.");
        if (seriesIndex < 0 || seriesIndex >= chart.Series.Count)
            throw new ArgumentOutOfRangeException(nameof(seriesIndex));

        var normalized = layoutId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized)
            || !BuildLayoutChoices(chart).Contains(normalized, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a layout already present in this ChartEx payload.", nameof(layoutId));

        return new ChartExSeriesLayoutCommitPlan(seriesIndex, normalized);
    }

    public static string FormatLayoutLabel(string layoutId)
    {
        var words = layoutId.Replace('_', ' ').Replace('-', ' ');
        return string.Concat(words.Select((character, index) =>
            index == 0 ? char.ToUpperInvariant(character).ToString() : character.ToString()));
    }
}
