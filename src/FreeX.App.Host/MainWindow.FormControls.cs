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
                AdvanceDropDownSelection(e.Control),

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

    /// <summary>
    /// For DropDown controls without a full popup: advances SelectedIndex by 1, wrapping around.
    /// A proper popup picker is deferred; this keeps the linked cell cycling through list items
    /// so the interaction is visible.
    /// </summary>
    private IWorkbookCommand? AdvanceDropDownSelection(FormControlModel control)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var itemCount = EstimateListItemCount(control, sheet);
        if (itemCount <= 0)
            itemCount = 1;

        var current = control.SelectedIndex ?? 0;
        var next = current >= itemCount ? 1 : current + 1;

        return FormControlInteractionService.CreateSelectListItemCommand(
            control, next, _currentSheetId, _workbook);
    }

    private int EstimateListItemCount(FormControlModel control, Sheet? sheet)
    {
        if (sheet is null || string.IsNullOrWhiteSpace(control.ListFillRange))
            return 0;

        // Quick estimation: parse the range and count cells.
        // Reuse the resolver's TryResolveLinkedCell pattern.
        var raw = control.ListFillRange.Trim().TrimStart('=').Trim();
        var bangIdx = raw.IndexOf('!');
        string cellPart;
        Sheet? sourceSheet;

        if (bangIdx >= 0)
        {
            var sheetPart = raw[..bangIdx].Trim().Trim('\'');
            cellPart = raw[(bangIdx + 1)..].Trim().Replace("$", string.Empty, System.StringComparison.Ordinal);
            sourceSheet = _workbook.GetSheet(sheetPart) ?? sheet;
        }
        else
        {
            cellPart = raw.Replace("$", string.Empty, System.StringComparison.Ordinal);
            sourceSheet = sheet;
        }

        var colon = cellPart.IndexOf(':');
        if (colon < 0)
            return 1;

        var startStr = cellPart[..colon];
        var endStr = cellPart[(colon + 1)..];

        if (!CellAddress.TryParse(startStr, sourceSheet.Id, out var start) ||
            !CellAddress.TryParse(endStr, sourceSheet.Id, out var end))
            return 0;

        // Excel populates list-style controls from the FIRST COLUMN of ListFillRange only, so the
        // item count is the row count regardless of how many columns the range spans.
        var rows = Math.Max(end.Row, start.Row) - Math.Min(end.Row, start.Row) + 1;
        return (int)rows;
    }
}
