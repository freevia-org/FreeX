namespace FreeX.App.Avalonia;

/// <summary>
/// The outcome of the save-changes confirmation dialog shown before a destructive close
/// in the Avalonia shell.
/// </summary>
public enum AvaloniaCloseConfirmation
{
    /// <summary>The user clicked Cancel — abort the close, keep the window open.</summary>
    Cancel,

    /// <summary>
    /// The user clicked Save and the save completed (or the workbook was already clean).
    /// The window may still stay open if new edits arrived mid-save.
    /// </summary>
    Continue,

    /// <summary>The user clicked "Discard" — discard changes and proceed to close.</summary>
    Discard
}

/// <summary>The action the Avalonia window should take after the close-decision planner returns.</summary>
public enum AvaloniaCloseAction
{
    /// <summary>Proceed to close the window.</summary>
    Close,

    /// <summary>Keep the window open (cancel the close).</summary>
    StayOpen
}

/// <summary>
/// Pure decision logic for the post-prompt close step in the Avalonia shell.
/// Determines whether the window should proceed to close or stay open, given the
/// user's dialog answer and the current dirty state.
/// <para>
/// Extracted for unit-testability: no Avalonia, no async, no side effects.
/// </para>
/// </summary>
public static class AvaloniaCloseDecisionPlanner
{
    /// <summary>
    /// Determines the close action after the save-changes prompt has returned.
    /// </summary>
    /// <param name="confirmation">The outcome from the save-changes prompt.</param>
    /// <param name="isDirtyNow">
    ///   The current dirty state of the workbook, read <em>after</em> the async prompt/save returned.
    ///   Only meaningful when <paramref name="confirmation"/> is <see cref="AvaloniaCloseConfirmation.Continue"/>
    ///   (i.e. a save actually ran); discard and cancel paths do not re-check dirtiness.
    /// </param>
    /// <returns>A <see cref="AvaloniaCloseAction"/> the caller should execute.</returns>
    public static AvaloniaCloseAction Decide(AvaloniaCloseConfirmation confirmation, bool isDirtyNow)
        => confirmation switch
        {
            // User clicked Cancel — stay open unconditionally.
            AvaloniaCloseConfirmation.Cancel => AvaloniaCloseAction.StayOpen,

            // User clicked "Discard" — close unconditionally.
            // The discard path never calls MarkSaved, so the dirty flag remains set;
            // we must NOT re-check dirty here — that is the class of bug fixed in the WPF host.
            AvaloniaCloseConfirmation.Discard => AvaloniaCloseAction.Close,

            // User clicked Save (Continue) — a save ran.  If new edits arrived
            // during the async save, keep the window open so the user is not surprised
            // by data loss.  If the save landed cleanly, proceed to close.
            AvaloniaCloseConfirmation.Continue => isDirtyNow
                ? AvaloniaCloseAction.StayOpen
                : AvaloniaCloseAction.Close,

            _ => AvaloniaCloseAction.StayOpen
        };
}
