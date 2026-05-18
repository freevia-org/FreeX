using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static class PivotTableRefreshService
{
    public sealed record PivotDetailRows(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<ScalarValue>> Rows);

    public static void Refresh(Workbook workbook, Sheet targetSheet, PivotTableModel pivotTable)
    {
        var sourceSheet = workbook.GetSheet(pivotTable.SourceRange.Start.Sheet);
        if (sourceSheet is null || pivotTable.RowFields.Count == 0 || pivotTable.DataFields.Count == 0)
            return;

        ClearTargetRange(targetSheet, pivotTable.TargetRange);

        var headers = ReadHeaders(sourceSheet, pivotTable.SourceRange);
        var columnField = pivotTable.ColumnFields.FirstOrDefault();
        if (!pivotTable.RowFields.All(field => IsValidField(field.SourceFieldIndex, headers.Count)) ||
            !pivotTable.PageFields.All(field => IsValidField(field.SourceFieldIndex, headers.Count)) ||
            (columnField is not null && !IsValidField(columnField.SourceFieldIndex, headers.Count)) ||
            !pivotTable.DataFields.All(field => IsValidDataField(field, pivotTable, headers.Count)))
        {
            return;
        }

        var rows = ReadSourceRows(sourceSheet, pivotTable.SourceRange, headers.Count)
            .Where(row => MatchesPageFilters(row, pivotTable.PageFields))
            .ToList();
        if (columnField is not null)
            WriteMatrixPivot(targetSheet, pivotTable, headers, rows, columnField);
        else
            WriteRowPivot(targetSheet, pivotTable, headers, rows);
    }

    public static PivotDetailRows ExtractDetailRows(
        Workbook workbook,
        Sheet targetSheet,
        PivotTableModel pivotTable,
        CellAddress pivotCell)
    {
        var sourceSheet = workbook.GetSheet(pivotTable.SourceRange.Start.Sheet);
        if (sourceSheet is null || !pivotTable.TargetRange.Contains(pivotCell))
            return new PivotDetailRows([], []);

        var headers = ReadHeaders(sourceSheet, pivotTable.SourceRange);
        var outputRow = pivotCell.Row;
        if (outputRow <= pivotTable.TargetRange.Start.Row)
            return new PivotDetailRows(headers, []);

        var rowFields = pivotTable.RowFields.ToList();
        var keys = new List<string>();
        for (var index = 0; index < rowFields.Count; index++)
        {
            var value = targetSheet.GetCell(outputRow, pivotTable.TargetRange.Start.Col + (uint)index)?.Value;
            if (value is null)
                return new PivotDetailRows(headers, []);
            var key = KeyText(value);
            if (key.EndsWith(" Total", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "Grand Total", StringComparison.OrdinalIgnoreCase))
                return new PivotDetailRows(headers, []);
            keys.Add(key);
        }

        var rows = ReadSourceRows(sourceSheet, pivotTable.SourceRange, headers.Count)
            .Where(row => MatchesPageFilters(row, pivotTable.PageFields))
            .Where(row => rowFields.Select(field => GroupKeyText(row[field.SourceFieldIndex], field)).SequenceEqual(keys, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
        return new PivotDetailRows(headers, rows);
    }

    public static GridRange GetMaterializedOutputRange(Sheet sheet, PivotTableModel pivotTable)
    {
        uint? minRow = null;
        uint? minCol = null;
        uint? maxRow = null;
        uint? maxCol = null;

        for (var row = pivotTable.TargetRange.Start.Row; row <= pivotTable.TargetRange.End.Row; row++)
        for (var col = pivotTable.TargetRange.Start.Col; col <= pivotTable.TargetRange.End.Col; col++)
        {
            if (sheet.GetCell(row, col) is null)
                continue;

            minRow = minRow is null ? row : Math.Min(minRow.Value, row);
            minCol = minCol is null ? col : Math.Min(minCol.Value, col);
            maxRow = maxRow is null ? row : Math.Max(maxRow.Value, row);
            maxCol = maxCol is null ? col : Math.Max(maxCol.Value, col);
        }

        if (minRow is null || minCol is null || maxRow is null || maxCol is null)
            return new GridRange(pivotTable.TargetRange.Start, pivotTable.TargetRange.Start);

        return new GridRange(
            new CellAddress(sheet.Id, minRow.Value, minCol.Value),
            new CellAddress(sheet.Id, maxRow.Value, maxCol.Value));
    }

    private static void WriteRowPivot(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows)
    {
        var start = pivotTable.TargetRange.Start;
        var rowFields = pivotTable.RowFields.ToList();
        for (var index = 0; index < rowFields.Count; index++)
            sheet.SetCell(new CellAddress(sheet.Id, start.Row, start.Col + (uint)index), new TextValue(headers[rowFields[index].SourceFieldIndex]));
        for (var index = 0; index < pivotTable.DataFields.Count; index++)
            sheet.SetCell(new CellAddress(sheet.Id, start.Row, start.Col + (uint)rowFields.Count + (uint)index), new TextValue(pivotTable.DataFields[index].Name));

        var groups = rows
            .GroupBy(row => new PivotKey(rowFields.Select(field => GroupKeyText(row[field.SourceFieldIndex], field)).ToArray()))
            .ToList();
        groups = ApplyLabelFilters(groups, pivotTable, rowFields);
        groups = ApplyValueFilters(groups, pivotTable, headers);
        groups = ApplySorts(groups, pivotTable, headers);
        var retainedRows = groups.SelectMany(group => group).ToList();
        var outputRow = start.Row + 1;
        PivotKey? currentSubtotalKey = null;
        var subtotalRows = new List<IReadOnlyList<ScalarValue>>();
        var calculatedItemTotals = new double[pivotTable.DataFields.Count];
        foreach (var group in groups)
        {
            if (pivotTable.ShowSubtotals && rowFields.Count > 1)
            {
                var subtotalKey = new PivotKey(group.Key.Values.Take(rowFields.Count - 1).ToArray());
                if (currentSubtotalKey is not null && !currentSubtotalKey.Equals(subtotalKey))
                {
                    WriteSubtotalRow(sheet, pivotTable, headers, start, rowFields.Count, currentSubtotalKey, subtotalRows, outputRow);
                    outputRow++;
                    subtotalRows.Clear();
                }

                currentSubtotalKey = subtotalKey;
                subtotalRows.AddRange(group);
            }

            for (var index = 0; index < group.Key.Values.Count; index++)
                sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col + (uint)index), new TextValue(group.Key.Values[index]));
            for (var index = 0; index < pivotTable.DataFields.Count; index++)
                sheet.SetCell(
                    new CellAddress(sheet.Id, outputRow, start.Col + (uint)rowFields.Count + (uint)index),
                    new NumberValue(Aggregate(group, pivotTable.DataFields[index], pivotTable, headers)));
            outputRow++;
        }
        if (rowFields.Count == 1)
        {
            foreach (var calculatedItem in pivotTable.CalculatedItems
                         .Where(item => item.SourceFieldIndex == rowFields[0].SourceFieldIndex)
                         .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue(calculatedItem.Name));
                for (var index = 0; index < pivotTable.DataFields.Count; index++)
                {
                    var calculatedValue = EvaluateCalculatedItem(calculatedItem.Formula, groups, pivotTable.DataFields[index], pivotTable, headers);
                    sheet.SetCell(
                        new CellAddress(sheet.Id, outputRow, start.Col + 1 + (uint)index),
                        new NumberValue(calculatedValue));
                    calculatedItemTotals[index] += calculatedValue;
                }

                outputRow++;
            }
        }
        if (pivotTable.ShowSubtotals && rowFields.Count > 1 && currentSubtotalKey is not null)
        {
            WriteSubtotalRow(sheet, pivotTable, headers, start, rowFields.Count, currentSubtotalKey, subtotalRows, outputRow);
            outputRow++;
        }

        sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue("Grand Total"));
        for (var index = 0; index < pivotTable.DataFields.Count; index++)
            sheet.SetCell(
                new CellAddress(sheet.Id, outputRow, start.Col + (uint)rowFields.Count + (uint)index),
                new NumberValue(Aggregate(retainedRows, pivotTable.DataFields[index], pivotTable, headers) + calculatedItemTotals[index]));
    }

    private static void WriteSubtotalRow(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        CellAddress start,
        int rowFieldCount,
        PivotKey subtotalKey,
        IReadOnlyList<IReadOnlyList<ScalarValue>> subtotalRows,
        uint outputRow)
    {
        sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue($"{subtotalKey.Values[0]} Total"));
        for (var index = 0; index < pivotTable.DataFields.Count; index++)
            sheet.SetCell(
                new CellAddress(sheet.Id, outputRow, start.Col + (uint)rowFieldCount + (uint)index),
                new NumberValue(Aggregate(subtotalRows, pivotTable.DataFields[index], pivotTable, headers)));
    }

    private static void WriteMatrixPivot(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows,
        PivotFieldModel columnField)
    {
        var start = pivotTable.TargetRange.Start;
        var rowFields = pivotTable.RowFields.ToList();
        var rowGroups = rows
            .GroupBy(row => new PivotKey(rowFields.Select(field => GroupKeyText(row[field.SourceFieldIndex], field)).ToArray()))
            .ToList();
        rowGroups = ApplyLabelFilters(rowGroups, pivotTable, rowFields);
        rowGroups = ApplyValueFilters(rowGroups, pivotTable, headers);
        rowGroups = ApplySorts(rowGroups, pivotTable, headers);
        var retainedRows = rowGroups.SelectMany(group => group).ToList();
        var columnKeys = retainedRows.Select(row => GroupKeyText(row[columnField.SourceFieldIndex], columnField)).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        var singleDataField = pivotTable.DataFields.Count == 1;

        for (var index = 0; index < rowFields.Count; index++)
            sheet.SetCell(new CellAddress(sheet.Id, start.Row, start.Col + (uint)index), new TextValue(headers[rowFields[index].SourceFieldIndex]));

        var valueStartCol = start.Col + (uint)rowFields.Count;
        var outputColumn = valueStartCol;
        foreach (var columnKey in columnKeys)
        {
            foreach (var dataField in pivotTable.DataFields)
            {
                var caption = singleDataField ? columnKey : $"{columnKey} {dataField.Name}";
                sheet.SetCell(new CellAddress(sheet.Id, start.Row, outputColumn), new TextValue(caption));
                outputColumn++;
            }
        }
        foreach (var dataField in pivotTable.DataFields)
        {
            var caption = singleDataField ? "Grand Total" : $"Grand Total {dataField.Name}";
            sheet.SetCell(new CellAddress(sheet.Id, start.Row, outputColumn), new TextValue(caption));
            outputColumn++;
        }

        var outputRow = start.Row + 1;
        foreach (var rowGroup in rowGroups)
        {
            for (var index = 0; index < rowGroup.Key.Values.Count; index++)
                sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col + (uint)index), new TextValue(rowGroup.Key.Values[index]));

            outputColumn = valueStartCol;
            foreach (var columnKey in columnKeys)
            {
                var columnRows = rowGroup
                    .Where(row => string.Equals(GroupKeyText(row[columnField.SourceFieldIndex], columnField), columnKey, StringComparison.CurrentCultureIgnoreCase))
                    .ToList();
                foreach (var dataField in pivotTable.DataFields)
                {
                    sheet.SetCell(new CellAddress(sheet.Id, outputRow, outputColumn), new NumberValue(Aggregate(columnRows, dataField, pivotTable, headers)));
                    outputColumn++;
                }
            }
            foreach (var dataField in pivotTable.DataFields)
            {
                sheet.SetCell(new CellAddress(sheet.Id, outputRow, outputColumn), new NumberValue(Aggregate(rowGroup, dataField, pivotTable, headers)));
                outputColumn++;
            }
            outputRow++;
        }

        sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue("Grand Total"));
        outputColumn = valueStartCol;
        foreach (var columnKey in columnKeys)
        {
            var columnRows = retainedRows
                .Where(row => string.Equals(GroupKeyText(row[columnField.SourceFieldIndex], columnField), columnKey, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
            foreach (var dataField in pivotTable.DataFields)
            {
                sheet.SetCell(new CellAddress(sheet.Id, outputRow, outputColumn), new NumberValue(Aggregate(columnRows, dataField, pivotTable, headers)));
                outputColumn++;
            }
        }
        foreach (var dataField in pivotTable.DataFields)
        {
                sheet.SetCell(new CellAddress(sheet.Id, outputRow, outputColumn), new NumberValue(Aggregate(retainedRows, dataField, pivotTable, headers)));
            outputColumn++;
        }
    }

    private static IReadOnlyList<string> ReadHeaders(Sheet sheet, GridRange range)
    {
        var headers = new List<string>();
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            var value = sheet.GetCell(range.Start.Row, col)?.Value;
            headers.Add(value is TextValue text && !string.IsNullOrWhiteSpace(text.Value)
                ? text.Value
                : $"Field{headers.Count + 1}");
        }

        return headers;
    }

    private static IEnumerable<IReadOnlyList<ScalarValue>> ReadSourceRows(Sheet sheet, GridRange range, int fieldCount)
    {
        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
        {
            var values = new List<ScalarValue>(fieldCount);
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                values.Add(sheet.GetCell(row, col)?.Value ?? BlankValue.Instance);
            yield return values;
        }
    }

    private static void ClearTargetRange(Sheet sheet, GridRange targetRange)
    {
        for (var row = targetRange.Start.Row; row <= targetRange.End.Row; row++)
        for (var col = targetRange.Start.Col; col <= targetRange.End.Col; col++)
            sheet.ClearCell(row, col);
    }

    private static bool IsValidField(int index, int fieldCount) => index >= 0 && index < fieldCount;

    private static bool IsValidDataField(PivotDataFieldModel field, PivotTableModel pivotTable, int fieldCount) =>
        IsValidField(field.SourceFieldIndex, fieldCount) ||
        (!string.IsNullOrWhiteSpace(field.CalculatedFieldName) &&
         pivotTable.CalculatedFields.Any(calculated =>
             string.Equals(calculated.Name, field.CalculatedFieldName, StringComparison.OrdinalIgnoreCase)));

    private static bool MatchesPageFilters(IReadOnlyList<ScalarValue> row, IReadOnlyList<PivotFieldModel> pageFields)
    {
        foreach (var field in pageFields)
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
        IReadOnlyList<string> headers)
    {
        foreach (var filter in pivotTable.ValueFilters)
        {
            if (filter.DataFieldIndex < 0 ||
                filter.DataFieldIndex >= pivotTable.DataFields.Count)
            {
                continue;
            }
            if ((filter.Kind == PivotValueFilterKind.Top || filter.Kind == PivotValueFilterKind.Bottom) && filter.Count <= 0)
                continue;

            var dataField = pivotTable.DataFields[filter.DataFieldIndex];
            groups = filter.Kind switch
            {
                PivotValueFilterKind.Bottom => groups.OrderBy(group => Aggregate(group, dataField, pivotTable, headers)).Take(filter.Count).ToList(),
                PivotValueFilterKind.Top => groups.OrderByDescending(group => Aggregate(group, dataField, pivotTable, headers)).Take(filter.Count).ToList(),
                PivotValueFilterKind.GreaterThan => groups.Where(group => Aggregate(group, dataField, pivotTable, headers) > (filter.ComparisonValue ?? 0)).ToList(),
                PivotValueFilterKind.GreaterThanOrEqual => groups.Where(group => Aggregate(group, dataField, pivotTable, headers) >= (filter.ComparisonValue ?? 0)).ToList(),
                PivotValueFilterKind.LessThan => groups.Where(group => Aggregate(group, dataField, pivotTable, headers) < (filter.ComparisonValue ?? 0)).ToList(),
                PivotValueFilterKind.LessThanOrEqual => groups.Where(group => Aggregate(group, dataField, pivotTable, headers) <= (filter.ComparisonValue ?? 0)).ToList(),
                PivotValueFilterKind.Equals => groups.Where(group => Math.Abs(Aggregate(group, dataField, pivotTable, headers) - (filter.ComparisonValue ?? 0)) < 0.0000001).ToList(),
                PivotValueFilterKind.DoesNotEqual => groups.Where(group => Math.Abs(Aggregate(group, dataField, pivotTable, headers) - (filter.ComparisonValue ?? 0)) >= 0.0000001).ToList(),
                _ => groups
            };
            groups = groups.OrderBy(group => group.Key, PivotKeyComparer.Instance).ToList();
        }

        return groups;
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
            _ => true
        };
    }

    private static List<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> ApplySorts(
        List<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> groups,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        if (pivotTable.Sorts.Count == 0)
            return groups.OrderBy(group => group.Key, PivotKeyComparer.Instance).ToList();

        var sort = pivotTable.Sorts[^1];
        if (sort.Target == PivotSortTarget.Value &&
            sort.DataFieldIndex >= 0 &&
            sort.DataFieldIndex < pivotTable.DataFields.Count)
        {
            var dataField = pivotTable.DataFields[sort.DataFieldIndex];
            return sort.Direction == PivotSortDirection.Descending
                ? groups.OrderByDescending(group => Aggregate(group, dataField, pivotTable, headers)).ThenBy(group => group.Key, PivotKeyComparer.Instance).ToList()
                : groups.OrderBy(group => Aggregate(group, dataField, pivotTable, headers)).ThenBy(group => group.Key, PivotKeyComparer.Instance).ToList();
        }

        return sort.Direction == PivotSortDirection.Descending
            ? groups.OrderByDescending(group => group.Key, PivotKeyComparer.Instance).ToList()
            : groups.OrderBy(group => group.Key, PivotKeyComparer.Instance).ToList();
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

    private static bool HasNumericValue(ScalarValue value) =>
        value is NumberValue or DateTimeValue or BoolValue;

    private static bool IsNonBlank(ScalarValue value) =>
        value is not BlankValue;

    private static double Aggregate(
        IEnumerable<IReadOnlyList<ScalarValue>> rows,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        var values = rows.Select(row => GetDataFieldValue(row, dataField, pivotTable, headers)).ToList();
        var numericValues = values.Where(HasNumericValue).Select(Number).ToList();
        return dataField.SummaryFunction.Trim().ToLowerInvariant() switch
        {
            "count" => values.Count(IsNonBlank),
            "countnums" or "countNums" => numericValues.Count,
            "average" or "avg" => numericValues.Count == 0 ? 0 : numericValues.Average(),
            "min" => numericValues.Count == 0 ? 0 : numericValues.Min(),
            "max" => numericValues.Count == 0 ? 0 : numericValues.Max(),
            "product" => numericValues.Count == 0 ? 0 : numericValues.Aggregate(1.0, (acc, value) => acc * value),
            _ => numericValues.Sum()
        };
    }

    private static ScalarValue GetDataFieldValue(
        IReadOnlyList<ScalarValue> row,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        if (dataField.SourceFieldIndex >= 0 && dataField.SourceFieldIndex < row.Count)
            return row[dataField.SourceFieldIndex];

        if (!string.IsNullOrWhiteSpace(dataField.CalculatedFieldName))
        {
            var calculated = pivotTable.CalculatedFields.FirstOrDefault(field =>
                string.Equals(field.Name, dataField.CalculatedFieldName, StringComparison.OrdinalIgnoreCase));
            if (calculated is not null)
                return new NumberValue(EvaluateCalculatedField(calculated.Formula, row, headers));
        }

        return BlankValue.Instance;
    }

    private static double EvaluateCalculatedField(
        string formula,
        IReadOnlyList<ScalarValue> row,
        IReadOnlyList<string> headers)
    {
        var parser = new CalculatedFieldExpressionParser(formula, name =>
        {
            var index = headers.ToList().FindIndex(header => string.Equals(header, name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index < row.Count ? Number(row[index]) : 0;
        });
        return parser.Parse();
    }

    private static double EvaluateCalculatedItem(
        string formula,
        IReadOnlyList<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> groups,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        var parser = new CalculatedFieldExpressionParser(formula, name =>
        {
            var group = groups.FirstOrDefault(candidate =>
                candidate.Key.Values.Count > 0 &&
                string.Equals(candidate.Key.Values[0], name, StringComparison.CurrentCultureIgnoreCase));
            return group is null ? 0 : Aggregate(group, dataField, pivotTable, headers);
        });
        return parser.Parse();
    }

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

    private sealed class CalculatedFieldExpressionParser
    {
        private readonly string _text;
        private readonly Func<string, double> _fieldValue;
        private int _position;

        public CalculatedFieldExpressionParser(string text, Func<string, double> fieldValue)
        {
            _text = text ?? "";
            _fieldValue = fieldValue;
        }

        public double Parse()
        {
            var value = ParseAddSubtract();
            SkipWhitespace();
            return value;
        }

        private double ParseAddSubtract()
        {
            var value = ParseMultiplyDivide();
            while (true)
            {
                SkipWhitespace();
                if (TryConsume('+'))
                    value += ParseMultiplyDivide();
                else if (TryConsume('-'))
                    value -= ParseMultiplyDivide();
                else
                    return value;
            }
        }

        private double ParseMultiplyDivide()
        {
            var value = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                if (TryConsume('*'))
                    value *= ParseUnary();
                else if (TryConsume('/'))
                {
                    var denominator = ParseUnary();
                    value = Math.Abs(denominator) < double.Epsilon ? 0 : value / denominator;
                }
                else
                    return value;
            }
        }

        private double ParseUnary()
        {
            SkipWhitespace();
            if (TryConsume('+'))
                return ParseUnary();
            if (TryConsume('-'))
                return -ParseUnary();
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (TryConsume('('))
            {
                var value = ParseAddSubtract();
                TryConsume(')');
                return value;
            }

            if (Peek() == '[')
                return _fieldValue(ReadBracketedIdentifier());
            if (char.IsLetter(Peek()) || Peek() == '_')
                return _fieldValue(ReadIdentifier());
            return ReadNumber();
        }

        private string ReadBracketedIdentifier()
        {
            TryConsume('[');
            var start = _position;
            while (_position < _text.Length && _text[_position] != ']')
                _position++;
            var value = _text[start.._position].Trim();
            TryConsume(']');
            return value;
        }

        private string ReadIdentifier()
        {
            var start = _position;
            while (_position < _text.Length && (char.IsLetterOrDigit(_text[_position]) || _text[_position] == '_' || _text[_position] == ' '))
                _position++;
            return _text[start.._position].Trim();
        }

        private double ReadNumber()
        {
            var start = _position;
            while (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.'))
                _position++;
            return double.TryParse(_text[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }

        private char Peek() => _position < _text.Length ? _text[_position] : '\0';

        private bool TryConsume(char ch)
        {
            SkipWhitespace();
            if (Peek() != ch)
                return false;
            _position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
                _position++;
        }
    }
}
