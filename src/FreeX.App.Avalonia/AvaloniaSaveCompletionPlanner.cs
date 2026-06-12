namespace FreeX.App.Avalonia;

/// <summary>
/// Pure decision logic for the post-save completion step in the Avalonia shell.
/// Determines whether the completed save should mark the workbook saved and
/// whether the file-context metadata (path, name, recent files) should be applied.
/// Extracted for unit-testability without a live Avalonia window.
/// </summary>
public static class AvaloniaSaveCompletionPlanner
{
    /// <summary>
    /// Plans the post-save actions given the generation counter values and workbook identity.
    /// </summary>
    /// <param name="generationAtSaveStart">
    ///   The <c>DirtyGeneration</c> value captured just before the async save awaited.
    /// </param>
    /// <param name="generationNow">
    ///   The current <c>DirtyGeneration</c> value after the await completes.
    /// </param>
    /// <param name="sameWorkbook">
    ///   <c>true</c> when the workbook reference has not been replaced during the save
    ///   (i.e. the user did not open a different file while saving).
    /// </param>
    /// <returns>A <see cref="AvaloniaSaveCompletionPlan"/> describing what the caller should do.</returns>
    public static AvaloniaSaveCompletionPlan Plan(
        int generationAtSaveStart,
        int generationNow,
        bool sameWorkbook)
    {
        // If the workbook was replaced mid-save (open-over scenario), nothing about
        // the current state corresponds to what was saved — skip everything.
        if (!sameWorkbook)
            return new AvaloniaSaveCompletionPlan(MarkSaved: false, ApplyFileContext: false);

        // Edits arrived during the save window (generation advanced) → do NOT clear
        // the dirty flag; do apply file-context (path/name) since the save did land.
        var noEditsArrivedDuringSave = generationNow == generationAtSaveStart;
        return new AvaloniaSaveCompletionPlan(
            MarkSaved: noEditsArrivedDuringSave,
            ApplyFileContext: true);
    }
}

/// <summary>
/// Result of <see cref="AvaloniaSaveCompletionPlanner.Plan"/>.
/// </summary>
/// <param name="MarkSaved">
///   When <c>true</c>, call the session's save method to clear the dirty flag.
///   When <c>false</c>, the workbook has unsaved edits that arrived after the save started.
/// </param>
/// <param name="ApplyFileContext">
///   When <c>true</c>, apply the post-save file context: update the current file path,
///   workbook name, and the recent-files list.
///   When <c>false</c>, skip these mutations (the save result is stale relative to
///   the current workbook state).
/// </param>
public sealed record AvaloniaSaveCompletionPlan(bool MarkSaved, bool ApplyFileContext);
