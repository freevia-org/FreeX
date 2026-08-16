using FreeX.Core.Model;

namespace FreeX.App.Presentation.Dialogs;

public enum DialogRangeSelectionKey
{
    Other,
    Enter,
    Escape,
}

public readonly record struct DialogRangeSelectionKeyDecision(
    bool Handled,
    bool ApplySelection)
{
    public static DialogRangeSelectionKeyDecision Ignore { get; } = new(false, false);

    public static DialogRangeSelectionKeyDecision Apply { get; } = new(true, true);

    public static DialogRangeSelectionKeyDecision Cancel { get; } = new(true, false);
}

public readonly record struct DialogRangeSelectionKeyTransition<TContext>(
    bool Handled,
    DialogRangeSelectionTransition<TContext>? Transition);

public enum DialogRangeSelectionFormat
{
    Range,

    /// <summary>
    /// Like <see cref="Range"/> but written with absolute markers ($B$2:$C$3). Excel's
    /// "Allow Users to Edit Ranges" refers-to-cells box stores an absolute reference, so the range
    /// keeps pointing at the same cells when rows or columns move around it.
    /// </summary>
    AbsoluteRange,
    StartCell,
    DataValidationFormula,
    PageSetupPrintArea,
    PageSetupRepeatRows,
    PageSetupRepeatColumns,
}

public sealed record DialogRangeSelectionState<TContext>(
    TContext Context,
    string OriginalText,
    DialogRangeSelectionFormat Format,
    bool CollapseDialog,
    bool OwnerWasEnabled);

public sealed record DialogRangeSelectionTransition<TContext>(
    DialogRangeSelectionState<TContext> State,
    bool RestoreDialog,
    bool RestoreOriginalText,
    bool ApplySelection,
    GridRange? SelectedRange);

/// <summary>
/// Owns the renderer-neutral lifecycle of worksheet pointing from a dialog. Native event wiring,
/// dialog presentation, focus, and geometry remain responsibilities of the renderer context.
/// </summary>
public sealed class DialogRangeSelectionController<TContext>
{
    public DialogRangeSelectionState<TContext>? Active { get; private set; }

    public bool IsActive => Active is not null;

    public DialogRangeSelectionState<TContext> Begin(
        TContext context,
        string? originalText,
        DialogRangeSelectionFormat format,
        bool collapseDialog,
        bool ownerWasEnabled,
        Action<DialogRangeSelectionTransition<TContext>> finishPrevious)
    {
        ArgumentNullException.ThrowIfNull(finishPrevious);

        if (Cancel(restoreDialog: true, restoreOriginalText: true) is { } previous)
            finishPrevious(previous);

        var state = new DialogRangeSelectionState<TContext>(
            context,
            originalText ?? string.Empty,
            format,
            collapseDialog,
            ownerWasEnabled);
        Active = state;
        return state;
    }

    public DialogRangeSelectionKeyDecision DecideKey(DialogRangeSelectionKey key)
    {
        if (!IsActive)
            return DialogRangeSelectionKeyDecision.Ignore;

        return key switch
        {
            DialogRangeSelectionKey.Enter => DialogRangeSelectionKeyDecision.Apply,
            DialogRangeSelectionKey.Escape => DialogRangeSelectionKeyDecision.Cancel,
            _ => DialogRangeSelectionKeyDecision.Ignore,
        };
    }

    public DialogRangeSelectionKeyTransition<TContext> HandleKey(
        DialogRangeSelectionKey key,
        GridRange? selectedRange)
    {
        var decision = DecideKey(key);
        if (!decision.Handled)
            return new DialogRangeSelectionKeyTransition<TContext>(false, null);

        return new DialogRangeSelectionKeyTransition<TContext>(
            true,
            Complete(selectedRange, decision.ApplySelection));
    }

    public DialogRangeSelectionTransition<TContext>? Complete(
        GridRange? selectedRange,
        bool applySelection)
    {
        var state = TakeActive();
        if (state is null)
            return null;

        return new DialogRangeSelectionTransition<TContext>(
            state,
            RestoreDialog: true,
            RestoreOriginalText: !applySelection,
            ApplySelection: applySelection && selectedRange is not null,
            SelectedRange: selectedRange);
    }

    public DialogRangeSelectionTransition<TContext>? Cancel(
        bool restoreDialog,
        bool restoreOriginalText)
    {
        var state = TakeActive();
        return state is null
            ? null
            : new DialogRangeSelectionTransition<TContext>(
                state,
                restoreDialog,
                restoreOriginalText,
                ApplySelection: false,
                SelectedRange: null);
    }

    public void FinishTransition(
        DialogRangeSelectionTransition<TContext> transition,
        Action<TContext> detach,
        Action<DialogRangeSelectionState<TContext>, GridRange> applySelection,
        Action<DialogRangeSelectionState<TContext>>? restoreOriginalText,
        Action<DialogRangeSelectionState<TContext>> restoreDialog)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(detach);
        ArgumentNullException.ThrowIfNull(applySelection);
        ArgumentNullException.ThrowIfNull(restoreDialog);

        var state = transition.State;
        detach(state.Context);
        try
        {
            if (transition.ApplySelection && transition.SelectedRange is { } selectedRange)
                applySelection(state, selectedRange);
            else if (transition.RestoreOriginalText)
                restoreOriginalText?.Invoke(state);
        }
        finally
        {
            if (transition.RestoreDialog)
                restoreDialog(state);
        }
    }

    private DialogRangeSelectionState<TContext>? TakeActive()
    {
        var state = Active;
        Active = null;
        return state;
    }
}
