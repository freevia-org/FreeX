using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record PivotFieldListItem(string Caption, bool IsChecked);

public sealed record PendingPivotLayoutUpdate(
    bool IsDeferred,
    string? AvailableFieldsSearchText,
    IReadOnlyList<PivotFieldListItem> Fields);

public static class PivotUiPlanner
{
    public static string FieldCaption(IReadOnlyList<string> headers, int sourceFieldIndex) =>
        sourceFieldIndex >= 0 && sourceFieldIndex < headers.Count
            ? headers[sourceFieldIndex]
            : $"Column {sourceFieldIndex + 1}";

    public static int? FindSourceFieldIndex(IReadOnlyList<string> headers, string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
            return null;

        for (var index = 0; index < headers.Count; index++)
        {
            if (string.Equals(headers[index], caption, StringComparison.CurrentCultureIgnoreCase))
                return index;
        }

        return null;
    }

    public static int? FindDataFieldIndex(PivotTableModel pivotTable, string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
            return null;

        for (var index = 0; index < pivotTable.DataFields.Count; index++)
        {
            if (string.Equals(pivotTable.DataFields[index].Name, caption, StringComparison.CurrentCultureIgnoreCase))
                return index;
        }

        return null;
    }

    public static int? FindFieldSourceIndex(IReadOnlyList<string> headers, PivotTableModel pivotTable, string caption)
    {
        var sourceIndex = FindSourceFieldIndex(headers, caption);
        if (sourceIndex is not null)
            return sourceIndex;

        return pivotTable.DataFields
            .FirstOrDefault(field => string.Equals(field.Name, caption, StringComparison.CurrentCultureIgnoreCase))
            ?.SourceFieldIndex;
    }

    public static PivotTableModel? FindPivotTableForSelection(Sheet sheet, GridRange? selectedRange)
    {
        var pivotTable = FindPivotTableContainingSelection(sheet, selectedRange);
        if (pivotTable is not null)
            return pivotTable;

        return sheet.PivotTables.FirstOrDefault();
    }

    public static PivotTableModel? FindPivotTableContainingSelection(Sheet sheet, GridRange? selectedRange)
    {
        if (selectedRange is not { } range)
            return null;

        return sheet.PivotTables.FirstOrDefault(pivot =>
            pivot.TargetRange.Contains(range.Start) || pivot.TargetRange.Overlaps(range));
    }

    public static int ChooseDefaultDataField(Sheet sheet, GridRange sourceRange)
    {
        for (var col = sourceRange.Start.Col; col <= sourceRange.End.Col; col++)
        {
            for (var row = sourceRange.Start.Row + 1; row <= sourceRange.End.Row; row++)
            {
                if (sheet.GetValue(row, col) is NumberValue or DateTimeValue)
                    return checked((int)(col - sourceRange.Start.Col));
            }
        }

        return checked((int)Math.Min(1, sourceRange.ColCount - 1));
    }

    public static GridRange DefaultTargetRange(Sheet sheet, GridRange sourceRange)
    {
        var start = new CellAddress(
            sheet.Id,
            sourceRange.Start.Row,
            Math.Min(sourceRange.End.Col + 2, CellAddress.MaxCol));
        var end = new CellAddress(
            sheet.Id,
            Math.Min(start.Row + sourceRange.RowCount + 2, CellAddress.MaxRow),
            Math.Min(start.Col + sourceRange.ColCount + 2, CellAddress.MaxCol));
        return new GridRange(start, end);
    }

    public static string GenerateUniquePivotTableName(Sheet sheet)
    {
        for (var index = sheet.PivotTables.Count + 1; index <= 10000; index++)
        {
            var name = $"PivotTable{index}";
            if (sheet.PivotTables.All(pivot => !string.Equals(pivot.Name, name, StringComparison.OrdinalIgnoreCase)))
                return name;
        }

        return $"PivotTable{Guid.NewGuid():N}"[..31];
    }

    public static string UnquoteSheetName(string sheetName)
    {
        if (sheetName.Length >= 2 && sheetName[0] == '\'' && sheetName[^1] == '\'')
            return sheetName[1..^1].Replace("''", "'", StringComparison.Ordinal);

        return sheetName;
    }

    public static string QuoteSheetNameForReference(string sheetName)
    {
        if (sheetName.All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
            return sheetName;

        return $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    public static PivotDataFieldModel CreateDefaultDataField(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        int sourceFieldIndex)
    {
        var caption = FieldCaption(headers, sourceFieldIndex);
        var summaryFunction = IsNumericSourceField(sheet, pivotTable, sourceFieldIndex) ? "sum" : "count";
        var displayName = summaryFunction == "sum" ? $"Sum of {caption}" : $"Count of {caption}";
        return new PivotDataFieldModel(sourceFieldIndex, displayName, summaryFunction);
    }

    public static bool IsNumericSourceField(Sheet sheet, PivotTableModel pivotTable, int sourceFieldIndex)
    {
        var sourceColumn = pivotTable.SourceRange.Start.Col + (uint)sourceFieldIndex;
        for (var row = pivotTable.SourceRange.Start.Row + 1; row <= pivotTable.SourceRange.End.Row; row++)
        {
            if (sheet.GetValue(row, sourceColumn) is NumberValue or DateTimeValue)
                return true;
        }

        return false;
    }

    public static bool TryParseLabelFilter(string input, int sourceFieldIndex, out PivotLabelFilterModel filter)
    {
        filter = new PivotLabelFilterModel(sourceFieldIndex, PivotLabelFilterKind.Contains, "");
        var normalized = input.Trim();
        if (normalized.StartsWith("<>", StringComparison.Ordinal))
        {
            filter = new PivotLabelFilterModel(sourceFieldIndex, PivotLabelFilterKind.DoesNotEqual, normalized[2..].Trim());
            return !string.IsNullOrWhiteSpace(filter.Value);
        }

        var parts = normalized.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1]))
            return false;

