using FreeX.App.Localization;
using FreeX.App.Presentation.Filtering;

namespace FreeX.App.Avalonia;

// Excel's cell menu Filter ▸ "Filter by Selected Cell's Value".
public partial class MainWindow
{
    internal void FilterBySelectedCellValue()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var sheet = _session.ActiveSheet;
        if (FilterBySelectedCellValuePlanner.NeedsAutoFilter(sheet))
        {
            var result = _session.EnableAutoFilterAroundActiveCell();
            if (!result.Success)
            {
                ShowEditIssue(result.ErrorMessage ?? UiText.Get("MainLoc_ToggleFilterFailed"));
                return;
            }

            _filterWorkflowSession.ResetAutoFilterState();
        }

        if (!FilterBySelectedCellValuePlanner.TryPlan(sheet, _session.ActiveCell, out var range, out var columnOffset, out var values))
        {
            ShowEditIssue(UiText.Get("ShellLoc_FilterFailed"));
            return;
        }

        RunAutoFilter(range, columnOffset, values);
    }
}
