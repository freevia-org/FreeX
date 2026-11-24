using FreeX.App.Presentation.Comments;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private PresentationReviewSessionController ReviewSessionController => CreateReviewSessionController();

    private PresentationReviewSessionController CreateReviewSessionController(
        CellAddress? targetAddress = null,
        bool preserveSelection = false,
        bool repeatable = false) =>
        new(new PresentationReviewSessionAdapter(
            () => _session.Workbook,
            () => _session.ActiveSheet.Id,
            () => targetAddress is { } address
                ? address.Sheet == _session.ActiveSheet.Id ? new GridRange(address, address) : null
                : _session.SelectedRange,
            // r176: honour the configured author name, as the WPF shell has always done.
            // Hardcoding Environment.UserName here meant Options > User name silently did
            // nothing for comments in this shell, and every comment inserted on Linux/macOS
            // stamped the OS ACCOUNT name into a document that gets shared. NormalizeUserName
            // still falls back to Environment.UserName when nothing is configured, so the
            // out-of-the-box behaviour is unchanged.
            () => AppOptions.NormalizeUserName(_optionsRuntimeSession.LiveOptions.UserName),
            (plan, fallbackRange) =>
            {
                // An inline editor retains its original cell even if selection changes while open.
                // Refuse a stale editor after a sheet switch rather than applying its text elsewhere.
                if (targetAddress is { } target &&
                    (_isOpening || _isSaving || target.Sheet != _session.ActiveSheet.Id))
                {
                    return new PresentationCommentMutationExecutionResult(false,
                        UiText.Get("MainLoc_ReviewTargetNotSelected"));
                }

                var result = repeatable
                    ? ExecuteRepeatableReviewCommand(plan, fallbackRange)
                    : preserveSelection
                        ? _session.ExecuteCommandPreservingSelection(plan.CreateCommand(fallbackRange))
                        : _session.ExecuteReviewCommand(plan.CreateCommand(fallbackRange));
                return new PresentationCommentMutationExecutionResult(
                    result.Success,
                    result.ErrorMessage);
            },
            _session.SelectCell));

    // The context menu belongs to the clicked cell, even inside a preserved multi-cell selection.
    // Repeat Last follows the then-current selection, while the first execution uses that target.
    private WorkbookCellEditResult ExecuteRepeatableReviewCommand(
        PresentationCommentMutationPlan plan,
        GridRange fallbackRange)
    {
        var initialExecution = true;
        return _session.ExecuteRepeatableCommandPreservingSelection(() =>
        {
            var start = initialExecution ? fallbackRange.Start : _session.SelectedRange.Start;
            initialExecution = false;
            return plan.CreateCommand(new GridRange(start, start));
        });
    }

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
