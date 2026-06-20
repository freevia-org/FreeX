namespace FreeX.App.Services;

/// <summary>
/// Pure decision logic for the post-save completion step, shared by every shell.
/// Determines whether the completed save should mark the workbook saved and
/// whether the file-context metadata (path, name, recent files) should be applied.
/// Extracted for unit-testability without a live window.
/// </summary>
public static class SaveCompletionPlanner
{
    /// <summary>
    /// Plans the post-save actions given the generation counter values and workbook identity.
    /// </summary>
    /// <param name="generationAtSaveStart">
    ///   The workbook dirty-generation value captured just before the async save awaited.
    /// </param>
    /// <param name="generationNow">
    ///   The current workbook dirty-generation value after the await completes.
    /// </param>
    /// <param name="sameWorkbook">
    ///   <c>true</c> when the workbook reference has not been replaced during the save
    ///   (i.e. the user did not open a different file while saving).
    /// </param>
    /// <returns>A <see cref="SaveCompletionPlan"/> describing what the caller should do.</returns>
    public static SaveCompletionPlan Plan(
        int generationAtSaveStart,
        int generationNow,
        bool sameWorkbook)
    {
        // If the workbook was replaced mid-save (open-over scenario), nothing about
        // the current state corresponds to what was saved — skip everything.
        if (!sameWorkbook)
            return new SaveCompletionPlan(MarkSaved: false, ApplyFileContext: false);

        // Edits arrived during the save window (generation advanced) → do NOT clear
        // the dirty flag; do apply file-context (path/name) since the save did land.
        var noEditsArrivedDuringSave = generationNow == generationAtSaveStart;
        return new SaveCompletionPlan(
            MarkSaved: noEditsArrivedDuringSave,
            ApplyFileContext: true);
    }
}

/// <summary>
/// Result of <see cref="SaveCompletionPlanner.Plan"/>.
/// </summary>
/// <param name="MarkSaved">
///   When <c>true</c>, mark the workbook saved to clear the dirty flag.
///   When <c>false</c>, the workbook has unsaved edits that arrived after the save started.
/// </param>
/// <param name="ApplyFileContext">
///   When <c>true</c>, apply the post-save file context: update the current file path,
///   workbook name, and the recent-files list.
///   When <c>false</c>, skip these mutations (the save result is stale relative to
///   the current workbook state).
/// </param>
public sealed record SaveCompletionPlan(bool MarkSaved, bool ApplyFileContext);
