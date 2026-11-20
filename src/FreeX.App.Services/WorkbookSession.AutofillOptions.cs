using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed partial class WorkbookSession
{
    private AutofillOptionsState? _lastAutofill;
    private long _lastAutofillUndoVersion = -1;

    /// <summary>
    /// The fill the Auto Fill Options button belongs to, or null once anything else has been done
    /// (the fill is no longer the newest undo entry).
    /// </summary>
    public AutofillOptionsState? AutofillOptions =>
        _lastAutofill is { } state && _lastAutofillUndoVersion == UndoStackVersion ? state : null;

    /// <summary>
    /// Remembers a fill-handle drag that a host just ran itself. Dragging back inside the source only
    /// clears cells, so it offers no options, as in Excel.
    /// </summary>
    public void NoteAutofill(GridRange source, GridRange fill, bool ctrlHeld)
    {
        if (source.Contains(fill))
        {
            _lastAutofill = null;
            return;
        }

        _lastAutofill = new AutofillOptionsState(source, fill, ctrlHeld, AutofillMode.Default);
        _lastAutofillUndoVersion = UndoStackVersion;
    }

    /// <summary>Undoes the last fill and redoes the same drag as the chosen Auto Fill Option.</summary>
    public WorkbookCellEditResult ApplyAutofillOption(AutofillMode mode)
    {
        if (AutofillOptions is not { } state)
            return SuccessfulNoOpEditResult();

        var undo = UndoLastEdit();
        if (!undo.Success)
            return undo;

        var command = CreateGroupedSheetCommand(
            "Autofill",
            sheetId => new AutofillCommand(
                sheetId,
                RemapRangeToSheet(state.Source, sheetId),
                RemapRangeToSheet(state.Fill, sheetId))
            { Mode = mode });
        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(
            result,
            FreeX.App.Presentation.GridInteraction.GridAutofillPlanner.CalculateCompletedSelectionRange(state.Source, state.Fill));
        _lastAutofill = state with { Mode = mode, CtrlHeld = false };
        _lastAutofillUndoVersion = UndoStackVersion;
        return result;
    }
}
