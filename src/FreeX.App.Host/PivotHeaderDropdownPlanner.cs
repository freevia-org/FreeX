using FreeX.Core.Model;

namespace FreeX.App.Host;

public enum PivotHeaderDropdownAxis
{
    Row,
    Column,
    Page
}

public sealed record PivotHeaderDropdownTarget(
    string PivotTableName,
    string FieldCaption,
    int SourceFieldIndex,
    PivotHeaderDropdownAxis Axis,
    CellAddress HeaderCell,
    bool IsActive);

public static class PivotHeaderDropdownPlanner
{
    public static IReadOnlyList<PivotHeaderDropdownTarget> BuildTargets(Workbook workbook, Sheet sheet)
    {
        if (sheet.PivotTables.Count == 0)
            return [];

        var targets = new List<PivotHeaderDropdownTarget>();
        foreach (var pivotTable in sheet.PivotTables)
            AddTargets(workbook, sheet, pivotTable, targets);

        return targets;
    }

    private static void AddTargets(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        List<PivotHeaderDropdownTarget> targets)
    {
        if (!pivotTable.ShowFieldHeaders)
            return;

        var headers = ReadHeaders(workbook, pivotTable);
        if (headers.Count == 0)
            return;

        AddPageTargets(sheet, pivotTable, headers, targets);
        var bodyStart = GetPivotBodyStart(pivotTable);
        AddRowTargets(sheet, pivotTable, headers, bodyStart, targets);
        AddColumnTargets(sheet, pivotTable, headers, bodyStart, targets);
    }

    private static void AddPageTargets(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        List<PivotHeaderDropdownTarget> targets)
    {
        if (pivotTable.PageFields.Count == 0)
            return;

        var start = pivotTable.TargetRange.Start;
        var wrap = Math.Max(0, pivotTable.PageWrap);
        for (var index = 0; index < pivotTable.PageFields.Count; index++)
        {
            var (rowOffset, colPairOffset) = GetPageFieldOffset(
                index,
                pivotTable.PageFields.Count,
                wrap,
                pivotTable.PageOverThenDown);
            var address = new CellAddress(sheet.Id, start.Row + rowOffset, start.Col + colPairOffset);
            AddTarget(sheet, pivotTable, headers, pivotTable.PageFields[index], PivotHeaderDropdownAxis.Page, address, targets);
        }
    }

    private static void AddRowTargets(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        CellAddress bodyStart,
        List<PivotHeaderDropdownTarget> targets)
    {
        if (pivotTable.RowFields.Count == 0)
            return;

        if (pivotTable.ReportLayout == PivotReportLayout.Compact && pivotTable.RowFields.Count > 1)
        {
            AddTarget(sheet, pivotTable, headers, pivotTable.RowFields[0], PivotHeaderDropdownAxis.Row, bodyStart, targets);
            return;
        }

        for (var index = 0; index < pivotTable.RowFields.Count; index++)
        {
            var address = new CellAddress(sheet.Id, bodyStart.Row, bodyStart.Col + (uint)index);
            AddTarget(sheet, pivotTable, headers, pivotTable.RowFields[index], PivotHeaderDropdownAxis.Row, address, targets);
        }
    }

    private static void AddColumnTargets(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        CellAddress bodyStart,
        List<PivotHeaderDropdownTarget> targets)
    {
        if (pivotTable.ColumnFields.Count == 0)
            return;

        var valueStartCol = bodyStart.Col + (uint)RowFieldOutputColumnCount(pivotTable);
        for (var index = 0; index < pivotTable.ColumnFields.Count; index++)
        {
            var address = new CellAddress(sheet.Id, bodyStart.Row + (uint)index, valueStartCol);
            AddTarget(sheet, pivotTable, headers, pivotTable.ColumnFields[index], PivotHeaderDropdownAxis.Column, address, targets);
        }
    }

    private static void AddTarget(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        PivotFieldModel field,
        PivotHeaderDropdownAxis axis,
        CellAddress address,
        List<PivotHeaderDropdownTarget> targets)
    {
        if (field.ShowDropDowns == false ||
            field.SourceFieldIndex < 0 ||
            field.SourceFieldIndex >= headers.Count ||
            !IsRenderableHeaderCell(sheet, pivotTable, address))
        {
            return;
        }

        targets.Add(new PivotHeaderDropdownTarget(
            pivotTable.Name,
            PivotUiPlanner.FieldCaption(headers, field.SourceFieldIndex),
            field.SourceFieldIndex,
            axis,
            address,
            IsFieldActive(pivotTable, field)));
    }

