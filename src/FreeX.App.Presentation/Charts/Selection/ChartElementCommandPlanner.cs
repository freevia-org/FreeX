using Free.Shared.Localization;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Selection;

/// <summary>Which existing chart formatting dialog a selected element opens ("Format Selection").</summary>
public enum ChartElementFormatTarget
{
    /// <summary>Format Chart Area dialog (chart area, plot area and legend share it).</summary>
    ChartArea,
    /// <summary>Chart Titles dialog (chart title and axis titles).</summary>
    ChartTitles,
    /// <summary>Format Axis dialog for the horizontal (X) axis, including its gridlines.</summary>
    XAxis,
    /// <summary>Format Axis dialog for the vertical (Y) axis, including its gridlines.</summary>
    YAxis,
    DataSeries,
    DataLabels,
    /// <summary>Format Trendline dialog.</summary>
    Trendline,
    /// <summary>Format Error Bars dialog.</summary>
    ErrorBars,
}

public enum ChartElementDeleteAction
{
    /// <summary>Nothing to delete (element not removable or already absent).</summary>
    None,
    /// <summary>Apply <see cref="ChartElementDeletePlan.Options"/> through the chart-layout command.</summary>
    ApplyLayout,
    /// <summary>Remove the series at <see cref="ChartElementDeletePlan.SeriesOrdinal"/>.</summary>
    RemoveSeries,
    /// <summary>The chart area itself is selected: Delete removes the whole chart object.</summary>
    DeleteChart,
}

public sealed record ChartElementDeletePlan(
    ChartElementDeleteAction Action,
    ChartLayoutOptions? Options = null,
    int SeriesOrdinal = -1)
{
    public static readonly ChartElementDeletePlan None = new(ChartElementDeleteAction.None);
}

/// <summary>
/// Maps a selected <see cref="ChartElementRef"/> onto the chart model's existing editing paths: which
/// dialog "Format Selection" opens, what Delete does, and what to call the element in status text.
/// Shells own selection state, command execution and dialogs; this owns the policy so WPF and Avalonia
/// behave identically.
/// </summary>
public static class ChartElementCommandPlanner
{
    public const string SelectedStatusResourceKey = "ChartElement_Selected";
    public const string DeletedStatusResourceKey = "ChartElement_Deleted";
    public const string CannotDeleteStatusResourceKey = "ChartElement_CannotDelete";

    /// <summary>True when the element is the horizontal/X axis family for <paramref name="chart"/>.</summary>
    public static bool IsHorizontalAxis(ChartModel chart, ChartElementKind kind)
    {
        var barFamily = ChartElementMapPlanner.IsBarFamily(chart.Type);
        return kind switch
        {
            ChartElementKind.CategoryAxis or ChartElementKind.CategoryAxisTitle or ChartElementKind.CategoryGridlines => !barFamily,
            ChartElementKind.ValueAxis or ChartElementKind.ValueAxisTitle or ChartElementKind.ValueGridlines => barFamily,
            _ => false,
        };
    }

