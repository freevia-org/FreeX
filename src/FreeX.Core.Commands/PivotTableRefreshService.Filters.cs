using System.Globalization;

using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class PivotTableRefreshService
{
    // Pivot source filtering, sorting, grouping, and scalar coercion helpers.
    private static bool MatchesFieldSelections(IReadOnlyList<ScalarValue> row, IReadOnlyList<PivotFieldModel> fields)
    {
        foreach (var field in fields)
        {
            if (field.SelectedItems is { Count: > 0 } selectedItems)
            {
                var hasExplicitSelection = false;
                var matchesExplicitSelection = false;
                string? rowKey = null;

                for (var index = 0; index < selectedItems.Count; index++)
                {
                    var selectedItem = selectedItems[index];
                    if (string.IsNullOrWhiteSpace(selectedItem) ||
                        string.Equals(selectedItem, "(All)", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    hasExplicitSelection = true;
                    rowKey ??= GroupKeyText(row[field.SourceFieldIndex], field);
                    if (!MatchesFieldKeyCandidate(rowKey, row[field.SourceFieldIndex], field, selectedItem))
                        continue;

                    matchesExplicitSelection = true;
                    break;
                }

                if (hasExplicitSelection)
                {
                    if (!matchesExplicitSelection)
                        return false;

                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(field.SelectedItem) ||
                string.Equals(field.SelectedItem, "(All)", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var singleRowKey = GroupKeyText(row[field.SourceFieldIndex], field);
            if (!MatchesFieldKeyCandidate(singleRowKey, row[field.SourceFieldIndex], field, field.SelectedItem))
                return false;
        }

        return true;
    }

    private static List<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> ApplyValueFilters(
        List<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> groups,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<PivotFieldModel> rowFields)
    {
        foreach (var filter in pivotTable.ValueFilters)
        {
            if (filter.SourceFieldIndex is not null &&
                !rowFields.Any(field => field.SourceFieldIndex == filter.SourceFieldIndex.Value))
            {
                continue;
            }

            if (filter.DataFieldIndex < 0 ||
                filter.DataFieldIndex >= pivotTable.DataFields.Count)
            {
                continue;
            }
            if ((filter.Kind == PivotValueFilterKind.Top || filter.Kind == PivotValueFilterKind.Bottom) && filter.Count <= 0)
                continue;

            var dataField = pivotTable.DataFields[filter.DataFieldIndex];
            var groupAggregates = groups
                .Select(group => (Group: group, Value: AggregateDouble(group, dataField, pivotTable, headers)))
                .ToList();
            var average = groupAggregates.Count == 0 ? 0 : groupAggregates.Average(item => item.Value);
            groups = filter.Kind switch
            {
                PivotValueFilterKind.Bottom => groupAggregates.OrderBy(item => item.Value).Take(filter.Count).Select(item => item.Group).ToList(),
                PivotValueFilterKind.Top => groupAggregates.OrderByDescending(item => item.Value).Take(filter.Count).Select(item => item.Group).ToList(),
                PivotValueFilterKind.GreaterThan => groupAggregates.Where(item => item.Value > (filter.ComparisonValue ?? 0)).Select(item => item.Group).ToList(),
                PivotValueFilterKind.GreaterThanOrEqual => groupAggregates.Where(item => item.Value >= (filter.ComparisonValue ?? 0)).Select(item => item.Group).ToList(),
                PivotValueFilterKind.LessThan => groupAggregates.Where(item => item.Value < (filter.ComparisonValue ?? 0)).Select(item => item.Group).ToList(),
                PivotValueFilterKind.LessThanOrEqual => groupAggregates.Where(item => item.Value <= (filter.ComparisonValue ?? 0)).Select(item => item.Group).ToList(),
                PivotValueFilterKind.Equals => groupAggregates.Where(item => Math.Abs(item.Value - (filter.ComparisonValue ?? 0)) < 0.0000001).Select(item => item.Group).ToList(),
                PivotValueFilterKind.DoesNotEqual => groupAggregates.Where(item => Math.Abs(item.Value - (filter.ComparisonValue ?? 0)) >= 0.0000001).Select(item => item.Group).ToList(),
                PivotValueFilterKind.Between => groupAggregates.Where(item => IsBetween(item.Value, filter)).Select(item => item.Group).ToList(),
                PivotValueFilterKind.NotBetween => groupAggregates.Where(item => !IsBetween(item.Value, filter)).Select(item => item.Group).ToList(),
                PivotValueFilterKind.AboveAverage => groupAggregates.Where(item => item.Value > average).Select(item => item.Group).ToList(),
                PivotValueFilterKind.BelowAverage => groupAggregates.Where(item => item.Value < average).Select(item => item.Group).ToList(),
                _ => groups
            };
            groups = groups.OrderBy(group => group.Key, PivotKeyComparer.Instance).ToList();
        }

        return groups;
    }

    private static List<PivotKey> ApplyValueFilters(
        List<PivotKey> keys,
        PivotColumnRowMap rowsByColumnKey,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<PivotFieldModel> fields,
        PivotColumnAggregateCache? aggregateCache)
    {
        for (var filterIndex = 0; filterIndex < pivotTable.ValueFilters.Count; filterIndex++)
        {
            var filter = pivotTable.ValueFilters[filterIndex];
            if (filter.SourceFieldIndex is null ||
                IndexOfSourceField(fields, filter.SourceFieldIndex.Value) < 0)
            {
                continue;
            }

            if (filter.DataFieldIndex < 0 ||
                filter.DataFieldIndex >= pivotTable.DataFields.Count)
            {
                continue;
            }
            if ((filter.Kind == PivotValueFilterKind.Top || filter.Kind == PivotValueFilterKind.Bottom) && filter.Count <= 0)
                continue;

            var dataField = pivotTable.DataFields[filter.DataFieldIndex];
            var aggregates = new List<(PivotKey Key, double Value)>(keys.Count);
            foreach (var key in keys)
            {
                aggregates.Add((
                    key,
                    ColumnAggregate(key, rowsByColumnKey, dataField, filter.DataFieldIndex, pivotTable, headers, aggregateCache)));
            }
            var average = aggregates.Count == 0 ? 0 : aggregates.Average(item => item.Value);

            keys = filter.Kind switch
            {
                PivotValueFilterKind.Bottom => aggregates.OrderBy(item => item.Value).Take(filter.Count).Select(item => item.Key).ToList(),
                PivotValueFilterKind.Top => aggregates.OrderByDescending(item => item.Value).Take(filter.Count).Select(item => item.Key).ToList(),
                PivotValueFilterKind.GreaterThan => aggregates.Where(item => item.Value > (filter.ComparisonValue ?? 0)).Select(item => item.Key).ToList(),
                PivotValueFilterKind.GreaterThanOrEqual => aggregates.Where(item => item.Value >= (filter.ComparisonValue ?? 0)).Select(item => item.Key).ToList(),
                PivotValueFilterKind.LessThan => aggregates.Where(item => item.Value < (filter.ComparisonValue ?? 0)).Select(item => item.Key).ToList(),
                PivotValueFilterKind.LessThanOrEqual => aggregates.Where(item => item.Value <= (filter.ComparisonValue ?? 0)).Select(item => item.Key).ToList(),
                PivotValueFilterKind.Equals => aggregates.Where(item => Math.Abs(item.Value - (filter.ComparisonValue ?? 0)) < 0.0000001).Select(item => item.Key).ToList(),
                PivotValueFilterKind.DoesNotEqual => aggregates.Where(item => Math.Abs(item.Value - (filter.ComparisonValue ?? 0)) >= 0.0000001).Select(item => item.Key).ToList(),
                PivotValueFilterKind.Between => aggregates.Where(item => IsBetween(item.Value, filter)).Select(item => item.Key).ToList(),
                PivotValueFilterKind.NotBetween => aggregates.Where(item => !IsBetween(item.Value, filter)).Select(item => item.Key).ToList(),
                PivotValueFilterKind.AboveAverage => aggregates.Where(item => item.Value > average).Select(item => item.Key).ToList(),
                PivotValueFilterKind.BelowAverage => aggregates.Where(item => item.Value < average).Select(item => item.Key).ToList(),
                _ => keys
            };

            if (filterIndex < pivotTable.ValueFilters.Count - 1)
                keys = keys.Order(PivotKeyComparer.Instance).ToList();
        }

        return keys;
    }

    private static bool IsBetween(double value, PivotValueFilterModel filter)
    {
        var first = filter.ComparisonValue ?? 0;
        var second = filter.ComparisonValue2 ?? first;
        var min = Math.Min(first, second);
        var max = Math.Max(first, second);
        return value >= min && value <= max;
    }

    private static List<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> ApplyLabelFilters(
        List<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> groups,
        PivotTableModel pivotTable,
        IReadOnlyList<PivotFieldModel> rowFields)
    {
        foreach (var filter in pivotTable.LabelFilters)
        {
            var rowFieldIndex = IndexOfSourceField(rowFields, filter.SourceFieldIndex);
            if (rowFieldIndex < 0)
                continue;

            groups = groups
                .Where(group => MatchesLabelFilter(group.Key.Values[rowFieldIndex], filter))
                .ToList();
        }

        return groups.OrderBy(group => group.Key, PivotKeyComparer.Instance).ToList();
    }

    private static List<PivotKey> ApplyLabelFilters(
        List<PivotKey> keys,
        PivotTableModel pivotTable,
        IReadOnlyList<PivotFieldModel> fields)
    {
        foreach (var filter in pivotTable.LabelFilters)
        {
            var fieldIndex = IndexOfSourceField(fields, filter.SourceFieldIndex);
            if (fieldIndex < 0)
                continue;

            keys = keys
                .Where(key => MatchesLabelFilter(key.Values[fieldIndex], filter))
                .ToList();
        }

        return keys.Order(PivotKeyComparer.Instance).ToList();
    }

    private static bool MatchesLabelFilter(string label, PivotLabelFilterModel filter)
    {
        var comparison = StringComparison.CurrentCultureIgnoreCase;
        return filter.Kind switch
        {
            PivotLabelFilterKind.Equals => string.Equals(label, filter.Value, comparison),
            PivotLabelFilterKind.DoesNotEqual => !string.Equals(label, filter.Value, comparison),
            PivotLabelFilterKind.BeginsWith => label.StartsWith(filter.Value, comparison),
            PivotLabelFilterKind.EndsWith => label.EndsWith(filter.Value, comparison),
            PivotLabelFilterKind.Contains => label.Contains(filter.Value, comparison),
            PivotLabelFilterKind.DoesNotContain => !label.Contains(filter.Value, comparison),
            PivotLabelFilterKind.GreaterThan => string.Compare(label, filter.Value, comparison) > 0,
            PivotLabelFilterKind.GreaterThanOrEqual => string.Compare(label, filter.Value, comparison) >= 0,
            PivotLabelFilterKind.LessThan => string.Compare(label, filter.Value, comparison) < 0,
            PivotLabelFilterKind.LessThanOrEqual => string.Compare(label, filter.Value, comparison) <= 0,
            PivotLabelFilterKind.Between => string.Compare(label, filter.Value, comparison) >= 0 &&
                                            string.Compare(label, filter.Value2 ?? filter.Value, comparison) <= 0,
            _ => true
        };
    }

    private static List<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> ApplySorts(
        List<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> groups,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<PivotFieldModel> rowFields)
    {
        if (pivotTable.Sorts.Count == 0)
            return groups.OrderBy(group => group.Key, PivotKeyComparer.Instance).ToList();

        var sort = pivotTable.Sorts[^1];
        if (sort.Target == PivotSortTarget.Value &&
            rowFields.Any(field => field.SourceFieldIndex == sort.FieldIndex) &&
            sort.DataFieldIndex >= 0 &&
            sort.DataFieldIndex < pivotTable.DataFields.Count)
        {
            var dataField = pivotTable.DataFields[sort.DataFieldIndex];
            return sort.Direction == PivotSortDirection.Descending
                ? groups.OrderByDescending(group => Aggregate(group, dataField, pivotTable, headers)).ThenBy(group => group.Key, PivotKeyComparer.Instance).ToList()
                : groups.OrderBy(group => Aggregate(group, dataField, pivotTable, headers)).ThenBy(group => group.Key, PivotKeyComparer.Instance).ToList();
        }

        if (!rowFields.Any(field => field.SourceFieldIndex == sort.FieldIndex))
        {
            return groups.OrderBy(group => group.Key, PivotKeyComparer.Instance).ToList();
        }

        return sort.Direction == PivotSortDirection.Descending
            ? groups.OrderByDescending(group => group.Key, PivotKeyComparer.Instance).ToList()
            : groups.OrderBy(group => group.Key, PivotKeyComparer.Instance).ToList();
    }

    private static List<PivotKey> ApplySorts(
        List<PivotKey> keys,
        PivotColumnRowMap rowsByColumnKey,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<PivotFieldModel> fields,
        PivotColumnAggregateCache? aggregateCache)
    {
        if (pivotTable.Sorts.Count == 0)
            return keys.Order(PivotKeyComparer.Instance).ToList();

        var sort = pivotTable.Sorts[^1];
        var fieldIndex = IndexOfSourceField(fields, sort.FieldIndex);
        if (sort.Target == PivotSortTarget.Label && fieldIndex >= 0)
        {
            var labelComparer = Comparer<string>.Create(PivotKeyComparer.CompareKeyText);
            return sort.Direction == PivotSortDirection.Descending
                ? keys.OrderByDescending(key => key.Values[fieldIndex], labelComparer).ThenBy(key => key, PivotKeyComparer.Instance).ToList()
                : keys.OrderBy(key => key.Values[fieldIndex], labelComparer).ThenBy(key => key, PivotKeyComparer.Instance).ToList();
        }

        if (sort.Target == PivotSortTarget.Value &&
            fieldIndex >= 0 &&
            sort.DataFieldIndex >= 0 &&
            sort.DataFieldIndex < pivotTable.DataFields.Count)
        {
            var dataField = pivotTable.DataFields[sort.DataFieldIndex];
            var aggregates = new List<(PivotKey Key, double Value)>(keys.Count);
            foreach (var key in keys)
            {
                aggregates.Add((
                    key,
                    ColumnAggregate(key, rowsByColumnKey, dataField, sort.DataFieldIndex, pivotTable, headers, aggregateCache)));
            }
            return sort.Direction == PivotSortDirection.Descending
                ? aggregates.OrderByDescending(item => item.Value).ThenBy(item => item.Key, PivotKeyComparer.Instance).Select(item => item.Key).ToList()
                : aggregates.OrderBy(item => item.Value).ThenBy(item => item.Key, PivotKeyComparer.Instance).Select(item => item.Key).ToList();
        }

        return keys.Order(PivotKeyComparer.Instance).ToList();
    }

    private static double ColumnAggregate(
        PivotKey key,
        PivotColumnRowMap rowsByColumnKey,
        PivotDataFieldModel dataField,
        int dataFieldIndex,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        PivotColumnAggregateCache? aggregateCache) =>
        aggregateCache?.Get(key, dataFieldIndex) ??
        AggregateDouble(RowsForColumnKey(rowsByColumnKey, key), dataField, pivotTable, headers);

    private static PivotColumnAggregateCache? CreateColumnAggregateCacheIfNeeded(
        PivotColumnRowMap rowsByColumnKey,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<PivotFieldModel> columnFields)
    {
        var aggregateConsumers = 0;
        foreach (var filter in pivotTable.ValueFilters)
        {
            if (filter.SourceFieldIndex is null ||
                IndexOfSourceField(columnFields, filter.SourceFieldIndex.Value) < 0 ||
                filter.DataFieldIndex < 0 ||
                filter.DataFieldIndex >= pivotTable.DataFields.Count)
            {
                continue;
            }

            aggregateConsumers++;
            if (aggregateConsumers > 1)
                return new PivotColumnAggregateCache(rowsByColumnKey, pivotTable, headers);
        }

        foreach (var sort in pivotTable.Sorts)
        {
            if (sort.Target != PivotSortTarget.Value ||
                IndexOfSourceField(columnFields, sort.FieldIndex) < 0 ||
                sort.DataFieldIndex < 0 ||
                sort.DataFieldIndex >= pivotTable.DataFields.Count)
            {
                continue;
            }

            aggregateConsumers++;
            if (aggregateConsumers > 1)
                return new PivotColumnAggregateCache(rowsByColumnKey, pivotTable, headers);
        }

        return null;
    }

    private static string GroupKeyText(ScalarValue value, PivotFieldModel field) =>
        GroupKeyText(value, field.Grouping, field.GroupStart, field.GroupEnd, field.GroupInterval);

    /// <summary>
    /// Matches a persisted "Select Items..."/slicer selection caption against a row's live value.
    /// Tries the refresh-time <paramref name="rowKey"/> (<see cref="GroupKeyText(ScalarValue, PivotFieldModel)"/>,
    /// formatted with <see cref="CultureInfo.CurrentCulture"/>) first, matching the existing
    /// same-culture behavior exactly. For an ungrouped Number/Date field, also falls back to a
    /// culture-INVARIANT candidate: the "Select Items..." dialog and slicer/timeline panes read
    /// their candidate captions via <c>SpreadsheetDisplayFormatter</c>'s CellDisplay profile, which
    /// formats a Number as <c>value.ToString(CultureInfo.InvariantCulture)</c> and a Date as a fixed
    /// "yyyy-MM-dd" invariant string -- independent of <see cref="CultureInfo.CurrentCulture"/> -- so
    /// a caption captured that way never matches a CurrentCulture-formatted row key on any machine
    /// whose culture isn't itself invariant (and the mismatch is equally possible after a save/
    /// reopen across differently-configured machines). Without this fallback, hasExplicitSelection
    /// is true but nothing matches, so the field's filter empties every row (see H-shared-localization-rtl).
    /// Year/Quarter/Month/Day grouped keys are already culture-invariant (see GroupKeyText), so they
    /// need no fallback. NumberRange-grouped bucket labels DO (r176): their "0.5-1", "&gt;100", "&lt;0"
    /// forms are built by string interpolation, which formats the bounds with
    /// <see cref="CultureInfo.CurrentCulture"/> -- so the very same bucket is "0,5-1" on a de-DE machine
    /// and "0.5-1" on en-US. A workbook whose checked-item captions were persisted under one culture
    /// then matches nothing when reopened under another, which empties the field's filter exactly as in
    /// the ungrouped case. r174 declared this instance out of its scope; it is fixed here.
    /// </summary>
    private static bool MatchesFieldKeyCandidate(string rowKey, ScalarValue value, PivotFieldModel field, string candidate)
    {
        if (string.Equals(rowKey, candidate, StringComparison.CurrentCultureIgnoreCase))
            return true;

        return InvariantFieldKeyText(value, field) is { } invariant &&
            string.Equals(invariant, candidate, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The culture-INVARIANT spelling of this field's key for <paramref name="value"/>, or null when the
    /// field's grouping already produces a culture-invariant key (Year/Quarter/Month/Day) and so has no
    /// second spelling to try. Deliberately a MATCHING candidate only -- <see cref="GroupKeyText"/>'s
    /// output is unchanged, because that string is also the row label the user sees, and a German user
    /// should keep seeing the "0,5-1" bucket rather than have it silently switch to "0.5-1".
    /// </summary>
    private static string? InvariantFieldKeyText(ScalarValue value, PivotFieldModel field) =>
        field.Grouping switch
        {
            PivotFieldGrouping.None => InvariantKeyText(value),
            PivotFieldGrouping.NumberRange => NumberRangeKeyText(
                value,
                field.GroupStart ?? 0,
                field.GroupEnd,
                field.GroupInterval ?? 1,
                CultureInfo.InvariantCulture),
            _ => null
        };

    private static string InvariantKeyText(ScalarValue value) =>
        value switch
        {
            NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
            DateTimeValue date => date.ToDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => KeyText(value)
        };

    private static int IndexOfSourceField(IReadOnlyList<PivotFieldModel> fields, int sourceFieldIndex)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            if (fields[index].SourceFieldIndex == sourceFieldIndex)
                return index;
        }

        return -1;
    }

    private static string GroupKeyText(ScalarValue value, PivotFieldGrouping grouping) =>
        GroupKeyText(value, grouping, null, null, null);

    private static string GroupKeyText(ScalarValue value, PivotFieldGrouping grouping, double? groupStart, double? groupEnd, double? groupInterval)
    {
        if (grouping == PivotFieldGrouping.None)
            return KeyText(value);

        if (grouping == PivotFieldGrouping.NumberRange)
            return NumberRangeKeyText(value, groupStart ?? 0, groupEnd, groupInterval ?? 1, CultureInfo.CurrentCulture);

        if (value is not DateTimeValue dateValue)
            return KeyText(value);

        var date = dateValue.ToDateTime();
        return grouping switch
        {
            PivotFieldGrouping.Year => date.Year.ToString(CultureInfo.InvariantCulture),
            PivotFieldGrouping.Quarter => $"{date.Year}-Q{((date.Month - 1) / 3) + 1}",
            PivotFieldGrouping.Month => date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            PivotFieldGrouping.Day => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => KeyText(value)
        };
    }

    /// <summary>
    /// The bucket label for <paramref name="value"/>. <paramref name="formatProvider"/> formats the
    /// numeric bounds: CurrentCulture for the row label the user sees, InvariantCulture for the
    /// cross-culture matching candidate (see <see cref="InvariantFieldKeyText"/>). It is explicit rather
    /// than implicit because these labels are used BOTH as display text and as persisted filter keys,
    /// and those two uses want different cultures.
    /// </summary>
    private static string NumberRangeKeyText(
        ScalarValue value,
        double start,
        double? end,
        double interval,
        IFormatProvider formatProvider)
    {
        // Blank/non-numeric source values (e.g. an empty cell or text mixed into a
        // numeric column) don't belong to any numeric bucket - Excel puts them in a
        // distinct "(blank)" group instead of silently coercing them to 0.
        if (value is not (NumberValue or DateTimeValue or BoolValue))
            return "(blank)";

        if (interval <= 0)
            interval = 1;
        var number = Number(value);

        // The "Ending at" bound (Excel's Group Field dialog) is a load-bearing bucket
        // boundary, not just a UI label: values at or past it don't get their own
        // interval-sized bucket past the configured end - they fall into a single
        // overflow group labeled ">end", the same way Excel does. Guard end > start so a
        // misconfigured/legacy end value (<= start) doesn't collapse every bucket.
        if (end.HasValue && end.Value > start && number >= end.Value)
            return string.Create(formatProvider, $">{end.Value:0.########}");

        // Symmetric with the overflow bucket above: values below the "Starting at" bound
        // don't get their own interval-sized bucket extrapolated backwards past the
        // configured start - they fall into a single underflow group labeled "<start", the
        // same way Excel does. Without this, a value below start fell into a bucket derived
        // by extending the interval grid backwards, whose label described a range the
        // grid math places it in but that isn't the range Excel would show.
        if (number < start)
            return string.Create(formatProvider, $"<{start:0.########}");

        var bucketStart = start + Math.Floor((number - start) / interval) * interval;

        // Excel labels integer-interval groups as an inclusive range ("0-9", "10-19", the
        // upper bound is one less than the next bucket's start) but fractional-interval
        // groups as a half-open range ("0-0.5", "0.5-1", the upper bound IS the next
        // bucket's start). Using the inclusive "-1" form for a fractional interval would
        // understate the range, or even put the end before the start.
        var isIntegerInterval = interval == Math.Floor(interval);
        var bucketEnd = isIntegerInterval ? bucketStart + interval - 1 : bucketStart + interval;
        return string.Create(formatProvider, $"{bucketStart:0.########}-{bucketEnd:0.########}");
    }

    private static string KeyText(ScalarValue value) =>
        value switch
        {
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString(CultureInfo.CurrentCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            DateTimeValue date => date.ToDateTime().ToShortDateString(),
            ErrorValue error => error.Code,
            _ => "(blank)"
        };

    private static double Number(ScalarValue value) =>
        value switch
        {
            NumberValue number => number.Value,
            DateTimeValue date => date.Value,
            BoolValue boolean => boolean.Value ? 1 : 0,
            _ => 0
        };

    private sealed class PivotKey : IEquatable<PivotKey>
    {
        public PivotKey(IReadOnlyList<string> values)
        {
            Values = values;
        }

        public IReadOnlyList<string> Values { get; }

        public bool Equals(PivotKey? other) =>
            other is not null && Values.SequenceEqual(other.Values, StringComparer.CurrentCultureIgnoreCase);

        public override bool Equals(object? obj) =>
            obj is PivotKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var value in Values)
                hash.Add(value, StringComparer.CurrentCultureIgnoreCase);
            return hash.ToHashCode();
        }
    }

    private sealed class PivotKeyComparer : IComparer<PivotKey>
    {
        public static PivotKeyComparer Instance { get; } = new();

        public int Compare(PivotKey? x, PivotKey? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;

            var count = Math.Min(x.Values.Count, y.Values.Count);
            for (var index = 0; index < count; index++)
            {
                var comparison = CompareKeyText(x.Values[index], y.Values[index]);
                if (comparison != 0)
                    return comparison;
            }

            return x.Values.Count.CompareTo(y.Values.Count);
        }

        /// <summary>
        /// Compares two pivot label strings the way Excel orders row/column items: numeric
        /// labels sort by their numeric value (ascending), and numbers always sort before
        /// text labels. Falls back to a plain culture-aware text comparison when either side
        /// isn't purely numeric.
        /// </summary>
        internal static int CompareKeyText(string left, string right)
        {
            var leftIsNumber = TryGetLabelSortNumber(left, out var leftNumber);
            var rightIsNumber = TryGetLabelSortNumber(right, out var rightNumber);

            if (leftIsNumber && rightIsNumber)
                return leftNumber.CompareTo(rightNumber);
            if (leftIsNumber)
                return -1;
            if (rightIsNumber)
                return 1;

            return StringComparer.CurrentCultureIgnoreCase.Compare(left, right);
        }

        /// <summary>
        /// Resolves the number that should drive ordering for a pivot label. A plain numeric
        /// label parses outright; a numeric-group bucket label such as "10-19" or "0-0.5"
        /// (produced by <see cref="NumberRangeKeyText"/>) doesn't parse as a single number, so
        /// falls back to the bucket's numeric start, so groups sort ascending by value instead
        /// of lexicographically (e.g. "20-29" before "100-109", not after).
        /// </summary>
        private static bool TryGetLabelSortNumber(string text, out double number) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out number) ||
            TryParseNumberRangeLabelStart(text, out number) ||
            TryParseNumberRangeOverflowLabel(text, out number) ||
            TryParseNumberRangeUnderflowLabel(text, out number);

        /// <summary>
        /// Parses the leading number out of a "{start}-{end}" numeric-group bucket label (see
        /// <see cref="NumberRangeKeyText"/>), returning that start value. Requires both halves
        /// to parse as numbers with end >= start, which rules out unrelated hyphenated labels
        /// that otherwise look similar, such as a "yyyy-MM" month-grouped date label (e.g.
        /// "2026-01", where the second segment is smaller than the first) or a "yyyy-MM-dd"
        /// day-grouped label (whose tail doesn't parse as a single number at all).
        /// </summary>
        private static bool TryParseNumberRangeLabelStart(string text, out double start)
        {
            start = 0;
            if (string.IsNullOrEmpty(text))
                return false;

            var searchFrom = text[0] == '-' ? 1 : 0;
            var separatorIndex = text.IndexOf('-', searchFrom);
            if (separatorIndex <= 0)
                return false;

            var startText = text[..separatorIndex];
            var endText = text[(separatorIndex + 1)..];
            if (!double.TryParse(startText, NumberStyles.Float, CultureInfo.CurrentCulture, out var startValue) ||
                !double.TryParse(endText, NumberStyles.Float, CultureInfo.CurrentCulture, out var endValue) ||
                endValue < startValue)
            {
                return false;
            }

            start = startValue;
            return true;
        }

        /// <summary>
        /// Parses the boundary out of a ">{end}" overflow-bucket label (see
        /// <see cref="NumberRangeKeyText"/>), produced for values at or past a numeric-range
        /// group's "Ending at" setting, so that overflow bucket sorts numerically (after every
        /// in-range bucket) instead of lexicographically.
        /// </summary>
        private static bool TryParseNumberRangeOverflowLabel(string text, out double boundary)
        {
            boundary = 0;
            if (string.IsNullOrEmpty(text) || text[0] != '>')
                return false;

            return double.TryParse(text[1..], NumberStyles.Float, CultureInfo.CurrentCulture, out boundary);
        }

        /// <summary>
        /// Parses a "&lt;{start}" underflow-bucket label (see <see cref="NumberRangeKeyText"/>),
        /// produced for values below a numeric-range group's "Starting at" setting, so that
        /// underflow bucket sorts numerically before every in-range bucket - including the
        /// bucket that begins exactly at "start", which would otherwise tie with it if this
        /// returned that same start value instead of negative infinity.
        /// </summary>
        private static bool TryParseNumberRangeUnderflowLabel(string text, out double boundary)
        {
            boundary = 0;
            if (string.IsNullOrEmpty(text) || text[0] != '<')
                return false;

            if (!double.TryParse(text[1..], NumberStyles.Float, CultureInfo.CurrentCulture, out _))
                return false;

            boundary = double.NegativeInfinity;
            return true;
        }
    }

    private readonly record struct PivotColumnAggregateCacheKey(PivotKey Key, int DataFieldIndex);

    private sealed class PivotColumnAggregateCache(
        PivotColumnRowMap rowsByColumnKey,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        private readonly Dictionary<PivotColumnAggregateCacheKey, double> _values = [];

        public double Get(PivotKey key, int dataFieldIndex)
        {
            var cacheKey = new PivotColumnAggregateCacheKey(key, dataFieldIndex);
            if (_values.TryGetValue(cacheKey, out var value))
                return value;

            value = AggregateDouble(RowsForColumnKey(rowsByColumnKey, key), pivotTable.DataFields[dataFieldIndex], pivotTable, headers);
            _values.Add(cacheKey, value);
            return value;
        }
    }
}
