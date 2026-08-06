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
        PivotTargetFallback fallback = PivotTargetFallback.FirstOnSheet)
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
            ShowPivotApplicationIssue(resolution.Message);
            return false;
        }

        target = resolved;
        return true;
    }

    private bool ApplyPivotApplicationPlan(PivotApplicationPlan plan)
    {
        var outcome = PivotApplication.Execute(plan);
        if (!outcome.Success)
        {
            ShowPivotApplicationIssue(outcome.Message);
            return false;
        }

        ApplyPivotApplicationOutcome(outcome);
        return true;
    }

    private void ApplyPivotApplicationOutcome(PivotApplicationOutcome outcome)
    {
        if (outcome.Transition.ActivateSheetId is { } sheetId)
            _session.SelectSheet(sheetId);
        if (outcome.Transition.SelectionRange is { } selectionRange)
            _session.SelectRange(selectionRange);

        _pivotPaneSignature = null;
        RefreshShell(PivotSuccessStatus(outcome));
    }

    private void ShowPivotApplicationIssue(PivotMessageModel? message)
    {
        if (message is null)
            return;

        ShowEditIssue(PivotApplicationIssueText(message));
    }

    private static string PivotApplicationIssueText(PivotMessageModel message) =>
        message.Issue switch
        {
            PivotApplicationIssue.MissingSource or
            PivotApplicationIssue.MinimumSourceShape or
            PivotApplicationIssue.MissingSourceHeaders or
            PivotApplicationIssue.InvalidSourceReference =>
                UiText.Get("PivotLoc_SelectRangeForPivot"),
            PivotApplicationIssue.MissingValueField =>
                UiText.Get("PivotLoc_AssignAtLeastOneValue"),
            PivotApplicationIssue.InvalidDestinationReference or
            PivotApplicationIssue.DestinationOutOfBounds =>
                UiText.Get("MovePivot_InvalidDestination"),
            PivotApplicationIssue.DestinationMustBeOnCurrentSheet =>
                UiText.Get("MovePivot_CurrentSheetOnly"),
            PivotApplicationIssue.EmptyName or
            PivotApplicationIssue.DuplicateName or
            PivotApplicationIssue.InvalidDataSource or
            PivotApplicationIssue.CommandFailed =>
                message.Detail ?? UiText.Get("PivotLoc_UpdateFailed"),
            PivotApplicationIssue.NoPivotTable =>
                UiText.Get("PivotLoc_SelectCellToChangeLayout"),
            _ => message.Detail ?? UiText.Get("PivotLoc_UpdateFailed"),
        };

    private static string PivotSuccessStatus(PivotApplicationOutcome outcome) =>
        outcome.Action switch
        {
            PivotApplicationAction.Create =>
                UiText.Format("PivotLoc_InsertedPivotTableFrom", outcome.StatusArgument ?? string.Empty),
            PivotApplicationAction.Refresh =>
                UiText.Format("PivotLoc_RefreshedPivot", outcome.StatusArgument ?? string.Empty),
            PivotApplicationAction.Rename =>
                UiText.Format("PivotName_Renamed", outcome.StatusArgument ?? string.Empty),
            PivotApplicationAction.Move =>
                UiText.Format("MovePivot_Moved", outcome.StatusArgument ?? string.Empty),
            PivotApplicationAction.ChangeDataSource =>
                UiText.Format("PivotDataSource_Changed", outcome.StatusArgument ?? string.Empty),
            PivotApplicationAction.Clear =>
                UiText.Format("PivotAnalyze_Cleared", outcome.StatusArgument ?? string.Empty),
            PivotApplicationAction.Select =>
                UiText.Format("PivotAnalyze_Selected", outcome.StatusArgument ?? string.Empty),
            PivotApplicationAction.ShowDetails =>
                UiText.Format("PivotAnalyze_ShowDetailsDone", outcome.StatusArgument ?? string.Empty),
            _ => outcome.StatusArgument ?? string.Empty,
        };
}
