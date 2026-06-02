using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxStructuredTableModelMapper
{
    public static StructuredTableModel ToModel(PendingStructuredTableModel pending, SheetId sheetId)
    {
        var table = new StructuredTableModel
        {
            Id = pending.Id,
            Name = pending.Name,
            DisplayName = pending.DisplayName,
            Range = GridRange.Parse(pending.RangeReference, sheetId),
            HasAutoFilter = pending.HasAutoFilter,
            TotalsRowShown = pending.TotalsRowShown,
            HeaderRowCount = pending.HeaderRowCount,
            TotalsRowCount = pending.TotalsRowCount,
            InsertRow = pending.InsertRow,
            InsertRowShift = pending.InsertRowShift,
            Published = pending.Published,
            Comment = pending.Comment,
            StyleName = pending.StyleName,
            ShowFirstColumn = pending.ShowFirstColumn,
            ShowLastColumn = pending.ShowLastColumn,
            ShowRowStripes = pending.ShowRowStripes,
            ShowColumnStripes = pending.ShowColumnStripes,
            PackagePart = pending.PackagePart,
            NativeSortStateXml = pending.NativeSortStateXml,
            NativeAttributes = pending.NativeAttributes,
            NativeChildXmls = pending.NativeChildXmls,
            NativeAutoFilterAttributes = pending.NativeAutoFilterAttributes,
            NativeAutoFilterChildXmls = pending.NativeAutoFilterChildXmls,
            NativeStyleInfoAttributes = pending.NativeStyleInfoAttributes,
            NativeStyleInfoChildXmls = pending.NativeStyleInfoChildXmls
        };
        table.Columns.AddRange(pending.Columns);
        table.FilterColumns.AddRange(pending.FilterColumns);
        return table;
    }

    public static void MaterializeFilters(Sheet sheet, StructuredTableModel table)
    {
        if (table.FilterColumns.Count == 0)
            return;

        var filters = BuildFilters(table).ToList();
        if (filters.Count != table.FilterColumns.Count)
            return;

        var lastDataRow = table.TotalsRowShown && table.Range.End.Row > table.Range.Start.Row
            ? table.Range.End.Row - 1
            : table.Range.End.Row;
        for (var row = table.Range.Start.Row + 1; row <= lastDataRow; row++)
        {
            if (!RowMatchesAllFilters(sheet, row, filters))
                sheet.FilterHiddenRows.Add(row);
        }
    }

    public static void MaterializeStyle(Workbook workbook, Sheet sheet, StructuredTableModel table)
    {
        if (string.IsNullOrWhiteSpace(table.StyleName) || workbook.StructuredTableStyles.Count == 0)
            return;

        var style = workbook.StructuredTableStyles.FirstOrDefault(candidate =>
            candidate.AppliesToTables &&
            string.Equals(candidate.Name, table.StyleName, StringComparison.OrdinalIgnoreCase));
        if (style is null)
            return;

        if (FindElementFormat(style, "wholeTable") is { } wholeTableFormat)
            ApplyStyleDiff(workbook, sheet, table.Range, wholeTableFormat);

        var hasHeaderRow = table.HeaderRowCount is null or > 0;
        if (hasHeaderRow && FindElementFormat(style, "headerRow") is { } headerRowFormat)
        {
            ApplyStyleDiff(
                workbook,
                sheet,
                new GridRange(
                    table.Range.Start,
                    new CellAddress(sheet.Id, table.Range.Start.Row, table.Range.End.Col)),
                headerRowFormat);
        }
    }

    private static IEnumerable<StructuredTableFilterState> BuildFilters(StructuredTableModel table)
    {
        foreach (var filterColumn in table.FilterColumns)
        {
            var tableColumnIndex = filterColumn.ColumnId;
            if (tableColumnIndex < 0 || tableColumnIndex >= table.Columns.Count)
                continue;
            if (filterColumn.CustomFilters.Count > 0 ||
                filterColumn.CustomFiltersAndRaw is not null ||
                filterColumn.NativeCustomFiltersAttributes?.Count > 0 ||
                filterColumn.NativeFilterXmls.Count > 0)
            {
                continue;
            }

            yield return new StructuredTableFilterState(
                table.Range.Start.Col + (uint)tableColumnIndex,
                new HashSet<string>(filterColumn.Values, StringComparer.OrdinalIgnoreCase),
                filterColumn.IncludeBlank);
        }
    }

    private static bool RowMatchesAllFilters(
        Sheet sheet,
        uint row,
        IReadOnlyList<StructuredTableFilterState> filters)
    {
        foreach (var filter in filters)
        {
            var text = XlsxFilterValueTextFormatter.ToFilterText(sheet.GetValue(row, filter.Column));
            if (text.Length == 0 && filter.IncludeBlank)
                continue;
            if (!filter.AllowedValues.Contains(text))
                return false;
        }

        return true;
    }

    private sealed record StructuredTableFilterState(
        uint Column,
        HashSet<string> AllowedValues,
        bool IncludeBlank);

    private static StyleDiff? FindElementFormat(StructuredTableStyleModel style, string type) =>
        style.Elements
            .FirstOrDefault(element =>
                string.Equals(element.Type, type, StringComparison.OrdinalIgnoreCase) &&
                element.Format is not null)
            ?.Format;

    private static void ApplyStyleDiff(Workbook workbook, Sheet sheet, GridRange range, StyleDiff diff)
    {
        var styleCache = new Dictionary<StyleId, StyleId>();
        foreach (var address in range.AllCells())
        {
            var cell = sheet.GetCell(address);
            var baseStyleId = cell?.StyleId ??
                sheet.GetStyleOnly(address.Row, address.Col) ??
                StyleId.Default;
            if (!styleCache.TryGetValue(baseStyleId, out var styleId))
            {
                styleId = workbook.RegisterStyle(diff.ApplyTo(workbook.GetStyle(baseStyleId)));
                styleCache[baseStyleId] = styleId;
            }

            if (cell is null)
                sheet.SetStyleOnly(address.Row, address.Col, styleId);
            else
                cell.StyleId = styleId;
        }
    }
}
