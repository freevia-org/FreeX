namespace FreeX.App.Host;

/// <summary>
/// The outcome of the save-changes confirmation dialog shown before a destructive action.
/// Promoted from a private nested type to a top-level internal type so that
/// <see cref="WindowCloseDecisionPlanner"/> can reference it without being nested inside
/// <c>MainWindow</c>.
/// </summary>
internal enum SaveChangesConfirmation
{
    /// <summary>The user clicked Cancel — abort the action, keep the window open.</summary>
    Cancel,
    /// <summary>
    /// The user clicked Save and the save completed (or the workbook was already clean).
    /// The window may still stay open if new edits arrived mid-save.
    /// </summary>
    Continue,
    /// <summary>The user clicked "Don't Save" — discard changes and proceed.</summary>
    DiscardWithoutSaving
}

/// <summary>
/// Pure decision logic for the post-prompt close step in <c>MainWindow_Closing</c>.
/// Determines whether the window should proceed to close or stay open, given the
/// user's dialog answer and the current dirty state.
/// <para>
/// Extracted for unit-testability: no WPF, no async, no side effects.
/// </para>
/// </summary>
internal static class WindowCloseDecisionPlanner
{
    /// <summary>
    /// Determines the close action after the save-changes prompt has returned.
    /// </summary>
    /// <param name="confirmation">The outcome from the save-changes prompt.</param>
    /// <param name="isDirtyNow">
    ///   The current dirty state of the workbook, read <em>after</em> the async prompt/save returned.
    ///   Only meaningful when <paramref name="confirmation"/> is <see cref="SaveChangesConfirmation.Continue"/>
    ///   (i.e. a save actually ran); discard and cancel paths do not re-check dirtiness.
    /// </param>
    /// <returns>A <see cref="WindowCloseAction"/> the caller should execute.</returns>
    public static WindowCloseAction Decide(SaveChangesConfirmation confirmation, bool isDirtyNow)
        => confirmation switch
        {
            // User clicked Cancel — stay open unconditionally.
            SaveChangesConfirmation.Cancel => WindowCloseAction.StayOpen,

            // User clicked "Don't Save" (Discard) — close unconditionally.
            // The discard path never calls MarkWorkbookSaved, so the dirty flag
            // remains set; the original bug was applying the dirty re-check here.
            SaveChangesConfirmation.DiscardWithoutSaving => WindowCloseAction.Close,

            // User clicked Save (Continue) — a save ran.  If new edits arrived
            // during the async save (rare but possible if input was somehow not
            // blocked), keep the window open so the user is not surprised by data
            // loss.  If the save landed cleanly, proceed to close.
            SaveChangesConfirmation.Continue => isDirtyNow
                ? WindowCloseAction.StayOpen
                : WindowCloseAction.Close,

            _ => WindowCloseAction.StayOpen
        };
}

/// <summary>The action the calling window should take after the close-decision planner returns.</summary>
internal enum WindowCloseAction
{
    /// <summary>Proceed to close the window.</summary>
    Close,
    /// <summary>Keep the window open (cancel the close).</summary>
    StayOpen
}
