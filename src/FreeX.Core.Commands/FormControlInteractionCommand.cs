using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// A composite undoable command that atomically writes a value to a form control's linked cell
/// AND saves/restores the control's in-model state (IsChecked / Value / SelectedIndex).
///
/// This fixes the undo asymmetry where <see cref="EditCellsCommand"/> restores the linked cell
/// but leaves the control's in-model fields (e.g. <see cref="FormControlModel.IsChecked"/>) at
/// the post-Apply value, causing the rendered control to show the wrong state after undo.
/// </summary>
public sealed class FormControlInteractionCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private readonly FormControlModel _control;
    private readonly EditCellsCommand _cellEdit;

    // Captured prior in-model state (snapshotted before Apply).
    private bool _priorIsChecked;
    private int? _priorValue;
    private int? _priorSelectedIndex;

    // Post-Apply in-model state (set during Apply so Revert can re-apply it if needed).
    private bool _appliedIsChecked;
    private int? _appliedValue;
    private int? _appliedSelectedIndex;

    private bool _applied;

    public string Label { get; }

    /// <summary>Forwards the affected cells from the inner cell-edit command.</summary>
    public IReadOnlyList<CellAddress> AffectedCells => _cellEdit.AffectedCells;

    private FormControlInteractionCommand(
        FormControlModel control,
        EditCellsCommand cellEdit,
        string label)
    {
        _control  = control;
        _cellEdit = cellEdit;
        Label     = label;
    }

    /// <summary>
    /// Creates a <see cref="FormControlInteractionCommand"/> that wraps <paramref name="cellEdit"/>
    /// and, on <see cref="Revert"/>, also restores the control's prior in-model state.
    ///
    /// Call this AFTER the caller has already mutated <paramref name="control"/> to its new state
    /// (e.g. flipped IsChecked) but BEFORE the cell edit has been applied.  The method snapshots
    /// the CURRENT (post-mutation) control state as the "applied" state, and the PRIOR state
    /// must be supplied by the caller as <paramref name="priorIsChecked"/>,
    /// <paramref name="priorValue"/>, and <paramref name="priorSelectedIndex"/>.
    /// </summary>
    public static FormControlInteractionCommand Wrap(
        FormControlModel control,
        EditCellsCommand cellEdit,
        string label,
        bool priorIsChecked,
        int? priorValue,
        int? priorSelectedIndex)
    {
        var cmd = new FormControlInteractionCommand(control, cellEdit, label)
        {
            _priorIsChecked      = priorIsChecked,
            _priorValue          = priorValue,
            _priorSelectedIndex  = priorSelectedIndex,
            _appliedIsChecked    = control.IsChecked,
            _appliedValue        = control.Value,
            _appliedSelectedIndex = control.SelectedIndex,
        };
        return cmd;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        // Re-apply in-model state (needed when re-doing after undo).
        if (_applied)
        {
            _control.IsChecked     = _appliedIsChecked;
            _control.Value         = _appliedValue;
            _control.SelectedIndex = _appliedSelectedIndex;
        }

        _applied = true;
        return _cellEdit.Apply(ctx);
    }

    public void Revert(ICommandContext ctx)
    {
        _cellEdit.Revert(ctx);

        // Restore the control's in-model state to what it was before Apply.
        _control.IsChecked     = _priorIsChecked;
        _control.Value         = _priorValue;
        _control.SelectedIndex = _priorSelectedIndex;
    }
}
