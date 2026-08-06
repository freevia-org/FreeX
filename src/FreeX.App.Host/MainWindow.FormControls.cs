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

        var command = FormControlInteractionService.CreateCommand(
            new FormControlInteractionRequest(e.Control, e.Gesture, e.ListItemIndex),
            sheet.FormControls,
            _currentSheetId,
            _workbook);

        if (command is null)
        {
            // No linked-cell write, but in-model state already mutated above; just refresh.
            UpdateViewport();
            return;
        }

        // Execute through the normal command bus: undoable and recalc-aware. A rejection here (e.g.
        // the linked cell landed on a legacy array member) never wrote the cell, but
        // TryExecuteCommand fired before this point already showed the click's visual feedback via
        // GridView's InvalidateVisual on the click event — so a rejected write must still resync the
        // control's visible state from the (unchanged) cell via UpdateViewport before returning,
        // mirroring the Avalonia shell's RefreshShell(...) on its own failure branch.
        if (!TryExecuteCommand(command, "Form Control"))
        {
            UpdateViewport();
            return;
        }

        UpdateViewport();
    }
}
