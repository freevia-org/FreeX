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
            ChartElementKind.Series => ChartElementFormatTarget.DataSeries,
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
