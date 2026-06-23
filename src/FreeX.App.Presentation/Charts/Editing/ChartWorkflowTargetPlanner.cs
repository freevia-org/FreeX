using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>
/// Shared chart-command target policy. Renderers own selection state and command execution; this planner
/// decides which charts are eligible for contextual chart workflows.
/// </summary>
public static class ChartWorkflowTargetPlanner
{
    public static bool IsContextualTarget(ChartModel chart) =>
        chart.IsVisible && !chart.IsPivotChart;

    public static ChartModel? FindSelectedChart(Sheet? sheet, Guid? selectedChartId)
    {
        if (sheet is null || selectedChartId is null || selectedChartId == Guid.Empty)
            return null;

        foreach (var chart in sheet.Charts)
        {
            if (chart.Id == selectedChartId && IsContextualTarget(chart))
                return chart;
        }

        return null;
    }

    public static ChartModel? FindSelectedOrFirstChart(Sheet? sheet, Guid? selectedChartId) =>
        FindSelectedChart(sheet, selectedChartId) ?? FindFirstChart(sheet);

    public static ChartModel? FindFirstChart(Sheet? sheet)
    {
        if (sheet is null)
            return null;

        foreach (var chart in sheet.Charts)
        {
            if (IsContextualTarget(chart))
                return chart;
        }

        return null;
    }

    public static bool HasSelectedChart(Sheet? sheet, Guid? selectedChartId) =>
        FindSelectedChart(sheet, selectedChartId) is not null;
}
