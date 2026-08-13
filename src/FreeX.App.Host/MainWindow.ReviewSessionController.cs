using FreeX.App.Presentation.Comments;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private PresentationReviewSessionController ReviewSessionController =>
        new(new PresentationReviewSessionAdapter(
            () => _workbook,
            () => _currentSheetId,
            () => SheetGrid.SelectedRange,
            () => AppOptions.NormalizeUserName(_options.UserName),
            (plan, fallbackRange) =>
            {
                if (!TryExecuteRepeatableCurrentRangeCommand(
                        plan.Label,
                        fallbackRange,
                        plan.CreateCommand,
                        out var outcome))
                {
                    return new PresentationCommentMutationExecutionResult(
                        false,
                        LocalizeCommandErrorMessage(outcome.ErrorMessage),
                        outcome.IsNoOp);
                }

                return new PresentationCommentMutationExecutionResult(true, null, outcome.IsNoOp);
            },
            SetActiveCell));

    private void ApplyReviewRefreshPlan(PresentationReviewRefreshPlan plan)
    {
        if (plan.RefreshViewport)
            UpdateViewport();
        if (plan.RefreshCommandStates)
            RefreshReviewCommentNoteCommandStates();
        if (plan.RefreshCommentPanes)
            RefreshOpenReviewCommentNoteWindows();
    }
}
