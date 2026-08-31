using FreeX.App.Presentation.Comments;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private PresentationReviewSessionController ReviewSessionController =>
        new(new PresentationReviewSessionAdapter(
            () => _session.Workbook,
            () => _session.ActiveSheet.Id,
            () => _session.SelectedRange,
            // r176: honour the configured author name, as the WPF shell has always done.
            // Hardcoding Environment.UserName here meant Options > User name silently did
            // nothing for comments in this shell, and every comment inserted on Linux/macOS
            // stamped the OS ACCOUNT name into a document that gets shared. NormalizeUserName
            // still falls back to Environment.UserName when nothing is configured, so the
            // out-of-the-box behaviour is unchanged.
            () => AppOptions.NormalizeUserName(_optionsRuntimeSession.LiveOptions.UserName),
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
