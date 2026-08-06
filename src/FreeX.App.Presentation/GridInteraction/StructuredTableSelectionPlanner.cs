using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.GridInteraction;

public enum StructuredTableSelectionRegionKind
{
    Mixed,
    FullTable,
    Header,
    DataBody,
    Totals
}

public enum StructuredTableSelectionExpansionKind
{
    TableColumnData,
    TableColumnAll,
    TableRows,
    WorksheetColumns,
    WorksheetRows
}

public sealed record StructuredTableSelectionContext(
    int TableId,
    string TableName,
    GridRange TableRange,
    GridRange Selection,
    GridRange? HeaderRange,
    GridRange? DataBodyRange,
    GridRange? TotalsRange,
    StructuredTableSelectionRegionKind RegionKind)
{
    public bool IncludesHeader => HeaderRange is { } header && RangesIntersect(Selection, header);
    public bool IncludesDataBody => DataBodyRange is { } body && RangesIntersect(Selection, body);
    public bool IncludesTotals => TotalsRange is { } totals && RangesIntersect(Selection, totals);

    private static bool RangesIntersect(GridRange left, GridRange right) =>
        left.Start.Row <= right.End.Row &&
        left.End.Row >= right.Start.Row &&
        left.Start.Col <= right.End.Col &&
        left.End.Col >= right.Start.Col;
}

public sealed record StructuredTableSelectionExpansionPlan(
    GridRange Range,
    StructuredTableSelectionExpansionKind Kind,
    int? TableId = null);

/// <summary>
/// Renderer-neutral structured-table selection policy. It owns table range decomposition, Name Box
/// table-name resolution, and Excel's table-first Ctrl+Space/Shift+Space escalation.
/// </summary>
public static class StructuredTableSelectionPlanner
{
    public static StructuredTableSelectionContext? Describe(Sheet sheet, GridRange selection)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var table = FindContainingTable(sheet, selection);
        if (table is null)
            return null;