    public static ChartElementFormatTarget GetFormatTarget(ChartModel chart, ChartElementRef element)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return element.Kind switch
        {
            ChartElementKind.ChartTitle
                or ChartElementKind.SecondaryAxisTitle
                or ChartElementKind.CategoryAxisTitle
                or ChartElementKind.ValueAxisTitle => ChartElementFormatTarget.ChartTitles,
            ChartElementKind.CategoryAxis
                or ChartElementKind.ValueAxis
                or ChartElementKind.CategoryGridlines
                or ChartElementKind.ValueGridlines => IsHorizontalAxis(chart, element.Kind)
                    ? ChartElementFormatTarget.XAxis
                    : ChartElementFormatTarget.YAxis,
            // The secondary value axis has no dialog of its own; it shares the vertical axis dialog.
            ChartElementKind.SecondaryValueAxis => ChartElementFormatTarget.YAxis,
            ChartElementKind.Series or ChartElementKind.DataPoint => ChartElementFormatTarget.DataSeries,
            ChartElementKind.Trendline => ChartElementFormatTarget.Trendline,
            ChartElementKind.ErrorBars => ChartElementFormatTarget.ErrorBars,
            ChartElementKind.DataLabels => ChartElementFormatTarget.DataLabels,
            _ => ChartElementFormatTarget.ChartArea,
        };
    }

    /// <summary>Plans what the Delete key does to <paramref name="element"/> on <paramref name="chart"/>.</summary>
    public static ChartElementDeletePlan PlanDelete(ChartModel chart, ChartElementRef element)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var horizontal = IsHorizontalAxis(chart, element.Kind);
        switch (element.Kind)
        {
            case ChartElementKind.ChartArea:
                return new ChartElementDeletePlan(ChartElementDeleteAction.DeleteChart);
            case ChartElementKind.ChartTitle:
                return string.IsNullOrEmpty(chart.Title)
                    ? ChartElementDeletePlan.None
                    : Layout(new ChartLayoutOptions(Title: string.Empty));
            case ChartElementKind.Legend:
                return chart.ShowLegend ? Layout(new ChartLayoutOptions(ShowLegend: false)) : ChartElementDeletePlan.None;
            case ChartElementKind.CategoryAxisTitle:
            case ChartElementKind.ValueAxisTitle:
                return horizontal
                    ? Layout(new ChartLayoutOptions(XAxisTitle: string.Empty))
                    : Layout(new ChartLayoutOptions(YAxisTitle: string.Empty));
            case ChartElementKind.CategoryAxis:
            case ChartElementKind.ValueAxis:
                return horizontal
                    ? Layout(new ChartLayoutOptions(ShowXAxisLabels: false))
                    : Layout(new ChartLayoutOptions(ShowYAxisLabels: false));
            case ChartElementKind.SecondaryValueAxis:
                return Layout(new ChartLayoutOptions(ShowSecondaryAxis: false));
            case ChartElementKind.SecondaryAxisTitle:
                return string.IsNullOrEmpty(chart.SecondaryAxisTitle)
                    ? ChartElementDeletePlan.None
                    : Layout(new ChartLayoutOptions { SecondaryAxisTitle = string.Empty });
            case ChartElementKind.Trendline:
                return chart.ShowLinearTrendline
                    ? Layout(new ChartLayoutOptions(ShowLinearTrendline: false))
                    : ChartElementDeletePlan.None;
            case ChartElementKind.ErrorBars:
                return chart.ShowErrorBars
                    ? Layout(new ChartLayoutOptions(ShowErrorBars: false))
                    : ChartElementDeletePlan.None;
            case ChartElementKind.LegendEntry:
                return Layout(new ChartLayoutOptions(LegendEntries: HideLegendEntry(chart, element.SeriesIndex)));
            case ChartElementKind.CategoryGridlines:
            case ChartElementKind.ValueGridlines:
                return horizontal
                    ? Layout(new ChartLayoutOptions(ShowXAxisMajorGridlines: false))
                    : Layout(new ChartLayoutOptions(ShowYAxisMajorGridlines: false));
            case ChartElementKind.DataLabels:
                return chart.ShowDataLabels ? Layout(new ChartLayoutOptions(ShowDataLabels: false)) : ChartElementDeletePlan.None;
            case ChartElementKind.Series:
                return new ChartElementDeletePlan(
                    ChartElementDeleteAction.RemoveSeries,
                    SeriesOrdinal: ResolveSeriesOrdinal(chart, element.SeriesIndex));
            default:
                // The plot area has no model-level "removed" state, so it cannot be deleted.
                return ChartElementDeletePlan.None;
        }
    }

    /// <summary>
    /// The legend-entry list with <paramref name="entryIndex"/> (series index, or category index for pie
    /// charts) marked deleted, addressed the way OOXML <c>c:legendEntry</c> does (declaration order).
    /// </summary>
    public static IReadOnlyList<ChartLegendEntryModel> HideLegendEntry(ChartModel chart, int entryIndex)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var isPie = chart.Type is ChartType.Pie or ChartType.ThreeDPie or ChartType.Doughnut;
        var xmlIndex = entryIndex;
        if (!isPie && chart.SeriesPlotOrder.Count > 0)
        {
            var at = chart.SeriesPlotOrder.IndexOf(entryIndex);
            if (at >= 0)
                xmlIndex = at;
        }

        var entries = chart.LegendEntries.ToList();
        var existing = entries.FindIndex(e => e.Index == xmlIndex);
        if (existing >= 0)
            entries[existing] = entries[existing] with { IsDeleted = true };
        else
            entries.Add(new ChartLegendEntryModel(xmlIndex, true));
        return entries;
    }

    /// <summary>
    /// The position of <paramref name="seriesIndex"/> among the chart's plotted series, which is what
    /// <see cref="RemoveChartSeriesCommand"/> addresses. The renderer's series index is already the
    /// plotted position (RemoveChartSeriesCommand renumbers the column mapping sequentially), so this
    /// is the identity; it exists so hosts never hand-roll that assumption.
    /// </summary>
    public static int ResolveSeriesOrdinal(ChartModel chart, int seriesIndex)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return seriesIndex;
    }

    /// <summary>The localized display name descriptor of <paramref name="element"/> for status text.</summary>
    public static LocalizedTextDescriptor Describe(ChartElementRef element, string? seriesName = null)
    {
        if (element.Kind == ChartElementKind.Series)
        {
            var name = string.IsNullOrWhiteSpace(seriesName) ? $"Series {element.SeriesIndex + 1}" : seriesName;
            return LocalizedTextDescriptor.Resource("ChartElement_Series", name);
        }

        if (element.Kind == ChartElementKind.DataLabels && element.SeriesIndex >= 0 && !string.IsNullOrWhiteSpace(seriesName))
            return LocalizedTextDescriptor.Resource("ChartElement_SeriesDataLabels", seriesName);

        if (element.Kind is ChartElementKind.Trendline or ChartElementKind.ErrorBars)
        {
            var key = element.Kind == ChartElementKind.Trendline ? "ChartElement_Trendline" : "ChartElement_ErrorBars";
            var seriesKey = element.Kind == ChartElementKind.Trendline ? "ChartElement_SeriesTrendline" : "ChartElement_SeriesErrorBars";
            return string.IsNullOrWhiteSpace(seriesName)
                ? LocalizedTextDescriptor.Resource(key)
                : LocalizedTextDescriptor.Resource(seriesKey, seriesName);
        }

        if (element.Kind == ChartElementKind.DataPoint)
        {
            var name = string.IsNullOrWhiteSpace(seriesName) ? $"Series {element.SeriesIndex + 1}" : seriesName;
            return LocalizedTextDescriptor.Resource("ChartElement_DataPoint", name, element.PointIndex + 1);
        }

        if (element.Kind == ChartElementKind.LegendEntry)
        {
            var name = string.IsNullOrWhiteSpace(seriesName) ? $"{element.SeriesIndex + 1}" : seriesName;
            return LocalizedTextDescriptor.Resource("ChartElement_LegendEntry", name);
        }

        return LocalizedTextDescriptor.Resource(element.Kind switch
        {
            ChartElementKind.PlotArea => "ChartElement_PlotArea",
            ChartElementKind.ChartTitle => "ChartElement_ChartTitle",
            ChartElementKind.Legend => "ChartElement_Legend",
            ChartElementKind.CategoryAxis => "ChartElement_CategoryAxis",
            ChartElementKind.ValueAxis => "ChartElement_ValueAxis",
            ChartElementKind.SecondaryValueAxis => "ChartElement_SecondaryValueAxis",
            ChartElementKind.CategoryAxisTitle => "ChartElement_CategoryAxisTitle",
            ChartElementKind.ValueAxisTitle => "ChartElement_ValueAxisTitle",
            ChartElementKind.SecondaryAxisTitle => "ChartElement_SecondaryAxisTitle",
            ChartElementKind.CategoryGridlines => "ChartElement_CategoryGridlines",
            ChartElementKind.ValueGridlines => "ChartElement_ValueGridlines",
            ChartElementKind.DataLabels => "ChartElement_DataLabels",
            _ => "ChartElement_ChartArea",
        });
    }

    /// <summary>
    /// Builds the workbook command that carries out <paramref name="plan"/> on <paramref name="chart"/>, or
    /// null when the plan has nothing to execute (<see cref="ChartElementDeleteAction.None"/> /
    /// <see cref="ChartElementDeleteAction.DeleteChart"/>, which the host handles as a plain object delete).
    /// </summary>
    public static IWorkbookCommand? BuildDeleteCommand(SheetId sheetId, ChartModel chart, ChartElementDeletePlan plan)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(plan);

        return plan.Action switch
        {
            ChartElementDeleteAction.ApplyLayout when plan.Options is { } options =>
                ChartCommandWorkflowPlanner.BuildLayoutCommand(sheetId, chart, options),
            ChartElementDeleteAction.RemoveSeries =>
                ChartCommandWorkflowPlanner.BuildRemoveSeriesCommand(sheetId, chart, plan.SeriesOrdinal),
            _ => null,
        };
    }

    private static ChartElementDeletePlan Layout(ChartLayoutOptions options) =>
        new(ChartElementDeleteAction.ApplyLayout, options);
}
