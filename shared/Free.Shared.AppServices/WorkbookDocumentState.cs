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
    public WorkbookDocumentState()
    {
    }

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

    /// <summary>
    /// The undo stack's monotonic <c>UndoRedoStack.Version</c> token at the time the workbook was
    /// last saved via <see cref="MarkSavedAtUndoDepth(int, long)"/>, or <c>null</c> when the save
    /// point was recorded without a version (legacy <see cref="MarkSavedAtUndoDepth(int)"/>
    /// overload, or no save point at all). When present, <see cref="TryMarkCleanIfAtSavePoint(int, long)"/>
    /// uses it as the robust identity check instead of relying on <see cref="SavedUndoDepth"/> alone
    /// — the version can never alias across a depth-cap eviction the way a raw count can.
    /// </summary>
    public long? SavedUndoStackVersion { get; private set; }

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
        SavedUndoStackVersion = null;
    }

    /// <summary>
    /// Marks the workbook clean and records the undo-stack depth at the time of save.
    /// After this call, <c>ExecuteUndo</c> / <c>ExecuteRedo</c> can call
    /// <see cref="TryMarkCleanIfAtSavePoint(int)"/> to restore the clean state when the stack
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
        SavedUndoStackVersion = null;
    }

    /// <summary>
    /// Marks the workbook clean and records both the undo-stack depth and the robust
    /// <c>UndoRedoStack.Version</c> token at the time of save. Prefer this overload over
    /// <see cref="MarkSavedAtUndoDepth(int)"/> whenever the caller has access to
    /// <c>ICommandBus.GetUndoStackVersion</c>: <see cref="TryMarkCleanIfAtSavePoint(int, long)"/>
    /// then uses the version as the save-point identity check, which — unlike a raw depth
    /// count — can never alias after the undo stack has been trimmed by its depth/byte cap.
    /// </summary>
    /// <param name="undoDepthAtSave">
    ///   The value of <c>_commandBus.GetUndoStackDepth(workbookId)</c> at the time the
    ///   save completed.
    /// </param>
    /// <param name="undoStackVersionAtSave">
    ///   The value of <c>_commandBus.GetUndoStackVersion(workbookId)</c> at the time the
    ///   save completed.
    /// </param>
    public void MarkSavedAtUndoDepth(int undoDepthAtSave, long undoStackVersionAtSave)
    {
        IsDirty = false;
        SavedUndoDepth = undoDepthAtSave;
        SavedUndoStackVersion = undoStackVersionAtSave;
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
        SavedUndoStackVersion = null;
    }

    /// <summary>
    /// If the current undo-stack depth matches the saved depth, clears the dirty flag
    /// (the user has undone/redone back to the save point — no unsaved changes).
    /// Returns <c>true</c> when the state transitioned from dirty to clean.
    /// </summary>
    /// <remarks>
    /// Depth alone is not a fully reliable identity check: once the undo stack's depth/byte cap
    /// has evicted entries (see <c>UndoRedoStack.TrimUndoStack</c>), a later depth match no longer
    /// proves the live state equals the saved state. Callers that also have the stack's version
    /// token available should call <see cref="TryMarkCleanIfAtSavePoint(int, long)"/> instead,
    /// which is immune to this aliasing. This depth-only overload is kept for callers/tests that
    /// only track depth; it accepts the residual (now rare — only within a single
    /// non-trimmed session) aliasing risk.
    /// </remarks>
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
    /// Robust variant of <see cref="TryMarkCleanIfAtSavePoint(int)"/>: in addition to the depth
    /// check, requires the undo stack's monotonic <c>UndoRedoStack.Version</c> token to match the
    /// version recorded at the save point (when one was recorded via
    /// <see cref="MarkSavedAtUndoDepth(int, long)"/>). The version can never alias across a
    /// depth-cap eviction and trim/refill, so this eliminates the false-clean scenario where the
    /// stack's raw depth returns to the saved value after the save-point entries were evicted and
    /// replaced with different ones.
    /// <para>
    /// If no version was recorded at the save point (the save point came from the legacy
    /// <see cref="MarkSavedAtUndoDepth(int)"/> overload, or none was recorded at all), this falls
    /// back to the depth-only check.
    /// </para>
    /// </summary>
    /// <param name="currentUndoDepth">
    ///   The value of <c>_commandBus.GetUndoStackDepth(workbookId)</c> right now.
    /// </param>
    /// <param name="currentUndoStackVersion">
    ///   The value of <c>_commandBus.GetUndoStackVersion(workbookId)</c> right now.
    /// </param>
    public bool TryMarkCleanIfAtSavePoint(int currentUndoDepth, long currentUndoStackVersion)
    {
        if (SavedUndoDepth < 0 || currentUndoDepth != SavedUndoDepth)
            return false;

        if (SavedUndoStackVersion is { } savedVersion && currentUndoStackVersion != savedVersion)
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