        var (header, body, totals) = Decompose(table);
        return new StructuredTableSelectionContext(
            table.Id,
            string.IsNullOrWhiteSpace(table.Name) ? table.DisplayName : table.Name,
            table.Range,
            selection,
            header,
            body,
            totals,
            Classify(selection, table.Range, header, body, totals));
    }

    public static StructuredTableSelectionExpansionPlan PlanWholeColumns(Sheet sheet, GridRange selection)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (Describe(sheet, selection) is { } table && table.DataBodyRange is { } body)
        {
            var dataColumns = SliceColumns(body, selection.Start.Col, selection.End.Col);
            var allTableColumns = SliceColumns(table.TableRange, selection.Start.Col, selection.End.Col);

            if (selection == allTableColumns)
            {
                return new StructuredTableSelectionExpansionPlan(
                    SelectionRangeService.GetWholeColumns(selection),
                    StructuredTableSelectionExpansionKind.WorksheetColumns,
                    table.TableId);
            }

            if (selection != dataColumns)
            {
                return new StructuredTableSelectionExpansionPlan(
                    dataColumns,
                    StructuredTableSelectionExpansionKind.TableColumnData,
                    table.TableId);
            }

            return new StructuredTableSelectionExpansionPlan(
                allTableColumns,
                StructuredTableSelectionExpansionKind.TableColumnAll,
                table.TableId);
        }

        return new StructuredTableSelectionExpansionPlan(
            SelectionRangeService.GetWholeColumns(selection),
            StructuredTableSelectionExpansionKind.WorksheetColumns);
    }

    public static StructuredTableSelectionExpansionPlan PlanWholeRows(Sheet sheet, GridRange selection)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (Describe(sheet, selection) is { } table)
        {
            var tableRows = new GridRange(
                new CellAddress(selection.Start.Sheet, selection.Start.Row, table.TableRange.Start.Col),
                new CellAddress(selection.Start.Sheet, selection.End.Row, table.TableRange.End.Col));
            if (selection != tableRows)
            {
                return new StructuredTableSelectionExpansionPlan(
                    tableRows,
                    StructuredTableSelectionExpansionKind.TableRows,
                    table.TableId);
            }
        }

        return new StructuredTableSelectionExpansionPlan(
            SelectionRangeService.GetWholeRows(selection),
            StructuredTableSelectionExpansionKind.WorksheetRows);
    }

    public static bool TryResolveDataBodyRange(Workbook workbook, string? tableName, out GridRange range)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var table = FindByName(workbook, tableName);
        if (table is null)
        {
            range = default;
            return false;
        }

        range = GetDataBodyRangeOrTableRange(table);
        return true;
    }

    public static bool ContainsTableName(Workbook workbook, string? tableName)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        return FindByName(workbook, tableName) is not null;
    }

    public static bool OverlapsAnyTable(Sheet sheet, GridRange selection)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return FindOverlappingTableRange(sheet, selection) is not null;
    }

    public static GridRange? FindOverlappingTableRange(Sheet sheet, GridRange selection)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        foreach (var table in sheet.StructuredTables)
        {
            if (table.Range.Overlaps(selection))
                return table.Range;
        }

        return null;
    }

    public static GridRange GetDataBodyRangeOrTableRange(StructuredTableModel table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return Decompose(table).DataBody ?? table.Range;
    }

    private static StructuredTableModel? FindContainingTable(Sheet sheet, GridRange selection)
    {
        foreach (var table in sheet.StructuredTables)
        {
            if (table.Range.Contains(selection.Start) && table.Range.Contains(selection.End))
                return table;
        }

        return null;
    }

    private static StructuredTableModel? FindByName(Workbook workbook, string? tableName)
    {
        var normalized = tableName?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;

        foreach (var sheet in workbook.Sheets)
        {
            foreach (var table in sheet.StructuredTables)
            {
                if (string.Equals(table.Name, normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(table.DisplayName, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return table;
                }
            }
        }

        return null;
    }

    private static (GridRange? Header, GridRange? DataBody, GridRange? Totals) Decompose(
        StructuredTableModel table)
    {
        var rowCount = checked((int)table.Range.RowCount);
        var headerRows = (uint)Math.Clamp(table.HeaderRowCount ?? 1, 0, rowCount);
        var remainingRows = rowCount - (int)headerRows;
        var requestedTotalsRows = table.TotalsRowCount ?? (table.TotalsRowShown ? 1 : 0);
        var totalsRows = (uint)Math.Clamp(requestedTotalsRows, 0, remainingRows);

        GridRange? header = headerRows == 0
            ? null
            : SliceRows(table.Range, table.Range.Start.Row, table.Range.Start.Row + headerRows - 1);
        GridRange? totals = totalsRows == 0
            ? null
            : SliceRows(table.Range, table.Range.End.Row - totalsRows + 1, table.Range.End.Row);

        var bodyStart = table.Range.Start.Row + headerRows;
        var bodyEnd = table.Range.End.Row - totalsRows;
        GridRange? body = bodyStart <= bodyEnd
            ? SliceRows(table.Range, bodyStart, bodyEnd)
            : null;
        return (header, body, totals);
    }

    private static StructuredTableSelectionRegionKind Classify(
        GridRange selection,
        GridRange table,
        GridRange? header,
        GridRange? body,
        GridRange? totals)
    {
        if (selection == table)
            return StructuredTableSelectionRegionKind.FullTable;
        if (header is { } headerRange && Contains(headerRange, selection))
            return StructuredTableSelectionRegionKind.Header;
        if (body is { } bodyRange && Contains(bodyRange, selection))
            return StructuredTableSelectionRegionKind.DataBody;
        if (totals is { } totalsRange && Contains(totalsRange, selection))
            return StructuredTableSelectionRegionKind.Totals;
        return StructuredTableSelectionRegionKind.Mixed;
    }

    private static bool Contains(GridRange outer, GridRange inner) =>
        outer.Contains(inner.Start) && outer.Contains(inner.End);

    private static GridRange SliceRows(GridRange range, uint startRow, uint endRow) =>
        new(
            new CellAddress(range.Start.Sheet, startRow, range.Start.Col),
            new CellAddress(range.Start.Sheet, endRow, range.End.Col));

    private static GridRange SliceColumns(GridRange range, uint startCol, uint endCol) =>
        new(
            new CellAddress(range.Start.Sheet, range.Start.Row, startCol),
            new CellAddress(range.Start.Sheet, range.End.Row, endCol));
}
