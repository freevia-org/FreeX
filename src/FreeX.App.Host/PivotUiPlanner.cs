using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record PivotFieldListItem(string Caption, bool IsChecked);

public sealed record PendingPivotLayoutUpdate(
    bool IsDeferred,
    string? AvailableFieldsSearchText,
    IReadOnlyList<PivotFieldListItem> Fields);

public sealed record PivotFieldListPanePlan(PivotTableModel? PivotTable)
{
    public bool ShouldShow => PivotTable is not null;
}

public sealed record PivotShowDetailsTarget(string PivotTableName, CellAddress PivotCell);

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
            if (CaptionEquals(headers[index], caption))
                return index;
        }

        return null;
    }

    public static int? FindDataFieldIndex(PivotTableModel pivotTable, string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
            return null;

        return FindDataFieldIndexByCaption(pivotTable, caption);
    }

    public static int? FindFieldSourceIndex(IReadOnlyList<string> headers, PivotTableModel pivotTable, string caption)
    {
        var sourceIndex = FindSourceFieldIndex(headers, caption);
        if (sourceIndex is not null)
            return sourceIndex;

        return FindDataFieldByCaption(pivotTable, caption)?.SourceFieldIndex;
    }

    public static PivotTableModel? FindPivotTableForSelection(Sheet sheet, GridRange? selectedRange)
    {
        var pivotTable = FindPivotTableContainingSelection(sheet, selectedRange);
        if (pivotTable is not null)
            return pivotTable;

        return FindFirstPivotTable(sheet);
    }

    public static PivotTableModel? FindPivotTableContainingSelection(Sheet sheet, GridRange? selectedRange)
    {
        if (selectedRange is not { } range)
            return null;

        return FindPivotTableIntersectingSelection(sheet, range);
    }

    public static PivotTableModel? FindPivotTableContainingCell(Sheet sheet, CellAddress cell) =>
        FindFirstPivotTable(sheet, pivotTable => PivotTableContainsCell(pivotTable, cell));

    public static GridRange VisiblePivotRange(PivotTableModel pivotTable) =>
        pivotTable.LastRenderedRange is { } renderedRange &&
        renderedRange.Start.Sheet == pivotTable.TargetRange.Start.Sheet
            ? renderedRange
            : pivotTable.TargetRange;

    public static PivotFieldListPanePlan CreateFieldListPanePlan(Sheet? sheet, GridRange? selectedRange)
    {
        if (sheet is null || selectedRange is not { } range)
            return new PivotFieldListPanePlan(null);

        return new PivotFieldListPanePlan(FindPivotTableContainingCell(sheet, range.Start));
    }

    public static CellAddress? ReconcileSelectionAfterPivotResize(
        GridRange previousVisibleRange,
        GridRange updatedVisibleRange,
        GridRange? selectedRange)
    {
        if (selectedRange is not { } range)
            return updatedVisibleRange.Start;

        var activeCell = range.Start;
        if (updatedVisibleRange.Contains(activeCell))
            return null;
        if (!previousVisibleRange.Contains(activeCell))
            return null;

        if (previousVisibleRange.Start.Sheet != updatedVisibleRange.Start.Sheet)
            return updatedVisibleRange.Start;

        var rowOffset = activeCell.Row - previousVisibleRange.Start.Row;
        var colOffset = activeCell.Col - previousVisibleRange.Start.Col;
        return new CellAddress(
            updatedVisibleRange.Start.Sheet,
            updatedVisibleRange.Start.Row + Math.Min(rowOffset, updatedVisibleRange.RowCount - 1),
            updatedVisibleRange.Start.Col + Math.Min(colOffset, updatedVisibleRange.ColCount - 1));
    }

    public static PivotShowDetailsTarget? ResolveShowDetailsTarget(Sheet? sheet, GridRange? selectedRange)
    {
        if (sheet is null || selectedRange is not { } range)
            return null;

        var pivotTable = FindPivotTableContainingCell(sheet, range.Start);
        return pivotTable is null
            ? null
            : new PivotShowDetailsTarget(pivotTable.Name, range.Start);
    }

    public static Sheet ResolvePivotSourceSheet(Workbook workbook, Sheet fallbackSheet, PivotTableModel pivotTable) =>
        workbook.GetSheet(pivotTable.SourceRange.Start.Sheet) ?? fallbackSheet;

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
            if (sheet.PivotTables.All(pivot => !PivotTableNameEquals(pivot, name)))
                return name;
        }

        return $"PivotTable{Guid.NewGuid():N}"[..31];
    }

    public static string NormalizePivotTableName(string? name) => name?.Trim() ?? string.Empty;

    public static bool IsPivotTableNameAvailable(Workbook workbook, PivotTableModel targetPivotTable, string name)
    {
        var normalized = NormalizePivotTableName(name);
        if (normalized.Length == 0)
            return false;

        return workbook.Sheets
            .SelectMany(sheet => sheet.PivotTables)
            .All(pivot => ReferenceEquals(pivot, targetPivotTable) ||
                          !PivotTableNameEquals(pivot, normalized));
    }

    public static GridRange ResolvePivotTableSelectionRange(PivotTableModel pivotTable) => pivotTable.TargetRange;

    public static bool TryCreateMovedTargetRange(
        PivotTableModel pivotTable,
        CellAddress targetStart,
        out GridRange targetRange)
    {
        var rowCount = pivotTable.TargetRange.RowCount;
        var colCount = pivotTable.TargetRange.ColCount;
        if (targetStart.Row > CellAddress.MaxRow - rowCount + 1 ||
            targetStart.Col > CellAddress.MaxCol - colCount + 1)
        {
            targetRange = default;
            return false;
        }

        targetRange = new GridRange(
            targetStart,
            new CellAddress(
                targetStart.Sheet,
                targetStart.Row + rowCount - 1,
                targetStart.Col + colCount - 1));
        return true;
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
        if (FieldButtonEquals(fieldButton, "Values"))
            return FindFirstDataField(pivotTable)?.Name;

        if (FieldButtonEquals(fieldButton, "Axis Fields"))
        {
            var field = FindFirstAxisField(pivotTable);
            return field is null ? null : FieldCaption(headers, field);
        }

        var pageField = FindFirstPageField(pivotTable);
        if (pageField is not null)
            return FieldCaption(headers, pageField);

        var axisField = FindFirstAxisField(pivotTable);
        return axisField is null ? FindFirstDataField(pivotTable)?.Name : FieldCaption(headers, axisField);
    }

    public static PivotFieldModel FindExistingPivotField(PivotTableModel pivotTable, int sourceFieldIndex) =>
        FindFirstLayoutField(pivotTable, sourceFieldIndex) ?? new PivotFieldModel(sourceFieldIndex);

    public static List<PivotFieldModel> SetFieldSelectedItems(
        IReadOnlyList<PivotFieldModel> fields,
        int sourceFieldIndex,
        IReadOnlyList<string>? selectedItems) =>
        fields
            .Select(field => SourceFieldIndexEquals(field, sourceFieldIndex)
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

    private static bool CaptionEquals(string value, string caption) =>
        string.Equals(value, caption, StringComparison.CurrentCultureIgnoreCase);

    private static bool DataFieldCaptionEquals(PivotDataFieldModel field, string caption) =>
        CaptionEquals(field.Name, caption);

    private static int? FindDataFieldIndexByCaption(PivotTableModel pivotTable, string caption) =>
        FindDataFieldIndexBy(pivotTable, field => DataFieldCaptionEquals(field, caption));

    private static int? FindDataFieldIndexBy(
        PivotTableModel pivotTable,
        Func<PivotDataFieldModel, bool> predicate)
    {
        for (var index = 0; index < pivotTable.DataFields.Count; index++)
        {
            if (predicate(pivotTable.DataFields[index]))
                return index;
        }

        return null;
    }

    private static PivotDataFieldModel? FindDataFieldByCaption(PivotTableModel pivotTable, string caption) =>
        FindFirstDataField(pivotTable, field => DataFieldCaptionEquals(field, caption));

    private static PivotTableModel? FindFirstPivotTable(Sheet sheet) =>
        sheet.PivotTables.Count == 0 ? null : sheet.PivotTables[0];

    private static PivotTableModel? FindFirstPivotTable(
        Sheet sheet,
        Func<PivotTableModel, bool> predicate)
    {
        foreach (var pivotTable in sheet.PivotTables)
        {
            if (predicate(pivotTable))
                return pivotTable;
        }

        return null;
    }

    private static PivotTableModel? FindPivotTableIntersectingSelection(Sheet sheet, GridRange range) =>
        FindFirstPivotTable(sheet, pivotTable => PivotTableIntersectsSelection(pivotTable, range));

    private static bool PivotTableIntersectsSelection(PivotTableModel pivotTable, GridRange range) =>
        PivotTableContainsCell(pivotTable, range.Start) || VisiblePivotRange(pivotTable).Overlaps(range);

    private static bool PivotTableContainsCell(PivotTableModel pivotTable, CellAddress cell) =>
        VisiblePivotRange(pivotTable).Contains(cell);

    private static bool PivotTableNameEquals(PivotTableModel pivotTable, string name) =>
        string.Equals(pivotTable.Name, name, StringComparison.OrdinalIgnoreCase);

    private static bool FieldButtonEquals(string fieldButton, string caption) =>
        string.Equals(fieldButton, caption, StringComparison.OrdinalIgnoreCase);

    private static PivotDataFieldModel? FindFirstDataField(PivotTableModel pivotTable) =>
        pivotTable.DataFields.Count == 0 ? null : pivotTable.DataFields[0];

    private static PivotDataFieldModel? FindFirstDataField(
        PivotTableModel pivotTable,
        Func<PivotDataFieldModel, bool> predicate)
    {
        foreach (var field in pivotTable.DataFields)
        {
            if (predicate(field))
                return field;
        }

        return null;
    }

    private static PivotFieldModel? FindFirstPageField(PivotTableModel pivotTable) =>
        pivotTable.PageFields.Count == 0 ? null : pivotTable.PageFields[0];

    private static PivotFieldModel? FindFirstAxisField(PivotTableModel pivotTable)
    {
        if (pivotTable.RowFields.Count > 0)
            return pivotTable.RowFields[0];

        return pivotTable.ColumnFields.Count == 0 ? null : pivotTable.ColumnFields[0];
    }

    private static string FieldCaption(IReadOnlyList<string> headers, PivotFieldModel field) =>
        FieldCaption(headers, field.SourceFieldIndex);

    private static PivotFieldModel? FindFirstLayoutField(PivotTableModel pivotTable, int sourceFieldIndex) =>
        FindFirstLayoutField(pivotTable.RowFields, sourceFieldIndex) ??
        FindFirstLayoutField(pivotTable.ColumnFields, sourceFieldIndex) ??
        FindFirstLayoutField(pivotTable.PageFields, sourceFieldIndex);

    private static PivotFieldModel? FindFirstLayoutField(
        IReadOnlyList<PivotFieldModel> fields,
        int sourceFieldIndex)
    {
        foreach (var field in fields)
        {
            if (SourceFieldIndexEquals(field, sourceFieldIndex))
                return field;
        }

        return null;
    }

    private static bool SourceFieldIndexEquals(PivotFieldModel field, int sourceFieldIndex) =>
        field.SourceFieldIndex == sourceFieldIndex;
}
