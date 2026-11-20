using FreeX.App.Localization;
using FreeX.App.Presentation.Filtering;

namespace FreeX.App.Host;

// Excel's cell menu Filter ▸ "Filter by Selected Cell's Value".
public partial class MainWindow
{
    internal void FilterBySelectedCellValue()
    {
        if (_workbook.GetSheet(_currentSheetId) is not { } sheet ||
            (SheetGrid.ActiveCell ?? SheetGrid.SelectedRange?.Start) is not { } activeCell)
        {
            return;
        }

        SynchronizeWorkbookSessionSelection();
        if (FilterBySelectedCellValuePlanner.NeedsAutoFilter(sheet))
        {
            if (!TryExecuteWorksheetFilterCommand(_session.EnableAutoFilterAroundActiveCell, "Filter"))
                return;

            _filterWorkflowSession.ResetAutoFilterState();
        }

        if (!FilterBySelectedCellValuePlanner.TryPlan(sheet, activeCell, out var range, out var columnOffset, out var values))
        {
            _messageService.ShowWarning(
                UiText.Get("ShellLoc_FilterFailed"),
                UiText.Get("MainWindowMessage_ClearFilterTitle"));
            return;
        }

        if (TryExecuteAutoFilterMutation(_filterWorkflowSession.PlanAllowedValues(sheet.Id, range, columnOffset, values)))
            UpdateFilterViewportAndStatusBar();
    }
}