        var kind = parts[0].ToLowerInvariant() switch
        {
            "equals" or "=" => PivotLabelFilterKind.Equals,
            "notequals" or "not" or "<>" => PivotLabelFilterKind.DoesNotEqual,
            "begins" or "beginswith" => PivotLabelFilterKind.BeginsWith,
            "ends" or "endswith" => PivotLabelFilterKind.EndsWith,
            "contains" => PivotLabelFilterKind.Contains,
            "notcontains" => PivotLabelFilterKind.DoesNotContain,
            _ => PivotLabelFilterKind.Contains
        };
        filter = new PivotLabelFilterModel(sourceFieldIndex, kind, parts[1]);
        return true;
    }

    public static bool TryParseValueFilter(string input, int sourceFieldIndex, out PivotValueFilterModel filter)
    {
        filter = new PivotValueFilterModel(0, PivotValueFilterKind.GreaterThan, SourceFieldIndex: sourceFieldIndex);
        var normalized = input.Trim();
        if (TryParseTopBottomValueFilter(normalized, sourceFieldIndex, out filter))
            return true;

        var operators = new[]
        {
            (Text: ">=", Kind: PivotValueFilterKind.GreaterThanOrEqual),
            (Text: "<=", Kind: PivotValueFilterKind.LessThanOrEqual),
            (Text: "<>", Kind: PivotValueFilterKind.DoesNotEqual),
            (Text: ">", Kind: PivotValueFilterKind.GreaterThan),
            (Text: "<", Kind: PivotValueFilterKind.LessThan),
            (Text: "=", Kind: PivotValueFilterKind.Equals)
        };
        foreach (var op in operators)
        {
            if (!normalized.StartsWith(op.Text, StringComparison.Ordinal))
                continue;

            if (!double.TryParse(
                    normalized[op.Text.Length..].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
            {
                return false;
            }

            filter = new PivotValueFilterModel(0, op.Kind, ComparisonValue: value, SourceFieldIndex: sourceFieldIndex);
            return true;
        }

        return false;
    }

    private static bool TryParseTopBottomValueFilter(string input, int sourceFieldIndex, out PivotValueFilterModel filter)
    {
        filter = new PivotValueFilterModel(0, PivotValueFilterKind.Top, SourceFieldIndex: sourceFieldIndex);
        var parts = input.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var count) ||
            count <= 0)
        {
            return false;
        }

        var kind = parts[0].ToLowerInvariant() switch
        {
            "top" => PivotValueFilterKind.Top,
            "bottom" => PivotValueFilterKind.Bottom,
            _ => (PivotValueFilterKind?)null
        };
        if (kind is null)
            return false;

        filter = new PivotValueFilterModel(0, kind.Value, Count: count, SourceFieldIndex: sourceFieldIndex);
        return true;
    }

    public static string? ResolvePivotChartFieldButtonCaption(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        string fieldButton)
    {
        if (string.Equals(fieldButton, "Values", StringComparison.OrdinalIgnoreCase))
            return pivotTable.DataFields.FirstOrDefault()?.Name;

        if (string.Equals(fieldButton, "Axis Fields", StringComparison.OrdinalIgnoreCase))
        {
            var field = pivotTable.RowFields.Concat(pivotTable.ColumnFields).FirstOrDefault();
            return field is null ? null : FieldCaption(headers, field.SourceFieldIndex);
        }

        var pageField = pivotTable.PageFields.FirstOrDefault();
        if (pageField is not null)
            return FieldCaption(headers, pageField.SourceFieldIndex);

        var axisField = pivotTable.RowFields.Concat(pivotTable.ColumnFields).FirstOrDefault();
        return axisField is null ? pivotTable.DataFields.FirstOrDefault()?.Name : FieldCaption(headers, axisField.SourceFieldIndex);
    }

    public static PivotFieldModel FindExistingPivotField(PivotTableModel pivotTable, int sourceFieldIndex) =>
        pivotTable.RowFields
            .Concat(pivotTable.ColumnFields)
            .Concat(pivotTable.PageFields)
            .FirstOrDefault(field => field.SourceFieldIndex == sourceFieldIndex)
        ?? new PivotFieldModel(sourceFieldIndex);

    public static List<PivotFieldModel> SetFieldSelectedItems(
        IReadOnlyList<PivotFieldModel> fields,
        int sourceFieldIndex,
        IReadOnlyList<string>? selectedItems) =>
        fields
            .Select(field => field.SourceFieldIndex == sourceFieldIndex
                ? field with
                {
                    SelectedItem = selectedItems is { Count: 1 } ? selectedItems[0] : null,
                    SelectedItems = selectedItems
                }
                : field)
            .ToList();

    public static string? GetFieldListCaption(object? item) =>
        item switch
        {
            string value when !string.IsNullOrWhiteSpace(value) => value,
            PivotFieldListItem field when !string.IsNullOrWhiteSpace(field.Caption) => field.Caption,
            _ => null
        };

    public static IReadOnlyList<PivotFieldListItem> FilterPivotFieldListItems(
        IEnumerable<PivotFieldListItem> fields,
        string? searchText)
    {
        var needle = searchText?.Trim();
        if (string.IsNullOrEmpty(needle))
            return fields.ToList();

        return fields
            .Where(field => field.Caption.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static void InsertOrAppend<T>(List<T> items, T item, int index)
    {
        if (index < 0 || index > items.Count)
            items.Add(item);
        else
            items.Insert(index, item);
    }
}
