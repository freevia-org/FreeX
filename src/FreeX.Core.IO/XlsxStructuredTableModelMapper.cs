using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxStructuredTableModelMapper
{
    internal static HashSet<uint> FindNativeFilterHiddenRows(Sheet sheet, StructuredTableModel table)
    {
        if (table.FilterColumns.Count == 0)
            return [];

        var lastDataRow = table.TotalsRowShown && table.Range.End.Row > table.Range.Start.Row
            ? table.Range.End.Row - 1
            : table.Range.End.Row;
        var rowCount = checked((int)table.Range.RowCount);
        var headerRows = Math.Clamp(table.HeaderRowCount ?? 1, 0, rowCount);
        var firstDataRow = table.Range.Start.Row + (uint)headerRows;
        var filters = BuildFilters(table, out _);
        if (filters.Count == table.FilterColumns.Count)
            return [];

        var nativeRows = new HashSet<uint>();
        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            if (sheet.FilterHiddenRows.Contains(row) && RowMatchesAllFilters(sheet, row, filters))
                nativeRows.Add(row);
        }

        return nativeRows;
    }

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

        var lastDataRow = table.TotalsRowShown && table.Range.End.Row > table.Range.Start.Row
            ? table.Range.End.Row - 1
            : table.Range.End.Row;
        // Mirrors TableStyleSections.From's header-row clamp: HeaderRowCount defaults to 1 (the
        // common case) but a headerless table (Excel's "Table has headers" unchecked) legitimately
        // has HeaderRowCount == 0, in which case Range.Start.Row IS itself a data row.
        var rowCount = checked((int)table.Range.RowCount);
        var headerRows = Math.Clamp(table.HeaderRowCount ?? 1, 0, rowCount);
        var firstDataRow = table.Range.Start.Row + (uint)headerRows;

        var filters = BuildFilters(table, out var singleUnsupportedColumn);
        if (filters.Count == table.FilterColumns.Count)
        {
            // R118-io-table-autofilter-activevaluefilter-1: mirrors
            // XlsxWorksheetAutoFilterMaterializer.MaterializeFilters' identical registration --
            // Sheet.ActiveValueFilterColumns/ValueFilterHiddenRows form an ownership pair that
            // FilterCommand.RecomputeHiddenRows relies on to know which rows it may safely un-hide
            // later (see those properties' doc comments on Sheet.cs, esp. "Persisted alongside
            // FilterHiddenRows so a reload doesn't leave the two out of sync"). Without this, a
            // table's own <autoFilter> criteria hide rows correctly on load via FilterHiddenRows
            // above, but ActiveValueFilterColumns stays empty, so RecomputeHiddenRows treats the
            // table's column as having "no active value filter" and any subsequent interactive
            // filter action on it (Clear Filter From <Column>, re-checking Select All) permanently
            // no-ops instead of restoring the rows.
            foreach (var filter in filters)
            {
                sheet.ActiveValueFilterColumns[filter.Column] = filter.IncludeBlank && !filter.AllowedValues.Contains("")
                    ? [.. filter.AllowedValues, ""]
                    : [.. filter.AllowedValues];
            }

            for (var row = firstDataRow; row <= lastDataRow; row++)
            {
                if (!RowMatchesAllFilters(sheet, row, filters))
                {
                    sheet.FilterHiddenRows.Add(row);
                    sheet.ValueFilterHiddenRows.Add(row);
                    // R95-io-autofilter-load-hiddenrows-1: see the matching fix in
                    // XlsxWorksheetAutoFilterMaterializer.MaterializeFilters -- the row's raw XML
                    // "hidden" bit was already unioned into sheet.HiddenRows before this method ran,
                    // and is now fully explained by this reloaded table filter's own criteria, so it
                    // must be reclassified as filter-hidden ONLY. Leaving it in HiddenRows too would
                    // make it permanently un-clearable, since StructuredTableFilterCommand and
                    // FilterCommand's clearing paths only ever mutate FilterHiddenRows-adjacent sets.
                    sheet.HiddenRows.Remove(row);
                }
            }

            return;
        }

        // At least one filter column uses criteria FreeX cannot evaluate directly (icon/color/custom/dynamic/
        // top-N filters, preserved only as native XML). Excel already saved the filtered rows as hidden, so
        // reclassify the table's hidden data-body rows as filter-hidden. This lets SUBTOTAL/AGGREGATE codes
        // 1-11 exclude them as Excel does, without having to re-evaluate the unsupported filter.
        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            if (sheet.HiddenRows.Contains(row))
            {
                sheet.FilterHiddenRows.Add(row);
                // R95-io-autofilter-load-hiddenrows-1: "reclassify" means MOVE, not duplicate --
                // without removing the row from HiddenRows here it stays double-classified forever
                // (see the sibling fix above), since no filter-clearing path ever mutates HiddenRows.
                sheet.HiddenRows.Remove(row);

                // R118-io-table-autofilter-unsupported-ownedrows-1: when exactly ONE table
                // FilterColumns entry is unsupported (the common case -- a single Top10/Average/
                // color/icon/custom filter), register this row into sheet.ColumnFilterOwnedRows for
                // that column too, mirroring XlsxWorksheetAutoFilterMaterializer's identical fallback
                // (its own R98-io-autofilter-unsupported-hiddenrows-1 fix) and how a LIVE
                // TopBottomFilterCommand/AverageFilterCommand apply registers ownership
                // (FilterHiddenRowUpdater.ApplyColumnOwnedVisibility). Without this, neither
                // sheet.ColumnFilterOwnedRows nor sheet.ActiveValueFilterColumns has an entry for the
                // column, so the UI's Clear-Filter column discovery
                // (MainWindow.DataFilterCommands.cs BuildClearAllValueFiltersCommand, which walks both
                // dictionaries' keys) never finds it, and "Clear Filter From <Column>"
                // (FilterCommand.ClearColumnOwnedRange) would find nothing owned and leave the row
                // hidden forever. Left null (no registration) when a second, different unsupported
                // column also exists, since which one actually hid any given row is then ambiguous.
                if (singleUnsupportedColumn is { } ownerColumn)
                {
                    if (!sheet.ColumnFilterOwnedRows.TryGetValue(ownerColumn, out var owned))
                    {
                        owned = [];
                        sheet.ColumnFilterOwnedRows[ownerColumn] = owned;
                    }

                    owned.Add(row);
                }
            }
        }
    }

    public static void MaterializeStyle(Workbook workbook, Sheet sheet, StructuredTableModel table)
    {
        if (string.IsNullOrWhiteSpace(table.StyleName) || workbook.StructuredTableStyles.Count == 0)
            return;

        var style = FindTableStyle(workbook, table.StyleName);
        if (style is null)
            return;

        // R90-io-table-style-banding-5-2: snapshot which cells already carry an explicit fill BEFORE
        // this method paints anything, so the passes below that reflect dynamic table styling
        // (wholeTable, row/column stripes, firstColumn/lastColumn) can preserve a pre-existing direct
        // cell fill instead of stomping it — Excel's direct-cell-format-wins-over-table-style
        // precedence, mirroring the built-in-style path's own keepExistingFill guard
        // (StructuredTableStyleService.MergeStyleOntoCell). Header/totals rows and their per-corner
        // overrides intentionally keep taking the style's fill unconditionally, matching the
        // documented "header/totals rows always take the style fill" contract, so they are excluded.
        var preserveExistingFill = CaptureExistingFillCells(workbook, sheet, table.Range);

        if (FindElementFormat(style, "wholeTable") is { } wholeTableFormat)
            ApplyStyleDiff(workbook, sheet, table.Range, wholeTableFormat, preserveExistingFill);

        var sections = TableStyleSections.From(table);
        ApplyRowStripeFormats(workbook, sheet, style, sections.DataBody, table.ShowRowStripes, preserveExistingFill);
        ApplyColumnStripeFormats(workbook, sheet, style, sections.DataBody, table.ShowColumnStripes, preserveExistingFill);

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

        if (table.ShowFirstColumn &&
            sections.FirstColumn is { } firstColumn &&
            FindElementFormat(style, "firstColumn") is { } firstColumnFormat)
        {
            ApplyStyleDiff(workbook, sheet, firstColumn, firstColumnFormat, preserveExistingFill);
        }

        if (table.ShowLastColumn &&
            sections.LastColumn is { } lastColumn &&
            FindElementFormat(style, "lastColumn") is { } lastColumnFormat)
        {
            ApplyStyleDiff(workbook, sheet, lastColumn, lastColumnFormat, preserveExistingFill);
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

    /// <summary>
    /// Builds the filters BuildFilters can evaluate directly, and separately reports which single
    /// absolute column (if any) was responsible for every FilterColumns entry this method could NOT
    /// represent (icon/color/custom/dynamic/top-N filters or an out-of-range colId). Mirrors
    /// XlsxWorksheetAutoFilterMaterializer.BuildFilters' identical unsupported-column tracking
    /// (R98-io-autofilter-unsupported-hiddenrows-1) so MaterializeFilters' fallback below can register
    /// Sheet.ColumnFilterOwnedRows ownership for the unrepresentable column (R118-io-table-autofilter-
    /// unsupported-ownedrows-1).
    /// </summary>
    private static List<StructuredTableFilterState> BuildFilters(
        StructuredTableModel table,
        out uint? singleUnsupportedColumn)
    {
        var filters = new List<StructuredTableFilterState>();
        var unsupportedColumnsSeen = new HashSet<uint>();
        var hasUnattributableUnsupportedColumn = false;

        foreach (var filterColumn in table.FilterColumns)
        {
            var tableColumnIndex = filterColumn.ColumnId;
            if (tableColumnIndex < 0 || tableColumnIndex >= table.Columns.Count)
            {
                // Can't be attributed to a real column, but this FilterColumns entry's real Excel
                // criteria still went unrepresented -- disable ownership registration rather than
                // guessing which column it was.
                hasUnattributableUnsupportedColumn = true;
                continue;
            }

            var column = table.Range.Start.Col + (uint)tableColumnIndex;
            if (filterColumn.CustomFilters.Count > 0 ||
                filterColumn.CustomFiltersAndRaw is not null ||
                filterColumn.NativeCustomFiltersAttributes?.Count > 0 ||
                filterColumn.NativeFilterXmls.Count > 0)
            {
                unsupportedColumnsSeen.Add(column);
                continue;
            }

            filters.Add(new StructuredTableFilterState(
                column,
                new HashSet<string>(filterColumn.Values, StringComparer.OrdinalIgnoreCase),
                filterColumn.IncludeBlank));
        }

        singleUnsupportedColumn = !hasUnattributableUnsupportedColumn && unsupportedColumnsSeen.Count == 1
            ? unsupportedColumnsSeen.Single()
            : null;
        return filters;
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
        bool enabled,
        IReadOnlySet<CellAddress>? preserveExistingFill = null)
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
                format,
                preserveExistingFill);
        }
    }

    private static void ApplyColumnStripeFormats(
        Workbook workbook,
        Sheet sheet,
        StructuredTableStyleModel style,
        GridRange? dataBody,
        bool enabled,
        IReadOnlySet<CellAddress>? preserveExistingFill = null)
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
                format,
                preserveExistingFill);
        }
    }

    private static uint GetStripeSize(StructuredTableStyleElementModel? element) =>
        element?.Size is > 0 ? checked((uint)element.Size.Value) : 1;

    private static StructuredTableStyleModel? FindTableStyle(Workbook workbook, string styleName)
    {
        foreach (var candidate in workbook.StructuredTableStyles)
        {
            if (candidate.AppliesToTables &&
                string.Equals(candidate.Name, styleName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static StructuredTableStyleElementModel? FindElement(StructuredTableStyleModel style, string type)
    {
        foreach (var element in style.Elements)
        {
            if (string.Equals(element.Type, type, StringComparison.OrdinalIgnoreCase))
                return element;
        }

        return null;
    }

    private static StyleDiff? FindElementFormat(StructuredTableStyleModel style, string type)
    {
        foreach (var element in style.Elements)
        {
            if (string.Equals(element.Type, type, StringComparison.OrdinalIgnoreCase) &&
                element.Format is not null)
            {
                return element.Format;
            }
        }

        return null;
    }

    private static void ApplyStyleDiff(
        Workbook workbook,
        Sheet sheet,
        GridRange range,
        StyleDiff diff,
        IReadOnlySet<CellAddress>? preserveExistingFill = null)
    {
        var fullCache = new Dictionary<StyleId, StyleId>();
        Dictionary<StyleId, StyleId>? guardedCache = null;
        StyleDiff? fillLessDiff = null;

        foreach (var address in range.AllCells())
        {
            var cell = sheet.GetCell(address);
            var baseStyleId = cell?.StyleId ??
                sheet.GetStyleOnly(address.Row, address.Col) ??
                StyleId.Default;

            // A cell that already carried an explicit fill before this table was styled at all keeps
            // it: direct cell formatting wins over dynamic table styling (see R90-io-table-style-
            // banding-5-2). Every other property in the diff (font, border, etc.) still applies.
            var keepExistingFill = preserveExistingFill?.Contains(address) == true;
            var effectiveDiff = diff;
            Dictionary<StyleId, StyleId> cache;
            if (keepExistingFill)
            {
                fillLessDiff ??= StripFillOverrides(diff);
                effectiveDiff = fillLessDiff;
                guardedCache ??= new Dictionary<StyleId, StyleId>();
                cache = guardedCache;
            }
            else
            {
                cache = fullCache;
            }

            if (!cache.TryGetValue(baseStyleId, out var styleId))
            {
                styleId = workbook.RegisterStyle(effectiveDiff.ApplyTo(workbook.GetStyle(baseStyleId)));
                cache[baseStyleId] = styleId;
            }

            if (cell is null)
                sheet.SetStyleOnly(address.Row, address.Col, styleId);
            else
                cell.StyleId = styleId;
        }
    }

    /// <summary>
    /// Returns a copy of <paramref name="diff"/> with every fill-affecting override cleared, so
    /// applying it leaves a cell's existing fill untouched while still applying its other properties
    /// (font, border, number format, alignment, etc.).
    /// </summary>
    private static StyleDiff StripFillOverrides(StyleDiff diff) => diff with
    {
        FillColor = null,
        FillThemeColor = null,
        ClearFill = null,
        FillPatternStyle = null,
        FillPatternColor = null,
        FillPatternThemeColor = null,
        GradientFill = null
    };

    /// <summary>
    /// Snapshots which cells in <paramref name="range"/> already carry an explicit fill (flat color,
    /// theme color, pattern, or gradient) before any table-style pass writes to them.
    /// </summary>
    private static HashSet<CellAddress> CaptureExistingFillCells(Workbook workbook, Sheet sheet, GridRange range)
    {
        var cells = new HashSet<CellAddress>();
        foreach (var address in range.AllCells())
        {
            var styleId = sheet.GetCell(address)?.StyleId ?? sheet.GetStyleOnly(address.Row, address.Col);
            if (styleId is null)
                continue;

            var existingStyle = workbook.GetStyle(styleId.Value);
            if (existingStyle.FillColor is not null ||
                existingStyle.FillThemeColor is not null ||
                existingStyle.FillPatternStyle != CellFillPatternStyle.None ||
                existingStyle.GradientFill is not null)
            {
                cells.Add(address);
            }
        }

        return cells;
    }

    private readonly record struct TableStyleSections(
        GridRange? HeaderRows,
        GridRange? DataBody,
        GridRange? TotalsRows,
        GridRange? FirstColumn,
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
            // firstColumn/lastColumn govern ONLY the data body rows of that column — the header and
            // totals corners of the first/last column are governed exclusively by
            // firstHeaderCell/lastHeaderCell/firstTotalCell/lastTotalCell (falling back to
            // headerRow/totalRow when a corner element is absent), never by firstColumn/lastColumn.
            // Scoping these to the full table height (including header/totals rows) would let this
            // format get applied to those corner cells too, stomping the correct headerRow/totalRow
            // look when the style doesn't define the per-corner overrides.
            var firstColumn = dataRange is { } firstColumnDataRange
                ? CreateRange(
                    table,
                    firstColumnDataRange.Start.Row,
                    table.Range.Start.Col,
                    firstColumnDataRange.End.Row,
                    table.Range.Start.Col)
                : (GridRange?)null;
            var hasDistinctLastColumn = table.Range.End.Col != table.Range.Start.Col;
            var lastColumn = hasDistinctLastColumn && dataRange is { } lastColumnDataRange
                ? CreateRange(
                    table,
                    lastColumnDataRange.Start.Row,
                    table.Range.End.Col,
                    lastColumnDataRange.End.Row,
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
