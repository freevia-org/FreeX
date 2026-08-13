using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

public enum ChartWorkflowTargetPolicy
{
    SelectedOnly,
    SelectedOrFirst,
}

public enum ChartLayoutCommandIssue
{
    None,
    MissingChart,
    Unsupported,
}

public sealed record ChartLayoutCommandPlan(
    ChartModel? Chart,
    SetChartLayoutCommand? Command,
    ChartLayoutCommandIssue Issue)
{
    public bool CanExecute => Command is not null;
}

public sealed record ChartMoveCommandPlan(
    IWorkbookCommand? Command,
    SheetId? ExistingTargetSheetId,
    string TargetName,
    string? Error)
{
    public bool CanExecute => Command is not null && Error is null;
}

/// <summary>
/// Portable command workflow for normal charts. Renderers collect native input and submit the returned
/// commands through their own command bus; target policy, support checks, and command composition live here.
/// </summary>
public static class ChartCommandWorkflowPlanner
{
    public static ChartType? ChartTypeForRibbonCommand(string commandId) =>
        ChartInsertionPlanner.ChartTypeForRibbonCommand(commandId);

    public static ChartInsertionPlan CreateEmbeddedChartPlan(
        Sheet sheet,
        GridRange selectedRange,
        ChartType chartType,
        ChartInsertionViewport viewport,
        string? title = "Chart") =>
        ChartInsertionPlanner.CreateEmbeddedChartPlan(sheet, selectedRange, chartType, viewport, title);

    public static ChartInsertionPlan CreateEmbeddedChartPlan(
        SheetId sheetId,
        GridRange dataRange,
        ChartType chartType,
        string? title,
        ChartInsertionPlacement placement) =>
        ChartInsertionPlanner.CreateEmbeddedChartPlan(sheetId, dataRange, chartType, title, placement);

    public static AddChartCommand BuildEmbeddedChartCommand(
        Sheet sheet,
        GridRange selectedRange,
        ChartType chartType,
        string? title = null,
        ChartInsertionPlacement? placement = null) =>
        ChartInsertionPlanner.BuildEmbeddedChartCommand(sheet, selectedRange, chartType, title, placement);

    public static AddChartCommand BuildEmbeddedChartCommand(
        SheetId sheetId,
        GridRange dataRange,
        ChartType chartType,
        string? title,
        ChartInsertionPlacement placement) =>
        ChartInsertionPlanner.BuildEmbeddedChartCommand(sheetId, dataRange, chartType, title, placement);

    public static AddChartSheetCommand BuildChartSheetCommand(
        Sheet? sheet,
        SheetId sheetId,
        GridRange selectedRange,
        ChartType chartType,
        string title) =>
        ChartInsertionPlanner.BuildChartSheetCommand(sheet, sheetId, selectedRange, chartType, title);

