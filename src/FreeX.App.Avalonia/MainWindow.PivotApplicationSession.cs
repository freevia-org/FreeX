using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private PivotApplicationSession PivotApplication =>
        new(
            _session.Workbook,
            (SheetId _, string referenceText, out GridRange range) =>
                _session.TryResolveReferenceRange(referenceText, out range),
            (command, _) =>
            {
                var result = _session.ExecuteReviewCommand(command);
                return new PivotCommandExecutionResult(
                    result.Success,
                    result.ErrorMessage,
                    result.IsNoOp,
                    result.AffectedCells);
            });

    private bool TryResolvePivotApplicationTarget(
        out PivotApplicationTarget target,
        PivotTargetFallback fallback = PivotTargetFallback.FirstOnSheet,
        string? missingMessage = null)
    {
        target = null!;
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return false;

        var resolution = PivotApplication.ResolveTarget(
            _session.ActiveSheet.Id,
            _session.SelectedRange,
            fallback);
        if (resolution.Target is not { } resolved)
        {
            if (missingMessage is not null)
                ShowEditIssue(missingMessage);
            else
                ShowPivotApplicationIssue(resolution.Message);
            return false;
        }

        target = resolved;
        return true;
    }

    private bool ApplyPivotApplicationPlan(
        PivotApplicationPlan plan,
        string? successStatus = null)
    {
        var outcome = PivotApplication.Execute(plan);
        if (!outcome.Success)
        {
            ShowPivotApplicationIssue(outcome.Message);
            return false;
        }

        ApplyPivotApplicationOutcome(outcome, successStatus);
        return true;
    }

    private void ApplyPivotApplicationOutcome(
        PivotApplicationOutcome outcome,
        string? successStatus = null)
    {
        if (outcome.Transition.ActivateSheetId is { } sheetId)
            _session.SelectSheet(sheetId);
        if (outcome.Transition.SelectionRange is { } selectionRange)
            _session.SelectRange(selectionRange);

        _pivotPaneSignature = null;
        RefreshShell(successStatus ?? PivotSuccessStatus(outcome));
    }

    private void ShowPivotApplicationIssue(PivotMessageModel? message)
    {
        if (message is null)
            return;

        ShowEditIssue(PivotApplicationIssueText(message));
    }

    private static string PivotApplicationIssueText(PivotMessageModel message) =>
        PivotApplicationMessagePlanner
            .DescribeIssue(message, PivotMessageTextProfile.Avalonia)
            .Resolve(UiText.Get, UiText.Format);

    private static string PivotSuccessStatus(PivotApplicationOutcome outcome) =>
        PivotApplicationMessagePlanner.DescribeSuccess(outcome).Resolve(UiText.Get, UiText.Format);
}