    private static bool IsRenderableHeaderCell(Sheet sheet, PivotTableModel pivotTable, CellAddress address)
    {
        if (sheet.GetCell(address.Row, address.Col)?.Value is not TextValue text)
            return false;

        return !IsGrandTotalCaption(pivotTable, text.Value);
    }

    private static bool IsFieldActive(PivotTableModel pivotTable, PivotFieldModel field) =>
        HasExplicitSelection(field) ||
        pivotTable.LabelFilters.Any(filter => filter.SourceFieldIndex == field.SourceFieldIndex) ||
        pivotTable.ValueFilters.Any(filter =>
            filter.SourceFieldIndex is null ||
            filter.SourceFieldIndex == field.SourceFieldIndex) ||
        pivotTable.Sorts.Any(sort => sort.FieldIndex == field.SourceFieldIndex);

    private static bool HasExplicitSelection(PivotFieldModel field)
    {
        if (field.SelectedItems is { Count: > 0 } selectedItems)
            return selectedItems.Any(IsExplicitSelection);

        return IsExplicitSelection(field.SelectedItem);
    }

    private static bool IsExplicitSelection(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !string.Equals(value, "(All)", StringComparison.OrdinalIgnoreCase);

    private static bool IsGrandTotalCaption(PivotTableModel pivotTable, string text)
    {
        var caption = string.IsNullOrWhiteSpace(pivotTable.GrandTotalCaption)
            ? "Grand Total"
            : pivotTable.GrandTotalCaption!;
        return string.Equals(text, caption, StringComparison.CurrentCultureIgnoreCase);
    }

    private static IReadOnlyList<string> ReadHeaders(Workbook workbook, PivotTableModel pivotTable)
    {
        var sourceSheet = workbook.GetSheet(pivotTable.SourceRange.Start.Sheet);
        if (sourceSheet is null)
            return [];

        var headers = new List<string>();
        for (var col = pivotTable.SourceRange.Start.Col; col <= pivotTable.SourceRange.End.Col; col++)
        {
            var value = sourceSheet.GetCell(pivotTable.SourceRange.Start.Row, col)?.Value;
            headers.Add(value is TextValue text && !string.IsNullOrWhiteSpace(text.Value)
                ? text.Value
                : $"Field{headers.Count + 1}");
        }

        return headers;
    }

    private static int RowFieldOutputColumnCount(PivotTableModel pivotTable) =>
        pivotTable.ReportLayout == PivotReportLayout.Compact && pivotTable.RowFields.Count > 1
            ? 1
            : pivotTable.RowFields.Count;

    private static CellAddress GetPivotBodyStart(PivotTableModel pivotTable)
    {
        var start = pivotTable.TargetRange.Start;
        var pageFieldRows = GetPageFieldRowSpan(pivotTable);
        return pageFieldRows == 0
            ? start
            : new CellAddress(start.Sheet, start.Row + pageFieldRows + 1, start.Col);
    }

    private static uint GetPageFieldRowSpan(PivotTableModel pivotTable)
    {
        var count = pivotTable.PageFields.Count;
        if (count == 0)
            return 0;

        var wrap = Math.Max(0, pivotTable.PageWrap);
        if (pivotTable.PageOverThenDown)
            return (uint)(wrap <= 0 ? 1 : (int)Math.Ceiling(count / (double)wrap));

        return (uint)(wrap <= 0 ? count : Math.Min(count, wrap));
    }

    private static (uint RowOffset, uint ColPairOffset) GetPageFieldOffset(
        int index,
        int pageFieldCount,
        int wrap,
        bool overThenDown)
    {
        if (overThenDown)
        {
            var fieldsPerRow = wrap <= 0 ? pageFieldCount : wrap;
            return ((uint)(index / fieldsPerRow), (uint)((index % fieldsPerRow) * 2));
        }

        var rowsPerColumn = wrap <= 0 ? pageFieldCount : wrap;
        return ((uint)(index % rowsPerColumn), (uint)((index / rowsPerColumn) * 2));
    }
}
