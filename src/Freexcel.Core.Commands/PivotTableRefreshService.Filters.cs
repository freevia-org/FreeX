using System.Globalization;

using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static partial class PivotTableRefreshService
{
    // Pivot source filtering, sorting, grouping, and scalar coercion helpers.
    private static bool MatchesFieldSelections(IReadOnlyList<ScalarValue> row, IReadOnlyList<PivotFieldModel> fields)
    {
        foreach (var field in fields)
        {
            var selectedItems = (field.SelectedItems ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item) && !string.Equals(item, "(All)", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (selectedItems.Count > 0)
            {
                if (!selectedItems.Contains(GroupKeyText(row[field.SourceFieldIndex], field), StringComparer.CurrentCultureIgnoreCase))
                    return false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(field.SelectedItem) ||
                string.Equals(field.SelectedItem, "(All)", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(GroupKeyText(row[field.SourceFieldIndex], field), field.SelectedItem, StringComparison.CurrentCultureIgnoreCase))
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
                .Select(group => (Group: group, Value: Aggregate(group, dataField, pivotTable, headers)))
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
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<PivotFieldModel> fields)
    {
        foreach (var filter in pivotTable.ValueFilters)
        {
            if (filter.SourceFieldIndex is null ||
                !fields.Any(field => field.SourceFieldIndex == filter.SourceFieldIndex.Value))
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
            var aggregates = keys
                .Select(key => new
                {
                    Key = key,
                    Value = Aggregate(rows.Where(row => ColumnKeyMatches(row, fields, key)).ToList(), dataField, pivotTable, headers)
                })
                .ToList();
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
            var rowFieldIndex = rowFields.ToList().FindIndex(field => field.SourceFieldIndex == filter.SourceFieldIndex);
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
            var fieldIndex = fields.ToList().FindIndex(field => field.SourceFieldIndex == filter.SourceFieldIndex);
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
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<PivotFieldModel> fields)
    {
        if (pivotTable.Sorts.Count == 0)
            return keys.Order(PivotKeyComparer.Instance).ToList();

        var sort = pivotTable.Sorts[^1];
        var fieldIndex = fields.ToList().FindIndex(field => field.SourceFieldIndex == sort.FieldIndex);
        if (sort.Target == PivotSortTarget.Label && fieldIndex >= 0)
        {
            return sort.Direction == PivotSortDirection.Descending
                ? keys.OrderByDescending(key => key.Values[fieldIndex], StringComparer.CurrentCultureIgnoreCase).ThenBy(key => key, PivotKeyComparer.Instance).ToList()
                : keys.OrderBy(key => key.Values[fieldIndex], StringComparer.CurrentCultureIgnoreCase).ThenBy(key => key, PivotKeyComparer.Instance).ToList();
        }

        if (sort.Target == PivotSortTarget.Value &&
            fieldIndex >= 0 &&
            sort.DataFieldIndex >= 0 &&
            sort.DataFieldIndex < pivotTable.DataFields.Count)
        {
            var dataField = pivotTable.DataFields[sort.DataFieldIndex];
            return sort.Direction == PivotSortDirection.Descending
                ? keys.OrderByDescending(key => Aggregate(rows.Where(row => ColumnKeyMatches(row, fields, key)).ToList(), dataField, pivotTable, headers)).ThenBy(key => key, PivotKeyComparer.Instance).ToList()
                : keys.OrderBy(key => Aggregate(rows.Where(row => ColumnKeyMatches(row, fields, key)).ToList(), dataField, pivotTable, headers)).ThenBy(key => key, PivotKeyComparer.Instance).ToList();
        }

        return keys.Order(PivotKeyComparer.Instance).ToList();
    }

    private static string GroupKeyText(ScalarValue value, PivotFieldModel field) =>
        GroupKeyText(value, field.Grouping, field.GroupStart, field.GroupEnd, field.GroupInterval);

    private static string GroupKeyText(ScalarValue value, PivotFieldGrouping grouping) =>
        GroupKeyText(value, grouping, null, null, null);

    private static string GroupKeyText(ScalarValue value, PivotFieldGrouping grouping, double? groupStart, double? groupEnd, double? groupInterval)
    {
        if (grouping == PivotFieldGrouping.None)
            return KeyText(value);

        if (grouping == PivotFieldGrouping.NumberRange)
            return NumberRangeKeyText(value, groupStart ?? 0, groupInterval ?? 1);

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

    private static string NumberRangeKeyText(ScalarValue value, double start, double interval)
    {
        if (interval <= 0)
            interval = 1;
        var number = Number(value);
        var bucketStart = start + Math.Floor((number - start) / interval) * interval;
        var bucketEnd = bucketStart + interval - 1;
        return $"{bucketStart:0.########}-{bucketEnd:0.########}";
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
                var comparison = StringComparer.CurrentCultureIgnoreCase.Compare(x.Values[index], y.Values[index]);
                if (comparison != 0)
                    return comparison;
            }

            return x.Values.Count.CompareTo(y.Values.Count);
        }
    }

}
