using System.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// A composite undoable command that atomically writes a value to a form control's linked cell
/// AND saves/restores the control's in-model state (IsChecked / Value / SelectedIndex).
///
/// This fixes the undo asymmetry where <see cref="EditCellsCommand"/> restores the linked cell
/// but leaves the control's in-model fields (e.g. <see cref="FormControlModel.IsChecked"/>) at
/// the post-Apply value, causing the rendered control to show the wrong state after undo.
///
/// <para>An option-button selection mutates not just the clicked control but every sibling in its
/// group (they get un-checked). <see cref="_priorGroupChecked"/> captures each affected sibling's
/// prior <see cref="FormControlModel.IsChecked"/> so <see cref="Revert"/> restores the WHOLE
/// group's checked state, not just the clicked control's own.</para>
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

    // Sibling option-button group state (null for non-group interactions such as CheckBox/Spinner/
    // ListBox). Keyed by control; value is that sibling's prior/applied IsChecked, snapshotted the
    // same way as _priorIsChecked/_appliedIsChecked above, but for every group member.
    private readonly IReadOnlyDictionary<FormControlModel, bool>? _priorGroupChecked;
    private readonly Dictionary<FormControlModel, bool>? _appliedGroupChecked;

    private bool _applied;

    public string Label { get; }

    /// <summary>Forwards the affected cells from the inner cell-edit command.</summary>
    public IReadOnlyList<CellAddress> AffectedCells => _cellEdit.AffectedCells;

    private FormControlInteractionCommand(
        FormControlModel control,
        EditCellsCommand cellEdit,
        string label,
        IReadOnlyDictionary<FormControlModel, bool>? priorGroupChecked)
    {
        _control  = control;
        _cellEdit = cellEdit;
        Label     = label;
        _priorGroupChecked = priorGroupChecked;
        _appliedGroupChecked = priorGroupChecked is null
            ? null
            : priorGroupChecked.Keys.ToDictionary(c => c, c => c.IsChecked);
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
        var cmd = new FormControlInteractionCommand(control, cellEdit, label, priorGroupChecked: null)
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

    /// <summary>
    /// Creates a <see cref="FormControlInteractionCommand"/> for an option-button group selection:
    /// like <see cref="Wrap"/>, but also snapshots every sibling's prior/applied
    /// <see cref="FormControlModel.IsChecked"/> (<paramref name="priorGroupChecked"/>, keyed by
    /// control, including <paramref name="control"/> itself) so <see cref="Revert"/> restores the
    /// WHOLE group's checked state — not just the clicked button's own.
    ///
    /// Call this AFTER the caller has already mutated every group member's <c>IsChecked</c> to its
    /// new (post-click) state but BEFORE the cell edit has been applied.
    /// </summary>
    public static FormControlInteractionCommand WrapGroupSelection(
        FormControlModel control,
        EditCellsCommand cellEdit,
        string label,
        IReadOnlyDictionary<FormControlModel, bool> priorGroupChecked,
        int? priorValue,
        int? priorSelectedIndex)
    {
        var cmd = new FormControlInteractionCommand(control, cellEdit, label, priorGroupChecked)
        {
            _priorIsChecked      = priorGroupChecked.TryGetValue(control, out var prior) && prior,
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

            if (_appliedGroupChecked is not null)
            {
                foreach (var (sibling, appliedChecked) in _appliedGroupChecked)
                    sibling.IsChecked = appliedChecked;
            }
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

        // Restore every sibling in the option-button group to its prior checked state too — a
        // sibling that was checked before Apply and got cleared during the click must come back,
        // not just the clicked control's own state.
        if (_priorGroupChecked is not null)
        {
            foreach (var (sibling, priorChecked) in _priorGroupChecked)
                sibling.IsChecked = priorChecked;
        }
    }
}
