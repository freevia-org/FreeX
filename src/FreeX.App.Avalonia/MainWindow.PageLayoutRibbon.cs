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
            return;

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
            return;
        }

        RefreshShell(_statusText.Text ?? "Ready");
    }
}
