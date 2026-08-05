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

public enum DialogRangeSelectionFormat
{
    Range,
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

    private DialogRangeSelectionState<TContext>? TakeActive()
    {
        var state = Active;
        Active = null;
        return state;
    }
}
