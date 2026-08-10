using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private PivotApplicationSession PivotApplication =>
        new(
            _workbook,
            (SheetId defaultSheetId, string referenceText, out GridRange range) =>
                TryParseWorkbookRange(defaultSheetId, referenceText, out range),
            (command, commandLabel) =>
            {
                var succeeded = TryExecuteCommand(command, commandLabel, out var outcome);
                return new PivotCommandExecutionResult(
                    succeeded,
                    outcome.ErrorMessage,
                    outcome.IsNoOp,
                    outcome.AffectedCells);
            });

    private bool TryResolvePivotTarget(
        string title,
        out PivotApplicationTarget target,
        PivotTargetFallback fallback = PivotTargetFallback.SelectionOnly,
        string? missingMessageResourceKey = null)
    {
        var resolution = PivotApplication.ResolveTarget(
            _currentSheetId,
            SheetGrid.SelectedRange,
            fallback);
        if (resolution.Target is { } resolved)
        {
            target = resolved;
            return true;
        }

        target = null!;
        if (missingMessageResourceKey is not null)
        {
            _messageService.ShowInfo(UiText.Get(missingMessageResourceKey), title);
        }
        else
        {
            ShowPivotApplicationMessage(resolution.Message, title);
        }

        return false;
    }

    private bool ApplyPivotApplicationPlan(PivotApplicationPlan plan, string title)
    {
        var outcome = PivotApplication.Execute(plan);
        if (!outcome.Success)
        {
            if (outcome.Message?.Issue != PivotApplicationIssue.CommandFailed)
                ShowPivotApplicationMessage(outcome.Message, title);
            return false;
        }

        ApplyPivotDisplayTransition(outcome.Action, outcome.Transition);
        return true;
    }

    private void ApplyPivotDisplayTransition(
        PivotApplicationAction action,
        PivotDisplayTransition transition)
    {
        if (transition.ActivateSheetId is { } activateSheetId)
        {
            if (action == PivotApplicationAction.Create)
                ActivateNewWorksheetAtA1(activateSheetId);
            else
                _currentSheetId = activateSheetId;
        }

        if (transition.SelectionRange is { } selectionRange)
            SetSelectionRange(selectionRange, selectionRange.Start);
        if (transition.EnsureVisible is { } ensureVisible)
            EnsureCellVisible(ensureVisible);
        if (transition.RefreshSheetTabs)
            RefreshSheetTabs();
        if (transition.RefreshViewport)
            UpdateViewport();
        if (transition.RefreshStatus)
            RefreshStatusBar();
        if (transition.RefreshFieldList)
            RefreshPivotFieldListPane();
        if (transition.RefreshSlicerTimeline)
            RefreshSlicerTimelinePane();
    }

    private void ShowPivotApplicationMessage(PivotMessageModel? message, string title)
    {
        if (message is null)
            return;

        var text = PivotApplicationMessagePlanner
            .DescribeIssue(message, PivotMessageTextProfile.Wpf)
            .Resolve(UiText.Get, UiText.Format);

        if (message.Severity == PivotMessageSeverity.Information)
            _messageService.ShowInfo(text, title);
        else
            _messageService.ShowWarning(text, title);
    }
}
