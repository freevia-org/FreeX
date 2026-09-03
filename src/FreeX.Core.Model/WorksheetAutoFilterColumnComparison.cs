namespace FreeX.Core.Model;

/// <summary>
/// r253: content comparison for <see cref="WorksheetAutoFilterColumnModel"/> and the nested filter
/// models it carries.
///
/// <para>The compiler-generated record equality on these types is NOT usable for deciding whether an
/// AutoFilter command would change anything. Eleven of the column model's fifteen members are
/// reference types -- three dictionaries, three lists, four nested models, and the values list --
/// and record equality compares those with EqualityComparer&lt;T&gt;.Default, i.e. by REFERENCE for
/// the collections and (transitively, via their own NativeAttributes dictionaries) for the nested
/// models. Re-applying a filter builds a brand-new column model with identical content, so record
/// equality reports "different" for every one of them.</para>
///
/// <para>The comparison is structured so that record equality still does the work it is good at:
/// every member that would be compared by reference is stripped to a shared instance, the stripped
/// pair is compared with <c>==</c> (which covers the scalars, and keeps covering a scalar member
/// added later without an edit here), and each stripped member is then compared by content.
/// <c>R253_AutoFilterColumnComparisonCoverageContractTests</c> fails if a reference-typed member is
/// added to any of these types without being handled here.</para>
/// </summary>
public static class WorksheetAutoFilterColumnComparison
{
    // Shared empty instances: Strip must substitute the SAME instance on both sides, so that record
    // equality's reference comparison of the stripped members always succeeds. A fresh [] per call
    // would allocate two distinct arrays and never compare equal.
    private static readonly IReadOnlyList<string> NoStrings = [];
    private static readonly IReadOnlyList<WorksheetAutoFilterDateGroupItemModel> NoDateGroups = [];
    private static readonly IReadOnlyList<WorksheetAutoFilterCustomFilterModel> NoCustomFilters = [];

    /// <summary>
    /// True when <paramref name="left"/> and <paramref name="right"/> describe the same filter
    /// criterion -- every member compared by content, however either was constructed.
    /// </summary>
    public static bool SameAs(this WorksheetAutoFilterColumnModel? left, WorksheetAutoFilterColumnModel? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;

        if (Strip(left) != Strip(right))
            return false;

        return SameStrings(left.Values, right.Values)
            && SameDateGroups(left.DateGroups, right.DateGroups)
            && SameMap(left.NativeFiltersAttributes, right.NativeFiltersAttributes)
            && SameCustomFilters(left.CustomFilters, right.CustomFilters)
            && SameMap(left.NativeCustomFiltersAttributes, right.NativeCustomFiltersAttributes)
            && SameTop10(left.Top10, right.Top10)
            && SameDynamicFilter(left.DynamicFilter, right.DynamicFilter)
            && SameColorFilter(left.ColorFilter, right.ColorFilter)
            && SameIconFilter(left.IconFilter, right.IconFilter)
            && SameStrings(left.NativeFilterXmls, right.NativeFilterXmls)
            && SameMap(left.NativeAttributes, right.NativeAttributes);
    }

    /// <summary>
    /// Every member record equality would compare by reference, replaced with a shared instance --
    /// what survives is exactly the members record equality compares correctly.
    /// </summary>
    private static WorksheetAutoFilterColumnModel Strip(WorksheetAutoFilterColumnModel model) => model with
    {
        Values = NoStrings,
        DateGroups = NoDateGroups,
        NativeFiltersAttributes = null,
        CustomFilters = NoCustomFilters,
        NativeCustomFiltersAttributes = null,
        Top10 = null,
        DynamicFilter = null,
        ColorFilter = null,
        IconFilter = null,
        NativeFilterXmls = NoStrings,
        NativeAttributes = null,
    };

    // Each nested model carries scalars plus a single NativeAttributes dictionary, so the same
    // strip-then-compare shape applies: `with { NativeAttributes = null }` leaves record equality
    // covering every scalar, including one added later.
    private static bool SameTop10(WorksheetAutoFilterTop10Model? left, WorksheetAutoFilterTop10Model? right) =>
        left is null
            ? right is null
            : right is not null
              && (left with { NativeAttributes = null }) == (right with { NativeAttributes = null })
              && SameMap(left.NativeAttributes, right.NativeAttributes);

    private static bool SameDynamicFilter(WorksheetAutoFilterDynamicFilterModel? left, WorksheetAutoFilterDynamicFilterModel? right) =>
        left is null
            ? right is null
            : right is not null
              && (left with { NativeAttributes = null }) == (right with { NativeAttributes = null })
              && SameMap(left.NativeAttributes, right.NativeAttributes);

    private static bool SameColorFilter(WorksheetAutoFilterColorFilterModel? left, WorksheetAutoFilterColorFilterModel? right) =>
        left is null
            ? right is null
            : right is not null
              && (left with { NativeAttributes = null }) == (right with { NativeAttributes = null })
              && SameMap(left.NativeAttributes, right.NativeAttributes);

    private static bool SameIconFilter(WorksheetAutoFilterIconFilterModel? left, WorksheetAutoFilterIconFilterModel? right) =>
        left is null
            ? right is null
            : right is not null
              && (left with { NativeAttributes = null }) == (right with { NativeAttributes = null })
              && SameMap(left.NativeAttributes, right.NativeAttributes);

    private static bool SameCustomFilter(WorksheetAutoFilterCustomFilterModel left, WorksheetAutoFilterCustomFilterModel right) =>
        (left with { NativeAttributes = null }) == (right with { NativeAttributes = null })
        && SameMap(left.NativeAttributes, right.NativeAttributes);

    private static bool SameDateGroupItem(WorksheetAutoFilterDateGroupItemModel left, WorksheetAutoFilterDateGroupItemModel right) =>
        (left with { NativeAttributes = null }) == (right with { NativeAttributes = null })
        && SameMap(left.NativeAttributes, right.NativeAttributes);

    // Lists are compared in ORDER: filter children round-trip in document order, so a reordering is
    // a real change to what gets written.
    private static bool SameCustomFilters(
        IReadOnlyList<WorksheetAutoFilterCustomFilterModel>? left,
        IReadOnlyList<WorksheetAutoFilterCustomFilterModel>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (!SameCustomFilter(left[i], right[i]))
                return false;
        }
        return true;
    }

    private static bool SameDateGroups(
        IReadOnlyList<WorksheetAutoFilterDateGroupItemModel>? left,
        IReadOnlyList<WorksheetAutoFilterDateGroupItemModel>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (!SameDateGroupItem(left[i], right[i]))
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
            if (!right.TryGetValue(pair.Key, out var otherValue) || !string.Equals(pair.Value, otherValue, StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}
