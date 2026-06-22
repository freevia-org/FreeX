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

        var pageStart = GetPageFieldStart(sheet, pivotTable, headers);
        var bodyStart = GetPivotBodyStart(sheet, pivotTable, headers);
        var rowHeaderStart = GetRowHeaderStart(bodyStart, pivotTable);
        var columnHeaderStart = GetColumnHeaderStart(bodyStart, pivotTable);
        AddPageTargets(sheet, pivotTable, headers, pageStart, targets);
        AddRowTargets(sheet, pivotTable, headers, rowHeaderStart, targets);
        AddColumnTargets(sheet, pivotTable, headers, columnHeaderStart, targets);
    }

    private static void AddPageTargets(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        CellAddress start,
        List<PivotHeaderDropdownTarget> targets)
    {
        if (pivotTable.PageFields.Count == 0)
            return;

        var wrap = Math.Max(0, pivotTable.PageWrap);
        for (var index = 0; index < pivotTable.PageFields.Count; index++)
        {
            var (rowOffset, colPairOffset) = GetPageFieldOffset(
                index,
                pivotTable.PageFields.Count,
                wrap,
                pivotTable.PageOverThenDown);
            var field = pivotTable.PageFields[index];
            var captionAddress = ResolvePageFieldCaptionAddress(
                sheet,
                pivotTable,
                headers,
                field,
                new CellAddress(sheet.Id, start.Row + rowOffset, start.Col + colPairOffset));
            var address = new CellAddress(sheet.Id, captionAddress.Row, captionAddress.Col + 1);
            AddTarget(sheet, pivotTable, headers, field, PivotHeaderDropdownAxis.Page, address, targets, allowNonTextValue: true);
        }
    }

    private static CellAddress ResolvePageFieldCaptionAddress(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        PivotFieldModel field,
        CellAddress expectedCaptionAddress)
    {
        var caption = field.SourceFieldIndex >= 0 && field.SourceFieldIndex < headers.Count
            ? PivotUiPlanner.FieldCaption(headers, field.SourceFieldIndex)
            : null;
        if (string.IsNullOrWhiteSpace(caption) ||
            CellTextEquals(sheet, expectedCaptionAddress, caption))
        {
            return expectedCaptionAddress;
        }

        var pageStart = GetPageFieldStart(sheet, pivotTable, headers);
        var pageRows = Math.Max(1u, GetPageFieldRowSpan(pivotTable));
        var maxCol = Math.Max(
            pivotTable.TargetRange.End.Col + 1,
            pageStart.Col + (uint)(pivotTable.PageFields.Count * 3));

        for (var row = pageStart.Row; row < pageStart.Row + pageRows; row++)
        for (var col = pageStart.Col; col <= maxCol; col++)
        {
            var address = new CellAddress(sheet.Id, row, col);
            if (CellTextEquals(sheet, address, caption))
                return address;
        }

        return expectedCaptionAddress;
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
        List<PivotHeaderDropdownTarget> targets,
        bool allowNonTextValue = false)
    {
        if (field.ShowDropDowns == false ||
            field.SourceFieldIndex < 0 ||
            field.SourceFieldIndex >= headers.Count ||
            !IsRenderableHeaderCell(sheet, pivotTable, address, allowNonTextValue))
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

    private static bool IsRenderableHeaderCell(Sheet sheet, PivotTableModel pivotTable, CellAddress address, bool allowNonTextValue)
    {
        if (sheet.GetCell(address.Row, address.Col)?.Value is not { } value)
            return false;

        if (value is TextValue text)
            return !IsGrandTotalCaption(pivotTable, text.Value);

        return allowNonTextValue;
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
        var headers = new List<string>();
        var sourceSheet = workbook.GetSheet(pivotTable.SourceRange.Start.Sheet);
        if (sourceSheet is not null)
        {
            for (var col = pivotTable.SourceRange.Start.Col; col <= pivotTable.SourceRange.End.Col; col++)
            {
                var value = sourceSheet.GetCell(pivotTable.SourceRange.Start.Row, col)?.Value;
                headers.Add(value is TextValue text && !string.IsNullOrWhiteSpace(text.Value)
                    ? text.Value
                    : $"Field{headers.Count + 1}");
            }
        }

        // Cache-based pivots loaded from xlsx have no SourceRange (the source sheet does not resolve),
        // which previously produced zero dropdown targets; fall back to the cache field names so the
        // row/column header dropdowns render (Issue 123).
        return PivotSourceHeaderResolver.Resolve(workbook, pivotTable, headers);
    }

    private static int RowFieldOutputColumnCount(PivotTableModel pivotTable) =>
        pivotTable.ReportLayout == PivotReportLayout.Compact && pivotTable.RowFields.Count > 1
            ? 1
            : pivotTable.RowFields.Count;

    private static CellAddress GetPageFieldStart(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        var start = pivotTable.TargetRange.Start;
        if (pivotTable.PageFields.Count == 0 ||
            IsPageFieldCaption(sheet, start, pivotTable, headers))
        {
            return start;
        }

        var pageFieldRows = GetPageFieldRowSpan(pivotTable);
        if (start.Row <= pageFieldRows + 1)
            return start;

        var nativePageStart = new CellAddress(start.Sheet, start.Row - pageFieldRows - 1, start.Col);
        return IsPageFieldCaption(sheet, nativePageStart, pivotTable, headers)
            ? nativePageStart
            : start;
    }

    private static bool IsPageFieldCaption(
        Sheet sheet,
        CellAddress address,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        if (pivotTable.PageFields.Count == 0 ||
            sheet.GetCell(address.Row, address.Col)?.Value is not TextValue text)
        {
            return false;
        }

        var firstPageField = pivotTable.PageFields[0];
        return firstPageField.SourceFieldIndex >= 0 &&
            firstPageField.SourceFieldIndex < headers.Count &&
            string.Equals(text.Value, PivotUiPlanner.FieldCaption(headers, firstPageField.SourceFieldIndex), StringComparison.OrdinalIgnoreCase);
    }

    private static bool CellTextEquals(Sheet sheet, CellAddress address, string text) =>
        sheet.GetCell(address.Row, address.Col)?.Value is TextValue cellText &&
        string.Equals(cellText.Value, text, StringComparison.OrdinalIgnoreCase);

    private static CellAddress GetPivotBodyStart(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        var start = pivotTable.TargetRange.Start;
        var pageFieldRows = GetPageFieldRowSpan(pivotTable);
        return pageFieldRows == 0 || !IsPageFieldCaption(sheet, start, pivotTable, headers)
            ? start
            : new CellAddress(start.Sheet, start.Row + pageFieldRows + 1, start.Col);
    }

    private static CellAddress GetRowHeaderStart(CellAddress bodyStart, PivotTableModel pivotTable) =>
        new(
            bodyStart.Sheet,
            bodyStart.Row + (uint)Math.Max(0, pivotTable.FirstDataRow - 1),
            bodyStart.Col);

    private static CellAddress GetColumnHeaderStart(CellAddress bodyStart, PivotTableModel pivotTable) =>
        new(
            bodyStart.Sheet,
            bodyStart.Row + (uint)Math.Max(0, pivotTable.FirstHeaderRow - 1),
            bodyStart.Col);

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
