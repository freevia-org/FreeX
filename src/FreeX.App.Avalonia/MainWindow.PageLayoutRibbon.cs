using FreeX.Core.Model;
using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Avalonia;

public partial class MainWindow
{
    private PageLayoutCommandSession CreatePageLayoutCommandSession() =>
        new(_session.GetCurrentGroupedEditSheetIds());

    private bool ExecutePageLayoutCommandWithShellRefresh(PageLayoutCommandExecutionPlan plan)
    {
        var result = _session.ExecuteReviewCommand(plan.Command);
        RefreshShell(PageLayoutStatusPlanner.ResolveCommandStatus(
            plan,
            result.Success,
            result.ErrorMessage,
            UiText.Get));
        return result.Success;
    }

    private void TogglePrintGridlines()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        ExecutePageLayoutCommandWithShellRefresh(
            CreatePageLayoutCommandSession().PlanPrintGridlines(
                !_session.ActiveSheet.PrintGridlines,
                sheetId => _session.Workbook.GetSheet(sheetId)?.PrintHeadings ?? false));
    }

    private void TogglePrintHeadings()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        ExecutePageLayoutCommandWithShellRefresh(
            CreatePageLayoutCommandSession().PlanPrintHeadings(
                sheetId => _session.Workbook.GetSheet(sheetId)?.PrintGridlines ?? false,
                !_session.ActiveSheet.PrintHeadings));
    }

    private void ApplyPageLayoutScaleWidth(string? text) =>
        ApplyPageLayoutScale(PageLayoutScaleField.Width, text);

    private void ApplyPageLayoutScaleHeight(string? text) =>
        ApplyPageLayoutScale(PageLayoutScaleField.Height, text);

    private void ApplyPageLayoutScalePercent(string? text) =>
        ApplyPageLayoutScale(PageLayoutScaleField.Percent, text);

    private void ApplyPageLayoutScale(PageLayoutScaleField field, string? text)
    {
        var session = CreatePageLayoutCommandSession();
        ApplyPageLayoutScaleCommit(
            session,
            session.PlanScaleCommit(
                field,
                _session.ActiveSheet.ScaleToFit,
                text ?? string.Empty));
    }

    private void ApplyPageLayoutScaleCommit(
        PageLayoutCommandSession session,
        PageLayoutScaleCommitPlan plan)
    {
        if (!plan.ShouldApply)
        {
            // WPF restores the last valid value when arbitrary text cannot be parsed. The live
            // stateful scale commands feed the same value back into the editable combo controls.
            _refreshRibbonToggleStates?.Invoke();
            return;
        }

        var commandPlan = session.PlanScaleToFit(
            plan.ScaleToFit,
            _statusText.Text ?? UiText.Get("MainLoc_Ready"));
        var result = _session.ExecuteReviewCommand(commandPlan.Command);
        var status = PageLayoutStatusPlanner.ResolveCommandStatus(
            commandPlan,
            result.Success,
            result.ErrorMessage,
            UiText.Get);
        if (!result.Success)
        {
            ShowEditIssue(status);
            _refreshRibbonToggleStates?.Invoke();
            return;
        }

        RefreshShell(status);
    }
}
