using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// r266: the two content comparisons <see cref="MoveRangeCommand"/>'s no-op decision needs and that
/// nothing else in this program had built yet -- sparklines and a chart's verbatim formula snapshot.
///
/// <para>MoveRange is the last command on the no-op debt, and it is there because of size: twenty-four
/// snapshot fields, which Revert restores through as many helpers. Most of them are maps of
/// key-to-prior-value and compare directly against the model. These two do not, and each is large
/// enough to need its own coverage contract, so they are built and proved here BEFORE the decision
/// that will use them -- the order r262 established at the cost of a round, and r265's third parse
/// bug reinforced.</para>
/// </summary>
internal static class MoveRangeSnapshotComparison
{
    /// <summary>
    /// <see cref="SparklineModel"/> is a CLASS with twenty-nine scalar members and no collections, so
    /// <c>==</c> is reference equality with no value semantics at all. A move rewrites a sparkline's
    /// <c>DataRange</c> and <c>Location</c> IN PLACE on the captured instance, which is the worst
    /// case for a reference comparison: the captured object and the current object are the same
    /// object, so identity says "unchanged" for a sparkline that moved.
    /// </summary>
    internal static bool SameSparkline(SparklineModel left, SparklineModel right) =>
        left.Id == right.Id
        && left.DataRange.Equals(right.DataRange)
        && left.Location.Equals(right.Location)
        && left.Kind == right.Kind
        && left.GroupId == right.GroupId
        && left.ShowMarkers == right.ShowMarkers
        && left.ShowHighPoint == right.ShowHighPoint
        && left.ShowLowPoint == right.ShowLowPoint
        && left.ShowFirstPoint == right.ShowFirstPoint
        && left.ShowLastPoint == right.ShowLastPoint
        && left.ShowNegativePoints == right.ShowNegativePoints
        && left.ShowAxis == right.ShowAxis
        && left.DisplayHidden == right.DisplayHidden
        && left.RightToLeft == right.RightToLeft
        && left.SeriesColor == right.SeriesColor
        && left.NegativeColor == right.NegativeColor
        && left.AxisColor == right.AxisColor
        && left.MarkersColor == right.MarkersColor
        && left.HighPointColor == right.HighPointColor
        && left.LowPointColor == right.LowPointColor
        && left.FirstPointColor == right.FirstPointColor
        && left.LastPointColor == right.LastPointColor
        && left.LineWeight == right.LineWeight
        && left.MinAxisType == right.MinAxisType
        && left.MaxAxisType == right.MaxAxisType
        && left.ManualMin == right.ManualMin
        && left.ManualMax == right.ManualMax
        && left.DisplayEmptyCellsAs == right.DisplayEmptyCellsAs
        && left.DateAxisRange == right.DateAxisRange;

    /// <summary>
    /// A chart's verbatim formula snapshot: three list members of formula strings and raw XML plus
    /// three error-bar fields, any of which a move can rewrite. The lists are compared element by
    /// element, in order -- a series' position in them IS its identity, because the snapshot is
    /// captured positionally against <c>chart.Series</c>.
    ///
    /// <para>The three error-bar members were missing from the first draft and caught by
    /// <c>R266_MoveRangeComparisonCoverageContractTests</c>, alongside twenty-one missing sparkline
    /// members. Two comparisons written by hand, two incomplete, both found by the contracts rather
    /// than by re-reading.</para>
    /// </summary>
    internal static bool SameChartVerbatim(
        RowColumnShiftHelpers.ChartVerbatimSnapshot? left,
        RowColumnShiftHelpers.ChartVerbatimSnapshot? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;

        return SameList(left.VerbatimSeriesFormulas, right.VerbatimSeriesFormulas, static (a, b) => a == b)
            && SameList(left.DataLabelFormulas, right.DataLabelFormulas, static (a, b) => a == b)
            && SameList(left.MultiLevelCategoryXml, right.MultiLevelCategoryXml, static (a, b) => a == b)
            && left.ErrorBarsCaptured == right.ErrorBarsCaptured
            && string.Equals(left.ErrorBarPlusRangeFormula, right.ErrorBarPlusRangeFormula, StringComparison.Ordinal)
            && string.Equals(left.ErrorBarMinusRangeFormula, right.ErrorBarMinusRangeFormula, StringComparison.Ordinal);
    }

    /// <summary>
    /// Order-sensitive element-wise comparison for a nullable list. Absent and empty are
    /// DIFFERENT: a snapshot that recorded "this chart had no verbatim formulas at all" is not the
    /// same state as one that recorded an empty list, and the restore path distinguishes them.
    /// </summary>
    internal static bool SameList<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right, Func<T, T, bool> same)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (!same(left[i], right[i]))
                return false;
        }

        return true;
    }


    /// <summary>
    /// A per-address companion collection, compared over exactly the addresses the snapshot was
    /// captured across. Present-with-this-value versus absent: the capture records only the addresses
    /// that HAD an entry, so an address that has one now and did not before is a change even though
    /// the captured map says nothing about it.
    /// </summary>
    internal static bool SameScopedDictionary<TValue>(
        Func<SheetId, Sheet> resolveSheet,
        Func<Sheet, Dictionary<CellAddress, TValue>> selector,
        Dictionary<CellAddress, TValue>? captured,
        IReadOnlyList<CellAddress> addresses,
        Func<TValue, TValue, bool> same)
    {
        if (captured is null)
            return true;

        foreach (var address in addresses)
        {
            var presentNow = selector(resolveSheet(address.Sheet)).TryGetValue(address, out var now);
            var presentBefore = captured.TryGetValue(address, out var before);
            if (presentNow != presentBefore)
                return false;
            if (presentNow && !same(now!, before!))
                return false;
        }

        return true;
    }

    /// <summary>The set-shaped counterpart, for shown-comment flags.</summary>
    internal static bool SameScopedAddressSet(
        Func<SheetId, Sheet> resolveSheet,
        Func<Sheet, HashSet<CellAddress>> selector,
        HashSet<CellAddress>? captured,
        IReadOnlyList<CellAddress> addresses)
    {
        if (captured is null)
            return true;

        foreach (var address in addresses)
        {
            if (selector(resolveSheet(address.Sheet)).Contains(address) != captured.Contains(address))
                return false;
        }

        return true;
    }
    /// <summary>
    /// Every entry a snapshot recorded still holds the value it recorded, and the map has not grown.
    /// This is the shape most of MoveRange's snapshots have -- comments, hyperlinks, formulas,
    /// rule formulas, named ranges -- so they share one comparison rather than eleven copies.
    /// </summary>
    internal static bool SameMap<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue>? captured,
        IReadOnlyDictionary<TKey, TValue> current,
        Func<TValue, TValue, bool> same)
        where TKey : notnull
    {
        if (captured is null)
            return true;
        if (captured.Count != current.Count)
            return false;

        foreach (var (key, value) in captured)
        {
            if (!current.TryGetValue(key, out var currentValue) || !same(value, currentValue))
                return false;
        }

        return true;
    }
}
