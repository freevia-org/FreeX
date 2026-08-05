using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public sealed record PageLayoutCommandExecutionPlan(
    IWorkbookCommand Command,
    string CommandLabel,
    PageLayoutCommandStatusPlan? Status = null,
    string? SuccessStatusText = null);

/// <summary>
/// Owns the target-sheet set and portable command composition for Page Layout actions. Platform hosts
/// remain responsible for native controls, dialogs, command execution, and visual refreshes.
/// </summary>
public sealed class PageLayoutCommandSession
{
    private readonly IReadOnlyList<SheetId> _targetSheetIds;

    public PageLayoutCommandSession(IEnumerable<SheetId> targetSheetIds)
    {
        ArgumentNullException.ThrowIfNull(targetSheetIds);

        _targetSheetIds = targetSheetIds.Distinct().ToArray();
        if (_targetSheetIds.Count == 0)
            throw new ArgumentException("At least one target sheet is required.", nameof(targetSheetIds));
    }

    public IReadOnlyList<SheetId> TargetSheetIds => _targetSheetIds;

    public PageLayoutCommandExecutionPlan PlanMarginsPreset(PageLayoutMarginPreset preset)
    {
        var presetPlan = PageLayoutRibbonActionPlanner.PlanMarginsPreset(preset);
        return PlanForTargets(
            presetPlan.CommandLabel,
            sheetId => PageLayoutRibbonCommandPlanner.BuildMarginsCommand(
                sheetId,
                presetPlan.Value,
                presetPlan.HeaderMargin,
                presetPlan.FooterMargin),
            PageLayoutStatusPlanner.ForPreset(presetPlan));
    }

    public PageLayoutCommandExecutionPlan PlanOrientationPreset(PageLayoutOrientationPreset preset)
    {
        var presetPlan = PageLayoutRibbonActionPlanner.PlanOrientationPreset(preset);
        return PlanForTargets(
            presetPlan.CommandLabel,
            sheetId => PageLayoutRibbonCommandPlanner.BuildOrientationCommand(sheetId, presetPlan.Value),
            PageLayoutStatusPlanner.ForPreset(presetPlan));
    }

    public PageLayoutCommandExecutionPlan PlanPaperSizePreset(PageLayoutPaperSizePreset preset)
    {
        var presetPlan = PageLayoutRibbonActionPlanner.PlanPaperSizePreset(preset);
        return PlanForTargets(
            presetPlan.CommandLabel,
            sheetId => PageLayoutRibbonCommandPlanner.BuildPaperSizeCommand(sheetId, presetPlan.Value),
            PageLayoutStatusPlanner.ForPreset(presetPlan));
    }

    public PageLayoutCommandExecutionPlan PlanSetPrintArea(GridRange range) =>
        PlanForTargets(
            PageLayoutRibbonActionPlanner.PrintAreaCommandLabel,
            sheetId => PageLayoutRibbonCommandPlanner.BuildSetPrintAreaCommand(sheetId, range),
            PageLayoutStatusPlanner.PrintAreaSet);

    public PageLayoutCommandExecutionPlan PlanClearPrintArea() =>
        PlanForTargets(
            PageLayoutRibbonActionPlanner.PrintAreaCommandLabel,
            PageLayoutRibbonCommandPlanner.BuildClearPrintAreaCommand,
            PageLayoutStatusPlanner.PrintAreaClear);

    public PageLayoutCommandExecutionPlan PlanSetBackground(WorksheetBackgroundImage background)
    {
        ArgumentNullException.ThrowIfNull(background);

        return PlanForTargets(
            PageLayoutRibbonActionPlanner.BackgroundCommandLabel,
            sheetId => PageLayoutRibbonCommandPlanner.BuildSetBackgroundCommand(sheetId, background));
    }

    public PageLayoutCommandExecutionPlan PlanClearBackground() =>
        PlanForTargets(
            PageLayoutRibbonActionPlanner.ClearBackgroundCommandLabel,
            PageLayoutRibbonCommandPlanner.BuildClearBackgroundCommand);

    public PageLayoutCommandExecutionPlan PlanScaleToFit(WorksheetScaleToFit scaleToFit) =>
        PlanForTargets(
            PageLayoutRibbonActionPlanner.ScaleToFitCommandLabel,
            sheetId => PageLayoutRibbonCommandPlanner.BuildScaleToFitCommand(sheetId, scaleToFit));

    public PageLayoutCommandExecutionPlan PlanPageBreakAction(
        PageBreakMenuAction action,
        GridRange selection,
        IEnumerable<uint> currentRowBreaks,
        IEnumerable<uint> currentColumnBreaks)
    {
        var actionPlan = PageLayoutRibbonCommandPlanner.PlanPageBreakAction(
            action,
            selection,
            currentRowBreaks,
            currentColumnBreaks);
        return PlanPageBreaks(actionPlan, actionPlan.Status);
    }

    public PageLayoutCommandExecutionPlan PlanPageBreaks(
        IReadOnlyList<uint> rowBreaks,
        IReadOnlyList<uint> columnBreaks,
        string? successStatusText = null) =>
        PlanForTargets(
            PageLayoutRibbonActionPlanner.PageBreaksCommandLabel,
            sheetId => PageLayoutRibbonCommandPlanner.BuildPageBreaksCommand(sheetId, rowBreaks, columnBreaks),
            successStatusText: successStatusText);

    public PageLayoutCommandExecutionPlan PlanPageBreaks(
        PageBreakActionPlan plan,
        string? successStatusText = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return PlanPageBreaks(plan.RowBreaks, plan.ColumnBreaks, successStatusText);
    }

    public PageLayoutCommandExecutionPlan PlanPageBreaks(PageBreakSelectionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return PlanPageBreaks(plan.RowBreaks, plan.ColumnBreaks);
    }

    public PageLayoutCommandExecutionPlan PlanHeaderFooter(PageSetupHeaderFooterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PlanForTargets(
            PageLayoutRibbonActionPlanner.HeaderFooterCommandLabel,
            sheetId => PageSetupCommandFactory.BuildHeaderFooterCommand(sheetId, request),
            PageLayoutStatusPlanner.PageSetupSubmission);
    }

    public PageLayoutCommandExecutionPlan PlanPrintGridlines(
        bool printGridlines,
        bool currentPrintHeadings) =>
        PlanForTargets(
            PageLayoutRibbonActionPlanner.PrintGridlinesCommandLabel,
            sheetId => PageLayoutRibbonCommandPlanner.BuildPrintGridlinesCommand(
                sheetId,
                printGridlines,
                currentPrintHeadings));

    public PageLayoutCommandExecutionPlan PlanPrintHeadings(
        bool currentPrintGridlines,
        bool printHeadings) =>
        PlanForTargets(
            PageLayoutRibbonActionPlanner.PrintHeadingsCommandLabel,
            sheetId => PageLayoutRibbonCommandPlanner.BuildPrintHeadingsCommand(
                sheetId,
                currentPrintGridlines,
                printHeadings));

    private PageLayoutCommandExecutionPlan PlanForTargets(
        string commandLabel,
        Func<SheetId, IWorkbookCommand> commandFactory,
        PageLayoutCommandStatusPlan? status = null,
        string? successStatusText = null)
    {
        var commands = _targetSheetIds.Select(commandFactory).ToArray();
        var command = commands.Length == 1
            ? commands[0]
            : new CompositeWorkbookCommand(commandLabel, commands);
        return new PageLayoutCommandExecutionPlan(command, commandLabel, status, successStatusText);
    }
}
