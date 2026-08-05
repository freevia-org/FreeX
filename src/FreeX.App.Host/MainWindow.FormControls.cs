using System.Windows;
using FreeX.App.UI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>WPF host wiring for legacy form-control interactivity.</summary>
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

        var gesture = e.Region switch
        {
            FormControlClickRegion.StepUp => FormControlGesture.StepUp,
            FormControlClickRegion.StepDown => FormControlGesture.StepDown,
            _ => FormControlGesture.Body,
        };
        var command = FormControlInteractionService.CreateCommand(
            new FormControlInteractionRequest(e.Control, gesture, e.ListItemIndex),
            sheet.FormControls,
            _currentSheetId,
            _workbook);

        if (command is null)
        {
            // No linked-cell write, but in-model state already mutated above; just refresh.
            UpdateViewport();
            return;
        }

        // Execute through the normal command bus: undoable and recalc-aware.
        if (!TryExecuteCommand(command, "Form Control"))
            return;

        UpdateViewport();
    }
}
