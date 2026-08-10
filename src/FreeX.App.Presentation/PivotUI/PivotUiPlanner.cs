using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

public sealed record PivotFieldListPanePlan(PivotTableModel? PivotTable)
{
    public bool ShouldShow => PivotTable is not null;
}

public sealed record PivotShowDetailsTarget(string PivotTableName, CellAddress PivotCell);

/// <summary>
/// Area-scoped Pivot field selection plus detached layout lists for the next filter command.
/// </summary>
public sealed record PivotFieldSelectionState(
    PivotHeaderArea Area,
    int SourceFieldIndex,
    IReadOnlyList<string> SelectedItems,
    bool HasStoredSelection,
    IReadOnlyList<PivotFieldModel> RowFields,
    IReadOnlyList<PivotFieldModel> ColumnFields,
    IReadOnlyList<PivotFieldModel> PageFields)
{
    public PivotFieldSelectionState WithSelectedItems(IReadOnlyList<string>? selectedItems)
    {
        var selection = HasTargetField() && selectedItems is { Count: > 0 }
            ? selectedItems.ToList()
            : null;
        return this with
        {
            SelectedItems = selection ?? [],
            HasStoredSelection = selection is { Count: > 0 },
            RowFields = UpdateFields(RowFields, PivotHeaderArea.Row, selection),
            ColumnFields = UpdateFields(ColumnFields, PivotHeaderArea.Column, selection),
            PageFields = UpdateFields(PageFields, PivotHeaderArea.Page, selection)
        };
    }

    private bool HasTargetField() =>
        Area switch
        {
            PivotHeaderArea.Row => RowFields.Any(IsTargetField),
            PivotHeaderArea.Column => ColumnFields.Any(IsTargetField),
            PivotHeaderArea.Page => PageFields.Any(IsTargetField),
            _ => false
        };

    private bool IsTargetField(PivotFieldModel field) => field.SourceFieldIndex == SourceFieldIndex;

    private IReadOnlyList<PivotFieldModel> UpdateFields(
        IReadOnlyList<PivotFieldModel> fields,
        PivotHeaderArea area,
        IReadOnlyList<string>? selectedItems) =>
        Area != area
            ? fields
            : fields
                .Select(field => field.SourceFieldIndex == SourceFieldIndex
                    ? field with
                    {
                        SelectedItem = selectedItems is { Count: 1 } ? selectedItems[0] : null,
                        SelectedItems = selectedItems
                    }
                    : field)
                .ToList();
}

/// <summary>
/// UI-free PivotTable interaction planning shared by desktop hosts. Renderers own controls and localized
/// labels; this planner owns pivot lookup, field captions, filter parsing, layout mutation helpers, and
/// selection reconciliation.
/// </summary>
public static class PivotUiPlanner
{
    public static string FieldCaption(IReadOnlyList<string> headers, int sourceFieldIndex) =>
        PivotFieldListPaneBuilder.FieldCaption(headers, sourceFieldIndex);

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
        => PivotCreatePlanner.ChooseDefaultDataField(sheet, sourceRange);

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
        => PivotCreatePlanner.SuggestName(sheet);

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

    public static PivotFieldSelectionState CreateFieldSelectionState(
        PivotTableModel pivotTable,
        PivotHeaderArea area,
        int sourceFieldIndex)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);

        var rowFields = pivotTable.RowFields.ToList();
        var columnFields = pivotTable.ColumnFields.ToList();
        var pageFields = pivotTable.PageFields.ToList();
        var fields = area switch
        {
            PivotHeaderArea.Row => rowFields,
            PivotHeaderArea.Column => columnFields,
            PivotHeaderArea.Page => pageFields,
            _ => []
        };
        var field = FindFirstLayoutField(fields, sourceFieldIndex);
        var selectedItems = field?.SelectedItems is { Count: > 0 } items
            ? items.ToList()
            : PivotFieldFilterSummary.IsExplicitSelection(field?.SelectedItem)
                ? [field!.SelectedItem!]
                : [];

        return new PivotFieldSelectionState(
            area,
            sourceFieldIndex,
            selectedItems,
            field?.SelectedItems is { Count: > 0 } || !string.IsNullOrWhiteSpace(field?.SelectedItem),
            rowFields,
            columnFields,
            pageFields);
    }

    public static bool FieldListCaptionMatchesSearch(string caption, string? searchText)
    {
        var needle = searchText?.Trim();
        return string.IsNullOrEmpty(needle) ||
            caption.Contains(needle, StringComparison.OrdinalIgnoreCase);
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
