using FreeX.App.Presentation.Comments;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private PresentationReviewSessionController ReviewSessionController =>
        new(new PresentationReviewSessionAdapter(
            () => _session.Workbook,
            () => _session.ActiveSheet.Id,
            () => _session.SelectedRange,
            () => Environment.UserName,
            (plan, fallbackRange) =>
            {
                var result = _session.ExecuteReviewCommand(plan.CreateCommand(fallbackRange));
                return new PresentationCommentMutationExecutionResult(
                    result.Success,
                    result.ErrorMessage);
            },
            _session.SelectCell));

    private void ApplyReviewRefreshPlan(PresentationReviewRefreshPlan plan, string status)
    {
        if (plan.RefreshViewport ||
            plan.RefreshCommandStates ||
            plan.RefreshCommentPanes)
        {
            RefreshShell(status);
        }

        if (plan.RefreshCommentPanes)
            _refreshCommentListWindow?.Invoke(CollectThreadedComments(_session.ActiveSheet));
    }
}