    public static RemoveChartSeriesCommand BuildRemoveSeriesCommand(
        SheetId sheetId,
        ChartModel chart,
        int seriesIndex)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new RemoveChartSeriesCommand(sheetId, chart.Id, seriesIndex);
    }

    public static ConfigureChartHiddenEmptyCellsCommand BuildHiddenEmptyCellsCommand(
        SheetId sheetId,
        ChartModel chart,
        ChartBlankDisplayMode blankDisplayMode,
        bool showDataInHiddenRowsAndColumns)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ConfigureChartHiddenEmptyCellsCommand(
            sheetId,
            chart.Id,
            blankDisplayMode,
            showDataInHiddenRowsAndColumns);
    }

    public static AddPivotChartCommand BuildAddPivotChartCommand(
        SheetId sheetId,
        PivotTableModel pivotTable,
        ChartType chartType,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        return new AddPivotChartCommand(sheetId, pivotTable.Name, chartType, title);
    }

    public static ChangePivotChartTypeCommand BuildChangePivotChartTypeCommand(
        SheetId sheetId,
        ChartModel chart,
        ChartType chartType)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChangePivotChartTypeCommand(sheetId, chart.Id, chartType);
    }

    public static ConfigurePivotChartOptionsCommand BuildPivotChartOptionsCommand(
        SheetId sheetId,
        ChartModel chart,
        PivotChartOptionsInput input)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(input);
        return new ConfigurePivotChartOptionsCommand(
            sheetId,
            chart.Id,
            input.ChartStyleId,
            input.ShowFieldButtons,
            input.ShowReportFilterButtons,
            input.ShowAxisFieldButtons,
            input.ShowValueFieldButtons,
            input.ShowDataTable,
            input.ShowDataTableLegendKeys,
            input.RoundedCorners,
            input.ShowHiddenData,
            input.BlankDisplayMode);
    }

    public static ChartLayoutCommandPlan PlanLayoutCommand(
        SheetId sheetId,
        Sheet? sheet,
        Guid? selectedChartId,
        ChartWorkflowTargetPolicy targetPolicy,
        Func<ChartModel, ChartLayoutOptions> optionsFactory,
        Func<ChartModel, bool>? canApply = null)
    {
        ArgumentNullException.ThrowIfNull(optionsFactory);

        var chart = ResolveTarget(sheet, selectedChartId, targetPolicy);
        if (chart is null)
            return new ChartLayoutCommandPlan(null, null, ChartLayoutCommandIssue.MissingChart);

        if (canApply is not null && !canApply(chart))
            return new ChartLayoutCommandPlan(chart, null, ChartLayoutCommandIssue.Unsupported);

        return new ChartLayoutCommandPlan(
            chart,
            BuildLayoutCommand(sheetId, chart, optionsFactory(chart)),
            ChartLayoutCommandIssue.None);
    }

    public static ChartLayoutCommandPlan PlanQuickCommand(
        SheetId sheetId,
        Sheet? sheet,
        Guid? selectedChartId,
        ChartWorkflowTargetPolicy targetPolicy,
        ChartQuickCommandDescriptor command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return PlanLayoutCommand(
            sheetId,
            sheet,
            selectedChartId,
            targetPolicy,
            chart => ChartQuickCommandPlanner.Plan(chart, command.Command),
            chart => ChartQuickCommandPlanner.CanApply(chart, command.Command));
    }

    public static SetChartLayoutCommand BuildLayoutCommand(
        SheetId sheetId,
        ChartModel chart,
        ChartLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(options);
        return new SetChartLayoutCommand(sheetId, chart.Id, options);
    }

    public static ChangeChartTypeCommand BuildChangeTypeCommand(
        SheetId sheetId,
        ChartModel chart,
        ChartType chartType)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChangeChartTypeCommand(sheetId, chart.Id, chartType);
    }

    public static ChangeChartSourceCommand BuildChangeSourceCommand(
        SheetId sheetId,
        ChartModel chart,
        GridRange dataRange,
        bool firstColumnIsCategories,
        bool switchRowColumn)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChangeChartSourceCommand(
            sheetId,
            chart.Id,
            dataRange,
            firstRowIsHeader: chart.FirstRowIsHeader,
            firstColIsCategories: firstColumnIsCategories,
            seriesInRows: switchRowColumn);
    }

    public static ChartMoveCommandPlan PlanMoveCommand(
        Workbook workbook,
        SheetId sourceSheetId,
        ChartModel chart,
        ChartMoveInput input)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(chart);

        var plan = ChartMovePlanner.Plan(input, name => workbook.GetSheet(name) is not null);
        if (!plan.IsValid)
            return new ChartMoveCommandPlan(null, null, plan.TargetName, plan.Error);

        if (plan.TargetKind == ChartMoveTargetKind.NewSheet)
        {
            return new ChartMoveCommandPlan(
                new MoveChartToNewSheetCommand(sourceSheetId, chart.Id, plan.TargetName),
                null,
                plan.TargetName,
                null);
        }

        var targetSheet = workbook.GetSheet(plan.TargetName);
        if (targetSheet is null)
            return new ChartMoveCommandPlan(null, null, plan.TargetName, $"There is no sheet named '{plan.TargetName}'.");

        return new ChartMoveCommandPlan(
            new MoveChartCommand(sourceSheetId, chart.Id, targetSheet.Id),
            targetSheet.Id,
            plan.TargetName,
            null);
    }

    public static SetChartStyleCommand BuildStyleCommand(
        SheetId sheetId,
        ChartModel chart,
        int? chartStyleId)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new SetChartStyleCommand(sheetId, chart.Id, chartStyleId);
    }

    public static SetChartBoundsCommand BuildBoundsCommand(
        SheetId sheetId,
        ChartModel chart,
        double left,
        double top,
        double width,
        double height)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return BuildBoundsCommand(sheetId, chart.Id, left, top, width, height);
    }

    public static SetChartBoundsCommand BuildBoundsCommand(
        SheetId sheetId,
        Guid chartId,
        double left,
        double top,
        double width,
        double height) =>
        new(sheetId, chartId, left, top, width, height);

    private static ChartModel? ResolveTarget(
        Sheet? sheet,
        Guid? selectedChartId,
        ChartWorkflowTargetPolicy targetPolicy) =>
        targetPolicy == ChartWorkflowTargetPolicy.SelectedOnly
            ? ChartWorkflowTargetPlanner.FindSelectedChart(sheet, selectedChartId)
            : ChartWorkflowTargetPlanner.FindSelectedOrFirstChart(sheet, selectedChartId);
}
