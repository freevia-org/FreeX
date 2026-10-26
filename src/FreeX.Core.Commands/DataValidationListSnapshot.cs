using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// r256: the before/after decision for the two commands that REPLACE a sheet's whole validation
/// list -- <see cref="FormatPainterDataValidationCommand"/> and
/// <see cref="PasteDataValidationCommand"/>.
/// <para>
/// r221 put both on the r208 debt for want of an `affected`/`_added` list to test after the loop.
/// They do not need one: each already snapshots the target sheet's ENTIRE
/// <c>Sheet.DataValidations</c> list before mutating, and each Revert restores precisely that list
/// and nothing else, so the snapshot is a complete account of what the command can change. The only
/// thing missing was a comparison that survives a copy, which <c>DataValidation.SameAs</c>'s
/// <c>ignoreIdentity</c> option supplies: copies mint a fresh Id by design, so an identity-sensitive
/// comparison of a re-copy against its predecessor could never fire.
/// </para>
/// </summary>
internal static class DataValidationListSnapshot
{
    /// <summary>
    /// Captures the sheet's rules for undo. Identity-PRESERVING (<see cref="DataValidation.Clone"/>,
    /// not <c>CloneWithNewIdentity</c>): a snapshot restored under fresh Ids would make undo an edit
    /// of its own, because rules are resolved by Id elsewhere.
    /// </summary>
    public static List<DataValidation> Capture(Sheet sheet) =>
        sheet.DataValidations.Select(rule => rule.Clone()).ToList();

    /// <summary>
    /// True when <paramref name="after"/> holds the same rules, in the same order, as
    /// <paramref name="before"/> -- ignoring the per-copy Id churn and nothing else.
    /// <para>
    /// Order-sensitive on purpose. Both commands rebuild the list by removing overlaps in place and
    /// appending copies at the end, so a repeat run reproduces the previous run's order; a
    /// re-ordering that somehow arose would be reported as a change, which is the safe direction to
    /// be wrong in for a no-op decision.
    /// </para>
    /// </summary>
    public static bool Unchanged(
        IReadOnlyList<DataValidation> before,
        IReadOnlyList<DataValidation> after)
    {
        if (before.Count != after.Count)
            return false;

        for (var i = 0; i < before.Count; i++)
        {
            if (!before[i].SameAs(after[i], ignoreIdentity: true))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Puts the sheet's original rule INSTANCES back after a run decided to be a no-op. A no-op is
    /// never pushed onto the undo stack, so a command that reports one must leave nothing behind --
    /// including the fresh Ids its (content-identical) replacements were built with.
    /// </summary>
    public static void Restore(Sheet sheet, IReadOnlyList<DataValidation> originals)
    {
        sheet.DataValidations.Clear();
        foreach (var rule in originals)
            sheet.DataValidations.Add(rule);
    }
}
