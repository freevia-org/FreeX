using System.Windows;
using FreeX.App.UI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>
/// WPF host wiring for legacy form-control interactivity.
/// The GridView fires <see cref="GridView.FormControlClicked"/> when the user left-clicks a form
/// control; this partial class handles that event by:
/// <list type="number">
///   <item>Routing to <see cref="FormControlInteractionService"/> to compute the command.</item>
///   <item>Executing the resulting <see cref="EditCellsCommand"/> through the normal command bus
///       (so the linked cell update is undoable and triggers recalc).</item>
///   <item>Refreshing the viewport so the state change is visible immediately.</item>
/// </list>
/// </summary>
public partial class MainWindow
{
    private void WireFormControlEvents()
    {
        SheetGrid.FormControlClicked += OnFormControlClicked;
    }

    private void OnFormControlClicked(object? sender, FormControlClickEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        IWorkbookCommand? command = e.Control.Kind switch
        {
            FormControlKind.CheckBox =>
                FormControlInteractionService.CreateToggleCheckBoxCommand(
                    e.Control, _currentSheetId, _workbook),

            FormControlKind.OptionButton =>
                FormControlInteractionService.CreateSelectOptionButtonCommand(
                    e.Control, sheet.FormControls, _currentSheetId, _workbook),

            FormControlKind.Spinner =>
                FormControlInteractionService.CreateStepCommand(
                    e.Control,
                    e.Region == FormControlClickRegion.StepUp ? +1 : -1,
                    _currentSheetId, _workbook),

            FormControlKind.ScrollBar =>
                FormControlInteractionService.CreateStepCommand(
                    e.Control,
                    e.Region == FormControlClickRegion.StepUp ? -1 : +1,
                    _currentSheetId, _workbook),

            FormControlKind.ListBox =>
                e.ListItemIndex > 0
                    ? FormControlInteractionService.CreateSelectListItemCommand(
                        e.Control, e.ListItemIndex, _currentSheetId, _workbook)
                    : null,

            FormControlKind.DropDown =>
                // Dropdown opens a picker — for now advance to next item on each click
                // (full picker popup is deferred; this keeps the linked cell updating)
                FormControlInteractionService.CreateAdvanceListSelectionCommand(
                    e.Control, _currentSheetId, _workbook),

            FormControlKind.Button =>
                // Push-button runs an assigned macro — FreeX has no macro engine,
                // so just give visual press feedback (IsChecked briefly true then restored).
                null,

            _ => null,
        };

        if (command is null)
        {
            // No linked-cell write, but in-model state already mutated above — just refresh.
            UpdateViewport();
            return;
        }

        // Execute through the normal command bus: undoable + triggers recalc via the
        // post-execution callback that calls RecalculateAfterCommandOutcome.
        if (!TryExecuteCommand(command, "Form Control"))
            return;

        var affected = (command as IAffectedCellsCommand)?.AffectedCells;
        if (affected is { Count: > 0 })
            RecalculateIfAutomatic(affected);

        UpdateViewport();
    }

}
