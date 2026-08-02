using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Avalonia;

public partial class MainWindow
{
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

        var commands = _session.GetCurrentGroupedEditSheetIds()
            .Select(sheetId => PageLayoutRibbonCommandPlanner.BuildScaleToFitCommand(sheetId, plan.ScaleToFit))
            .ToArray();
        var command = commands.Length == 1
            ? commands[0]
            : new CompositeWorkbookCommand(PageLayoutRibbonActionPlanner.ScaleToFitCommandLabel, commands);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Scale to fit failed.");
            _refreshRibbonToggleStates?.Invoke();
            return;
        }

        RefreshShell(_statusText.Text ?? "Ready");
    }
}
