using FreeX.Core.Model;
using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Avalonia;

public partial class MainWindow
{
    private PageLayoutCommandSession CreatePageLayoutCommandSession() =>
        new(_session.GetCurrentGroupedEditSheetIds());

    private void ApplyPageLayoutScaleWidth(string? text) =>
        ApplyPageLayoutScaleCommit(
            PageLayoutRibbonPolicyPlanner.PlanScaleWidthCommit(
                _session.ActiveSheet.ScaleToFit,
                text ?? string.Empty));

    private void ApplyPageLayoutScaleHeight(string? text) =>
        ApplyPageLayoutScaleCommit(
            PageLayoutRibbonPolicyPlanner.PlanScaleHeightCommit(
                _session.ActiveSheet.ScaleToFit,
                text ?? string.Empty));

    private void ApplyPageLayoutScalePercent(string? text) =>
        ApplyPageLayoutScaleCommit(
            PageLayoutRibbonPolicyPlanner.PlanScalePercentCommit(
                _session.ActiveSheet.ScaleToFit,
                text ?? string.Empty));

    private void ApplyPageLayoutScaleCommit(PageLayoutScaleCommitPlan plan)
    {
        if (!plan.ShouldApply)
        {
            // WPF restores the last valid value when arbitrary text cannot be parsed. The live
            // stateful scale commands feed the same value back into the editable combo controls.
            _refreshRibbonToggleStates?.Invoke();
            return;
        }

        var commandPlan = CreatePageLayoutCommandSession().PlanScaleToFit(plan.ScaleToFit);
        var result = _session.ExecuteReviewCommand(commandPlan.Command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Scale to fit failed.");
            _refreshRibbonToggleStates?.Invoke();
            return;
        }

        RefreshShell(_statusText.Text ?? "Ready");
    }
}
