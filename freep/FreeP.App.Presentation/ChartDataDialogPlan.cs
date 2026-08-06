namespace FreeP.App.Compositor;

public enum ChartDataDialogActionId
{
    AddSeries,
    RemoveSeries,
    MoveSeriesUp,
    MoveSeriesDown,
    AddCategory,
    RemoveCategory,
    MoveCategoryLeft,
    MoveCategoryRight,
    SwitchRowsAndColumns,
    Accept,
    Cancel,
}

public sealed record ChartDataDialogActionPlan(
    ChartDataDialogActionId Id,
    string Label,
    string AccessibleName,
    string AutomationId);

public sealed record ChartDataDialogActionGroupPlan(
    string Id,
    string AccessibleName,
    IReadOnlyList<ChartDataDialogActionPlan> Actions);

public sealed record ChartDataDialogChoicePlan(
    string Label,
    string AccessibleName,
    string AutomationId,
    int SelectedIndex,
    IReadOnlyList<string> Choices);

public sealed record ChartDataDialogTablePlan(
    string AccessibleName,
    string AutomationId,
    string ValidationAccessibleName,
    string ValidationAutomationId);

public sealed record ChartDataDialogPlan(
    string CommandId,
    string Title,
    double Width,
    double Height,
    double MinimumWidth,
    double MinimumHeight,
    bool IsResizable,
    IReadOnlyList<ChartDataDialogActionGroupPlan> ToolbarGroups,
    ChartDataDialogChoicePlan ChartType,
    ChartDataDialogTablePlan Table,
    ChartDataDialogActionPlan AcceptAction,
    ChartDataDialogActionPlan CancelAction);

public static class ChartDataDialogPlanCatalog
{
    public static ChartDataDialogPlan BuildDialogPlan(this ChartDataDialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var surface = session.Surface;
        return new ChartDataDialogPlan(
            surface.CommandId,
            surface.Title,
            surface.Width,
            surface.Height,
            520,
            320,
            true,
            [
                Group("series", "Series commands",
                    Action(ChartDataDialogActionId.AddSeries, surface.AddSeriesLabel, "Add series"),
                    Action(ChartDataDialogActionId.RemoveSeries, surface.RemoveSeriesLabel, "Remove series"),
                    Action(ChartDataDialogActionId.MoveSeriesUp, surface.MoveSeriesUpLabel, "Move series up"),
                    Action(ChartDataDialogActionId.MoveSeriesDown, surface.MoveSeriesDownLabel, "Move series down")),
                Group("category", "Category commands",
                    Action(ChartDataDialogActionId.AddCategory, surface.AddCategoryLabel, "Add category"),
                    Action(ChartDataDialogActionId.RemoveCategory, surface.RemoveCategoryLabel, "Remove category"),
                    Action(ChartDataDialogActionId.MoveCategoryLeft, surface.MoveCategoryLeftLabel, "Move category left"),
                    Action(ChartDataDialogActionId.MoveCategoryRight, surface.MoveCategoryRightLabel, "Move category right")),
                Group("table", "Table commands",
                    Action(ChartDataDialogActionId.SwitchRowsAndColumns, surface.SwitchRowsAndColumnsLabel, "Switch rows and columns")),
            ],
            new ChartDataDialogChoicePlan(
                surface.ChartTypeLabel,
                "Chart type",
                "FreeP.ChartData.ChartType",
                session.SelectedChartTypeIndex,
                session.ChartTypeOptions.Select(option => option.Label).ToArray()),
            new ChartDataDialogTablePlan(
                "Chart data table",
                "FreeP.ChartData.Table",
                "Chart data validation error",
                "FreeP.ChartData.Validation"),
            Action(ChartDataDialogActionId.Accept, surface.OkLabel, "Apply chart data changes"),
            Action(ChartDataDialogActionId.Cancel, surface.CancelLabel, "Cancel chart data changes"));
    }

    private static ChartDataDialogActionGroupPlan Group(
        string id,
        string accessibleName,
        params ChartDataDialogActionPlan[] actions) =>
        new(id, accessibleName, actions);

    private static ChartDataDialogActionPlan Action(
        ChartDataDialogActionId id,
        string label,
        string accessibleName) =>
        new(id, label, accessibleName, $"FreeP.ChartData.{id}");
}
