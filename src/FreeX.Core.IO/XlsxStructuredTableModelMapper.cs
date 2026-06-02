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

        var sections = TableStyleSections.From(table);
        ApplyRowStripeFormats(workbook, sheet, style, sections.DataBody, table.ShowRowStripes);
        ApplyColumnStripeFormats(workbook, sheet, style, sections.DataBody, table.ShowColumnStripes);

        if (sections.HeaderRows is { } headerRows && FindElementFormat(style, "headerRow") is { } headerRowFormat)
        {
            ApplyStyleDiff(
                workbook,
                sheet,
                headerRows,
                headerRowFormat);
        }

        if (sections.TotalsRows is { } totalsRows && FindElementFormat(style, "totalRow") is { } totalRowFormat)
            ApplyStyleDiff(workbook, sheet, totalsRows, totalRowFormat);

        if (table.ShowFirstColumn && FindElementFormat(style, "firstColumn") is { } firstColumnFormat)
            ApplyStyleDiff(workbook, sheet, sections.FirstColumn, firstColumnFormat);

        if (table.ShowLastColumn &&
            sections.LastColumn is { } lastColumn &&
            FindElementFormat(style, "lastColumn") is { } lastColumnFormat)
        {
            ApplyStyleDiff(workbook, sheet, lastColumn, lastColumnFormat);
        }

        if (table.ShowFirstColumn &&
            sections.FirstHeaderCell is { } firstHeaderCell &&
            FindElementFormat(style, "firstHeaderCell") is { } firstHeaderCellFormat)
        {
            ApplyStyleDiff(workbook, sheet, firstHeaderCell, firstHeaderCellFormat);
        }

        if (table.ShowLastColumn &&
            sections.LastHeaderCell is { } lastHeaderCell &&
            FindElementFormat(style, "lastHeaderCell") is { } lastHeaderCellFormat)
        {
            ApplyStyleDiff(workbook, sheet, lastHeaderCell, lastHeaderCellFormat);
        }

        if (table.ShowFirstColumn &&
            sections.FirstTotalCell is { } firstTotalCell &&
            FindElementFormat(style, "firstTotalCell") is { } firstTotalCellFormat)
        {
            ApplyStyleDiff(workbook, sheet, firstTotalCell, firstTotalCellFormat);
        }

        if (table.ShowLastColumn &&
            sections.LastTotalCell is { } lastTotalCell &&
            FindElementFormat(style, "lastTotalCell") is { } lastTotalCellFormat)
        {
            ApplyStyleDiff(workbook, sheet, lastTotalCell, lastTotalCellFormat);
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

    private static void ApplyRowStripeFormats(
        Workbook workbook,
        Sheet sheet,
        StructuredTableStyleModel style,
        GridRange? dataBody,
        bool enabled)
    {
        if (!enabled || dataBody is null)
            return;

        var firstStripe = FindElement(style, "firstRowStripe");
        var secondStripe = FindElement(style, "secondRowStripe");
        if (firstStripe?.Format is null && secondStripe?.Format is null)
            return;

        var firstSize = GetStripeSize(firstStripe);
        var secondSize = GetStripeSize(secondStripe);
        var cycleSize = checked((uint)(firstSize + secondSize));

        for (var row = dataBody.Value.Start.Row; row <= dataBody.Value.End.Row; row++)
        {
            var stripeOffset = (row - dataBody.Value.Start.Row) % cycleSize;
            var format = stripeOffset < firstSize ? firstStripe?.Format : secondStripe?.Format;
            if (format is null)
                continue;

            ApplyStyleDiff(
                workbook,
                sheet,
                new GridRange(
                    new CellAddress(dataBody.Value.Start.Sheet, row, dataBody.Value.Start.Col),
                    new CellAddress(dataBody.Value.Start.Sheet, row, dataBody.Value.End.Col)),
                format);
        }
    }

    private static void ApplyColumnStripeFormats(
        Workbook workbook,
        Sheet sheet,
        StructuredTableStyleModel style,
        GridRange? dataBody,
        bool enabled)
    {
        if (!enabled || dataBody is null)
            return;

        var firstStripe = FindElement(style, "firstColumnStripe");
        var secondStripe = FindElement(style, "secondColumnStripe");
        if (firstStripe?.Format is null && secondStripe?.Format is null)
            return;

        var firstSize = GetStripeSize(firstStripe);
        var secondSize = GetStripeSize(secondStripe);
        var cycleSize = checked((uint)(firstSize + secondSize));

        for (var col = dataBody.Value.Start.Col; col <= dataBody.Value.End.Col; col++)
        {
            var stripeOffset = (col - dataBody.Value.Start.Col) % cycleSize;
            var format = stripeOffset < firstSize ? firstStripe?.Format : secondStripe?.Format;
            if (format is null)
                continue;

            ApplyStyleDiff(
                workbook,
                sheet,
                new GridRange(
                    new CellAddress(dataBody.Value.Start.Sheet, dataBody.Value.Start.Row, col),
                    new CellAddress(dataBody.Value.Start.Sheet, dataBody.Value.End.Row, col)),
                format);
        }
    }

    private static uint GetStripeSize(StructuredTableStyleElementModel? element) =>
        element?.Size is > 0 ? checked((uint)element.Size.Value) : 1;

    private static StructuredTableStyleElementModel? FindElement(StructuredTableStyleModel style, string type) =>
        style.Elements
            .FirstOrDefault(element =>
                string.Equals(element.Type, type, StringComparison.OrdinalIgnoreCase));

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

    private readonly record struct TableStyleSections(
        GridRange? HeaderRows,
        GridRange? DataBody,
        GridRange? TotalsRows,
        GridRange FirstColumn,
        GridRange? LastColumn,
        GridRange? FirstHeaderCell,
        GridRange? LastHeaderCell,
        GridRange? FirstTotalCell,
        GridRange? LastTotalCell)
    {
        public static TableStyleSections From(StructuredTableModel table)
        {
            var rowCount = checked((int)table.Range.RowCount);
            var headerRows = Math.Clamp(table.HeaderRowCount ?? 1, 0, rowCount);
            var remainingRows = rowCount - headerRows;
            var totalsRows = table.TotalsRowShown
                ? Math.Clamp(table.TotalsRowCount ?? 1, 0, remainingRows)
                : 0;
            var dataRows = rowCount - headerRows - totalsRows;

            var headerRange = headerRows > 0
                ? CreateRange(
                    table,
                    table.Range.Start.Row,
                    table.Range.Start.Col,
                    table.Range.Start.Row + checked((uint)headerRows) - 1,
                    table.Range.End.Col)
                : (GridRange?)null;
            var dataRange = dataRows > 0
                ? CreateRange(
                    table,
                    table.Range.Start.Row + checked((uint)headerRows),
                    table.Range.Start.Col,
                    table.Range.End.Row - checked((uint)totalsRows),
                    table.Range.End.Col)
                : (GridRange?)null;
            var totalsRange = totalsRows > 0
                ? CreateRange(
                    table,
                    table.Range.End.Row - checked((uint)totalsRows) + 1,
                    table.Range.Start.Col,
                    table.Range.End.Row,
                    table.Range.End.Col)
                : (GridRange?)null;
            var firstColumn = CreateRange(
                table,
                table.Range.Start.Row,
                table.Range.Start.Col,
                table.Range.End.Row,
                table.Range.Start.Col);
            var hasDistinctLastColumn = table.Range.End.Col != table.Range.Start.Col;
            var lastColumn = hasDistinctLastColumn
                ? CreateRange(
                    table,
                    table.Range.Start.Row,
                    table.Range.End.Col,
                    table.Range.End.Row,
                    table.Range.End.Col)
                : (GridRange?)null;

            return new TableStyleSections(
                headerRange,
                dataRange,
                totalsRange,
                firstColumn,
                lastColumn,
                headerRange is null
                    ? null
                    : CreateRange(
                        table,
                        headerRange.Value.Start.Row,
                        table.Range.Start.Col,
                        headerRange.Value.End.Row,
                        table.Range.Start.Col),
                headerRange is null || !hasDistinctLastColumn
                    ? null
                    : CreateRange(
                        table,
                        headerRange.Value.Start.Row,
                        table.Range.End.Col,
                        headerRange.Value.End.Row,
                        table.Range.End.Col),
                totalsRange is null
                    ? null
                    : CreateRange(
                        table,
                        totalsRange.Value.Start.Row,
                        table.Range.Start.Col,
                        totalsRange.Value.End.Row,
                        table.Range.Start.Col),
                totalsRange is null || !hasDistinctLastColumn
                    ? null
                    : CreateRange(
                        table,
                        totalsRange.Value.Start.Row,
                        table.Range.End.Col,
                        totalsRange.Value.End.Row,
                        table.Range.End.Col));
        }

        private static GridRange CreateRange(
            StructuredTableModel table,
            uint startRow,
            uint startCol,
            uint endRow,
            uint endCol) =>
            new(
                new CellAddress(table.Range.Start.Sheet, startRow, startCol),
                new CellAddress(table.Range.Start.Sheet, endRow, endCol));
    }
}
