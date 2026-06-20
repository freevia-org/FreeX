namespace FreeX.App.Presentation.SheetUI;

/// <summary>
/// One selectable destination in the Move-or-Copy dialog: a sheet name plus the insert-before index
/// that selecting it represents. The terminal "(move to end)" entry carries an index equal to the
/// sheet count.
/// </summary>
public sealed record MoveCopySheetTarget(string DisplayName, int InsertBeforeIndex);

/// <summary>
/// The plan a host executes after the Move-or-Copy dialog is accepted. <see cref="InsertBeforeIndex"/>
/// is the 0-based position the (possibly copied) sheet should land before; an index equal to the sheet
/// count means "move to the end". <see cref="CreateCopy"/> mirrors the dialog's "Create a copy" toggle.
/// </summary>
public sealed record MoveCopySheetPlan(int InsertBeforeIndex, bool CreateCopy);

/// <summary>
/// Portable planner for the sheet-tab / Format-menu "Move or Copy Sheet" dialog — pure data in,
/// pure data out, with no view-framework or host types. A host passes the workbook's ordered sheet
/// names and the source sheet's index; the planner builds the "Before sheet" list (one entry per
/// sheet plus a trailing "move to end" entry), picks a sensible initial selection, and clamps the
/// accepted result. The host maps the resulting <see cref="MoveCopySheetPlan"/> onto its Core
/// commands (DuplicateSheetCommand / move commands).
/// </summary>
public static class MoveCopySheetPlanner
{
    /// <summary>
    /// Builds the ordered "Before sheet" target list: one <see cref="MoveCopySheetTarget"/> per sheet
    /// (insert-before that sheet) followed by a terminal entry whose index equals the sheet count.
    /// </summary>
    public static IReadOnlyList<MoveCopySheetTarget> BuildTargets(
        IReadOnlyList<string> sheetNames,
        string moveToEndLabel)
    {
        ArgumentNullException.ThrowIfNull(sheetNames);
        ArgumentNullException.ThrowIfNull(moveToEndLabel);

        var targets = new List<MoveCopySheetTarget>(sheetNames.Count + 1);
        for (var index = 0; index < sheetNames.Count; index++)
            targets.Add(new MoveCopySheetTarget(sheetNames[index], index));

        targets.Add(new MoveCopySheetTarget(moveToEndLabel, sheetNames.Count));
        return targets;
    }

    /// <summary>
    /// Picks the initial "Before sheet" selection for a move: the source sheet's own slot when present
    /// (so an un-changed selection is a no-op move), otherwise the terminal "move to end" entry.
    /// Returns the index into <paramref name="targets"/>, or 0 when the list is empty.
    /// </summary>
    public static int InitialTargetIndex(IReadOnlyList<MoveCopySheetTarget> targets, int sourceIndex)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
            return 0;

        for (var index = 0; index < targets.Count; index++)
        {
            if (targets[index].InsertBeforeIndex == sourceIndex)
                return index;
        }

        return targets.Count - 1;
    }

    /// <summary>Builds the accepted plan, clamping the insert index into <c>[0, sheetCount]</c>.</summary>
    public static MoveCopySheetPlan CreatePlan(int insertBeforeIndex, bool createCopy, int sheetCount) =>
        new(Math.Clamp(insertBeforeIndex, 0, Math.Max(0, sheetCount)), createCopy);

    /// <summary>
    /// Resolves the final 0-based landing index of the sheet after a move (no copy), given the source
    /// sheet's current index and the chosen insert-before index, accounting for the source itself being
    /// removed from the order before re-insertion. Used by hosts that move via a from/to index command.
    /// </summary>
    public static int ResolveMoveTargetIndex(int sourceIndex, int insertBeforeIndex, int sheetCount)
    {
        var lastIndex = Math.Max(0, sheetCount - 1);
        if (insertBeforeIndex <= sourceIndex)
            return Math.Clamp(insertBeforeIndex, 0, lastIndex);

        // Inserting after the source: the source's own removal shifts later positions left by one.
        return Math.Clamp(insertBeforeIndex - 1, 0, lastIndex);
    }

    /// <summary>
    /// Resolves the final 0-based landing index for a copied sheet after the host has inserted the
    /// duplicate immediately after the source. <paramref name="insertBeforeIndex"/> is relative to the
    /// original sheet order, before the duplicate exists.
    /// </summary>
    public static int ResolveCopyTargetIndex(int sourceIndex, int insertBeforeIndex, int originalSheetCount)
    {
        _ = sourceIndex;
        var newSheetCount = Math.Max(1, originalSheetCount + 1);
        var lastIndex = newSheetCount - 1;
        if (insertBeforeIndex >= originalSheetCount)
            return lastIndex;

        return Math.Clamp(insertBeforeIndex, 0, lastIndex);
    }
}
