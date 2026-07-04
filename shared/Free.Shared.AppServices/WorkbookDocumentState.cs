namespace Free.Shared.AppServices;

/// <summary>
/// Owns the save/dirty cluster for one open document: dirty flag, dirty-generation counter,
/// current file path, and the suppress-close-prompt flag.
/// <para>
/// One instance per document context: each independently opened/created workbook window has its
/// own instance, while the several views of one document created via "New Window" share the
/// originating window's instance (dirty/clean is a document property, not a per-view property).
/// </para>
/// <para>
/// Pure logic; no WPF references. All state transitions are synchronous and must be called on
/// the UI thread (the same constraint that applied when the fields lived in MainWindow).
/// </para>
/// </summary>
public sealed class WorkbookDocumentState
{
    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>True when the workbook has unsaved changes.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// Monotonically-increasing counter, incremented with every <see cref="MarkDirty"/> call.
    /// The async save path captures this before awaiting and compares afterwards to detect
    /// edits that arrived mid-save (see <see cref="SaveCompletionPlanner"/>).
    /// </summary>
    public int DirtyGeneration { get; private set; }

    /// <summary>
    /// The undo-stack depth at the time the workbook was last saved (or opened/created clean).
    /// Used by <c>ExecuteUndo</c> / <c>ExecuteRedo</c> to detect when the stack returns
    /// to the save point and clear the dirty flag without requiring an explicit save.
    /// <para>
    /// A value of <c>-1</c> means "no save point recorded" — the workbook was never saved
    /// or was saved while no undo history existed and the depth is unknown.  Callers that
    /// compare depth against this value must treat <c>-1</c> as "never at save point".
    /// </para>
    /// </summary>
    public int SavedUndoDepth { get; private set; } = -1;

    /// <summary>The full path of the file most recently saved to or opened from, or <c>null</c> for an unsaved workbook.</summary>
    public string? CurrentFilePath { get; private set; }

    /// <summary>
    /// When <c>true</c> the close-confirmation dialog is suppressed for the next close attempt.
    /// Set to <c>true</c> immediately before a programmatic <c>Close()</c> call that follows a
    /// completed save, and cleared on the next <c>MarkDirty</c> call.
    /// </summary>
    public bool SuppressClosePrompt { get; set; }

    // ── Transitions ──────────────────────────────────────────────────────────

    /// <summary>
    /// Marks the workbook dirty and increments the generation counter.
    /// Automatically clears <see cref="SuppressClosePrompt"/> so a re-dirtied workbook
    /// correctly prompts again on the next close attempt.
    /// </summary>
    public void MarkDirty()
    {
        IsDirty = true;
        DirtyGeneration++;
        SuppressClosePrompt = false;
    }

    /// <summary>
    /// Unconditionally marks the workbook clean (dirty flag cleared).
    /// Used after a new workbook is created or after a file is opened
    /// (where the workbook is considered saved at its loaded state).
    /// Does not update <see cref="CurrentFilePath"/>; callers that also want to record the
    /// saved path should use <see cref="MarkSavedWithPath"/> instead.
    /// </summary>
    public void MarkSaved()
    {
        IsDirty = false;
        SavedUndoDepth = -1;
    }

    /// <summary>
    /// Marks the workbook clean and records the undo-stack depth at the time of save.
    /// After this call, <c>ExecuteUndo</c> / <c>ExecuteRedo</c> can call
    /// <see cref="TryMarkCleanIfAtSavePoint"/> to restore the clean state when the stack
    /// returns to this depth.
    /// </summary>
    /// <param name="undoDepthAtSave">
    ///   The value of <c>_commandBus.GetUndoStackDepth(workbookId)</c> at the time the
    ///   save completed.
    /// </param>
    public void MarkSavedAtUndoDepth(int undoDepthAtSave)
    {
        IsDirty = false;
        SavedUndoDepth = undoDepthAtSave;
    }

    /// <summary>
    /// Marks the workbook clean and records the file path it was saved to.
    /// Use after a successful Save or Save As operation.
    /// </summary>
    /// <param name="path">The file path the workbook was written to.</param>
    public void MarkSavedWithPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        IsDirty = false;
        CurrentFilePath = path;
        SavedUndoDepth = -1;
    }

    /// <summary>
    /// If the current undo-stack depth matches the saved depth, clears the dirty flag
    /// (the user has undone/redone back to the save point — no unsaved changes).
    /// Returns <c>true</c> when the state transitioned from dirty to clean.
    /// </summary>
    /// <param name="currentUndoDepth">
    ///   The value of <c>_commandBus.GetUndoStackDepth(workbookId)</c> right now.
    /// </param>
    public bool TryMarkCleanIfAtSavePoint(int currentUndoDepth)
    {
        if (SavedUndoDepth < 0 || currentUndoDepth != SavedUndoDepth)
            return false;

        IsDirty = false;
        return true;
    }

    /// <summary>
    /// Sets <see cref="CurrentFilePath"/> without changing the dirty flag.
    /// Used after recovery load, where the path is known but the workbook is considered dirty.
    /// </summary>
    /// <param name="path">The original file path to associate, or <c>null</c> to clear the association.</param>
    public void SetCurrentFilePath(string? path)
    {
        CurrentFilePath = path;
    }

    /// <summary>
    /// Clears the current file path. Used when creating a new workbook that has no file association.
    /// </summary>
    public void ClearCurrentFilePath()
    {
        CurrentFilePath = null;
    }
}
