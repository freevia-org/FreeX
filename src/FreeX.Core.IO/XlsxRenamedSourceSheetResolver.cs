namespace FreeX.Core.IO;

// R102-io-rename-worksheet-exclusion-sweep-1: XlsxSourcePackagePreservationContext.SourceSheets is keyed
// by each sheet's name AS LOADED from the pristine source package; context.TargetSheets is keyed by its
// name in the FRESHLY GENERATED package -- i.e. AFTER any in-session rename has already been applied and
// re-serialized. A plain lookup of a source (load-time) sheet name against the new-name-keyed
// TargetSheets dictionary therefore fails for every renamed sheet, indistinguishable from that sheet
// having been deleted outright -- see XlsxFileAdapter.SourcePackage.cs's GetExcludedWorksheetPackagePartPaths
// (fixed under R102-io-rename-worksheet-exclusion-1) for the first-discovered instance of this bug class.
//
// That fix resolved the ambiguity via the rename-stable Sheet.Id identity threaded in specifically for it
// (sourcePackage.SourceSheetIdsByLocalId). That identity is only wired through the one call site already
// fixed in XlsxFileAdapter.SourcePackage.cs; every OTHER per-feature preserver below is invoked from that
// same protected file without it. Rather than requiring further plumbing changes there, this resolver uses
// a self-contained fallback available from context alone: a plain rename never changes the sheet's own
// physical worksheetN.xml part path (proven by
// R102_RenameSheetPreservedPartsTests.RenameSheet_KeepsQueryTablePartAndRelationship_SingleSheetBook,
// which asserts the renamed sheet's worksheet part path is unchanged). So when the load-time NAME no
// longer resolves in TargetSheets, falling back to a match on the load-time worksheet PATH against
// TargetSheets' own values recovers exactly the renamed-but-not-renumbered case, without needing Sheet.Id
// at all. A sheet that was both renamed AND renumbered (or genuinely deleted) still correctly falls
// through unresolved, matching this bug class's pre-existing (if incomplete) behavior for that rarer
// compound edit.
//
// R102-io-rename-worksheet-exclusion-sweep-1-falsepositive: a naive path-only fallback is itself unsound
// when combined with DELETE+renumber: deleting sheet B (originally worksheet2.xml) from {A, B, C} shifts
// the SURVIVING sheet C down to worksheet2.xml too (ClosedXML renumbers sequentially), so B's OLD path now
// coincides with C's NEW path even though B no longer exists at all. A naive fallback would then wrongly
// resolve B's lookup to C's target entry, resurrecting B's stale per-sheet content (e.g. a legacyDrawing
// marker or webPublishItems block) onto the unrelated surviving sheet C -- caught by
// FileAdapterSmokeTests.XlsxAdapter_LoadedWorkbookSave_DoesNotResurrectDeletedSheetUnsupportedWorksheetArtifacts.
// Guard against this: a path match is only trusted when the candidate target sheet's NAME did not also
// exist as a load-time name (i.e. it's a genuinely NEW name introduced by an in-session rename). C's name
// already existed at load time, so C's path-coincidence with B is correctly rejected; a real rename's new
// name (e.g. "PictureRenamed") never existed at load time, so it is correctly accepted.
internal static class XlsxRenamedSourceSheetResolver
{
    /// <summary>
    /// Resolves a source (load-time) sheet's current worksheet path in the target package, first by an
    /// unchanged name, then by falling back to the sheet's own (rename-stable) worksheet part path.
    /// Returns false when the sheet's worksheet part is genuinely gone from the target.
    /// </summary>
    public static bool TryResolveTargetWorksheetPath(
        XlsxSourcePackagePreservationContext context,
        string sourceSheetName,
        string sourceWorksheetPath,
        out string targetWorksheetPath)
    {
        return TryResolveCurrentSheet(context, sourceSheetName, sourceWorksheetPath, out _, out targetWorksheetPath);
    }

    /// <summary>
    /// Same resolution as <see cref="TryResolveTargetWorksheetPath"/>, but also returns the CURRENT
    /// (post-rename) sheet name -- needed by callers that must look the sheet up in the live
    /// <c>Workbook</c> model (which only knows sheets by their current name), not just its package path.
    /// </summary>
    public static bool TryResolveCurrentSheet(
        XlsxSourcePackagePreservationContext context,
        string sourceSheetName,
        string sourceWorksheetPath,
        out string currentSheetName,
        out string targetWorksheetPath)
    {
        if (context.TargetSheets.TryGetValue(sourceSheetName, out var direct))
        {
            currentSheetName = sourceSheetName;
            targetWorksheetPath = direct;
            return true;
        }

        var normalizedSourcePath = XlsxPackagePath.NormalizePackagePath(sourceWorksheetPath);
        foreach (var (candidateName, candidatePath) in context.TargetSheets)
        {
            // Reject a candidate whose name already existed at load time: its path coincidence with
            // sourceSheetName is a renumbering shift of THAT (still-existing, matched-by-name) sheet,
            // not evidence that sourceSheetName was renamed to candidateName.
            if (context.SourceSheets.ContainsKey(candidateName))
                continue;

            if (string.Equals(
                    XlsxPackagePath.NormalizePackagePath(candidatePath),
                    normalizedSourcePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                currentSheetName = candidateName;
                targetWorksheetPath = candidatePath;
                return true;
            }
        }

        currentSheetName = "";
        targetWorksheetPath = "";
        return false;
    }
}
