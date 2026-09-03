using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// r256: content comparison for the pivot state a command captures for undo, so a command can decide
/// post-hoc whether it wrote anything.
///
/// <para>Every pivot snapshot is a record of lists captured with <c>ToList()</c>, so record equality
/// compares them by reference and always reports "changed" -- the same trap the AutoFilter models
/// carried (r253-r255), for the fifth and sixth model family. The lists themselves hold records, and
/// of those only <see cref="PivotFieldModel"/> carries a collection member of its own
/// (<c>SelectedItems</c>); the rest are scalars throughout, so <c>EqualityComparer&lt;T&gt;.Default</c>
/// is the right comparison for their elements and stays right as scalar members are added.
/// <c>R256_PivotSnapshotComparisonCoverageContractTests</c> fails if that stops being true.</para>
/// </summary>
internal static class PivotSnapshotComparison
{
    private static readonly IReadOnlyList<string> NoStrings = [];

    /// <summary>
    /// Element-wise, in order. Order is content for every one of these lists: the row/column/page
    /// field order is the pivot's layout, and the filter and sort order is the order they are
    /// applied and written.
    /// </summary>
    internal static bool SameFields(IReadOnlyList<PivotFieldModel> left, IReadOnlyList<PivotFieldModel> right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (!SameField(left[i], right[i]))
                return false;
        }
        return true;
    }

    /// <summary>
    /// <see cref="PivotFieldModel.SelectedItems"/> is the field's checkbox filter, and it is rebuilt
    /// rather than shared whenever a dialog re-applies a selection -- so it is stripped to a shared
    /// instance and compared by content, leaving record equality to cover the seventeen scalars.
    /// </summary>
    private static bool SameField(PivotFieldModel left, PivotFieldModel right) =>
        (left with { SelectedItems = NoStrings }) == (right with { SelectedItems = NoStrings })
        && SameStrings(left.SelectedItems, right.SelectedItems);

    /// <summary>
    /// For element types with no collection member of their own, where record equality IS content
    /// equality. The coverage contract checks that claim against the types rather than trusting it.
    /// </summary>
    internal static bool SameScalarRecords<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
                return false;
        }
        return true;
    }

    private static bool SameStrings(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    /// <summary>
    /// The rendered-cell half, shared by every pivot command: a pivot command's
    /// <c>_targetSnapshot</c> is the block the re-render overwrote, so comparing it against the sheet
    /// answers empirically the question r219 could not answer by reasoning -- whether the re-render
    /// produced anything different from what was already on the sheet.
    /// </summary>
    internal static bool RenderedCellsUnchanged(Sheet sheet, List<(CellAddress Address, Cell? Cell)>? targetSnapshot)
    {
        if (targetSnapshot is null)
            return true;

        foreach (var (address, cell) in targetSnapshot)
        {
            if (!CellEditCompanionSnapshot.SameCellOrAbsent(sheet, address, cell))
                return false;
        }
        return true;
    }

    /// <summary>
    /// The merged-region half, for the commands that capture the merges their re-render can strip.
    /// Compared as a set over the same footprint the capture used, since re-adding the same regions
    /// in a different order is not a change the user can see or the file can record.
    /// </summary>
    internal static bool MergedRegionsUnchanged(
        Sheet sheet,
        List<GridRange>? captured,
        Func<GridRange, bool> overlapsCapturedFootprint)
    {
        if (captured is null)
            return true;

        // The predicate is the caller's own capture filter, passed in rather than reconstructed here,
        // so the set compared is by construction the set captured -- one command scopes its capture to
        // a single footprint, another to the union of two.
        var current = sheet.MergedRegions.Where(overlapsCapturedFootprint).ToList();
        if (current.Count != captured.Count)
            return false;

        foreach (var region in captured)
        {
            if (!current.Contains(region))
                return false;
        }
        return true;
    }
}
