using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Ribbon;

public static class WaterfallChartContextMenuPlanner
{
    public static IReadOnlyList<WaterfallChartContextMenuCommand> BuildCommands(
        ChartModel chart,
        int pointIndex)
    {
        var isValidPoint = IsValidWaterfallPoint(chart, pointIndex);
        return
        [
            new(
                "Set as Total",
                IsChecked: isValidPoint && IsPointTotal(chart, pointIndex),
                IsEnabled: isValidPoint,
                AccessHeader: "_Set as Total")
        ];
    }

    public static bool IsPointTotal(ChartModel chart, int pointIndex)
    {
        if (!IsValidWaterfallPoint(chart, pointIndex))
            return false;

        var pointCount = ChartTypeSupport.GetDataPointCount(chart);
        if (chart.WaterfallTotalPointIndices is not { } totals)
            return pointIndex == pointCount - 1;

        return totals.Contains(pointIndex);
    }

    public static SetWaterfallTotalPointCommand? CreateToggleCommand(
        SheetId sheetId,
        ChartModel chart,
        int pointIndex)
    {
        if (!IsValidWaterfallPoint(chart, pointIndex))
            return null;

        return new SetWaterfallTotalPointCommand(
            sheetId,
            chart.Id,
            pointIndex,
            setAsTotal: !IsPointTotal(chart, pointIndex));
    }

    private static bool IsValidWaterfallPoint(ChartModel chart, int pointIndex) =>
        chart.Type == ChartType.Waterfall &&
        pointIndex >= 0 &&
        pointIndex < ChartTypeSupport.GetDataPointCount(chart);
}

public sealed record WaterfallChartContextMenuCommand(
    string Header,
    bool IsChecked,
    bool IsEnabled = true,
    string? AccessHeader = null)
{
    public string AccessHeader { get; init; } = AccessHeader ?? Header;
}
