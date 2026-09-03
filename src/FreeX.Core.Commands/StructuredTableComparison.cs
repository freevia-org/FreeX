using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// r265: content comparison for <see cref="StructuredTableModel"/>, so a command that replaces a
/// table with a freshly built copy can decide post-hoc whether anything actually differs.
///
/// <para>The model is a CLASS, and every structural edit goes through
/// <c>StructuredTableDesignCommandHelpers.CopyTable</c>, which builds a new instance -- so <c>==</c>
/// is reference equality against an object that is new by construction and can never report
/// "unchanged". Twenty-seven members, two of them lists of records that carry collections of their
/// own, is well past the point where re-reading is a check, which is why
/// <c>R265_StructuredTableComparisonCoverageContractTests</c> derives the field list from
/// <c>CaptureCopyState</c> -- the model's own maintained enumeration of what a table consists of.</para>
/// </summary>
internal static class StructuredTableComparison
{
    private static readonly IReadOnlyList<string> NoStrings = [];

    internal static bool Same(StructuredTableModel? left, StructuredTableModel? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;

        return left.Id == right.Id
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
            && left.Range.Equals(right.Range)
            && left.HasAutoFilter == right.HasAutoFilter
            && left.TotalsRowShown == right.TotalsRowShown
            && left.HeaderRowCount == right.HeaderRowCount
            && left.TotalsRowCount == right.TotalsRowCount
            && left.InsertRow == right.InsertRow
            && left.InsertRowShift == right.InsertRowShift
            && left.Published == right.Published
            && string.Equals(left.Comment, right.Comment, StringComparison.Ordinal)
            && string.Equals(left.StyleName, right.StyleName, StringComparison.Ordinal)
            && left.ShowFirstColumn == right.ShowFirstColumn
            && left.ShowLastColumn == right.ShowLastColumn
            && left.ShowRowStripes == right.ShowRowStripes
            && left.ShowColumnStripes == right.ShowColumnStripes
            && string.Equals(left.PackagePart, right.PackagePart, StringComparison.Ordinal)
            && string.Equals(left.NativeSortStateXml, right.NativeSortStateXml, StringComparison.Ordinal)
            && SameMap(left.NativeAttributes, right.NativeAttributes)
            && SameStrings(left.NativeChildXmls, right.NativeChildXmls)
            && SameMap(left.NativeAutoFilterAttributes, right.NativeAutoFilterAttributes)
            && SameStrings(left.NativeAutoFilterChildXmls, right.NativeAutoFilterChildXmls)
            && SameMap(left.NativeStyleInfoAttributes, right.NativeStyleInfoAttributes)
            && SameStrings(left.NativeStyleInfoChildXmls, right.NativeStyleInfoChildXmls)
            && SameColumns(left.Columns, right.Columns)
            && SameFilterColumns(left.FilterColumns, right.FilterColumns);
    }

    /// <summary>
    /// <see cref="StructuredTableColumnModel"/> is a record carrying two collection members of its
    /// own, so it gets the strip-and-compare shape: record equality covers the scalars (and keeps
    /// covering one added later), the two collections are compared by content.
    /// </summary>
    private static bool SameColumns(
        IReadOnlyList<StructuredTableColumnModel> left,
        IReadOnlyList<StructuredTableColumnModel> right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            var strippedLeft = left[i] with { NativeChildXmls = NoStrings, NativeAttributes = null };
            var strippedRight = right[i] with { NativeChildXmls = NoStrings, NativeAttributes = null };
            if (strippedLeft != strippedRight)
                return false;
            if (!SameStrings(left[i].NativeChildXmls, right[i].NativeChildXmls))
                return false;
            if (!SameMap(left[i].NativeAttributes, right[i].NativeAttributes))
                return false;
        }
        return true;
    }

    /// <summary>Through the r254 comparison, which already handles that record's five collections.</summary>
    private static bool SameFilterColumns(
        IReadOnlyList<StructuredTableFilterColumnModel> left,
        IReadOnlyList<StructuredTableFilterColumnModel> right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (!left[i].SameAs(right[i]))
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

    private static bool SameMap(IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        if (left.Count != right.Count) return false;
        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) || !string.Equals(pair.Value, value, StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}
