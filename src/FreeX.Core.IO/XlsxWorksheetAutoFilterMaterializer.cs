using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetAutoFilterMaterializer
{
    public static void MaterializeFilters(Sheet sheet)
    {
        var autoFilter = sheet.AutoFilter;
        if (autoFilter is null || autoFilter.FilterColumns.Count == 0 || string.IsNullOrWhiteSpace(autoFilter.Reference))
            return;

        GridRange range;
        try
        {
            range = GridRange.Parse(autoFilter.Reference, sheet.Id);
        }
        catch
        {
            return;
        }

        // R65-services-autofilter-6-1: BuildFilters already skips whatever it cannot represent
        // (customFilters/dateGroups/colorFilter/iconFilter columns, button-only columns, etc.) --
        // it must NOT be all-or-nothing here. Bailing the entire sheet out just because ONE column
        // uses an unsupported filter kind would silently drop every OTHER column's perfectly
        // supported value-list/Top10/Average filter too, leaving FilterHiddenRows/
        // ActiveValueFilterColumns/ColumnFilterOwnedRows empty and making Clear Filter and the
        // dropdown checklist behave as if there were no filter at all. Whatever BuildFilters DID
        // manage to build is materialized below; unsupported columns are simply left alone (their
        // raw hidden-row bits loaded from the row XML are untouched, since nothing here un-hides
        // rows -- it only ever adds to FilterHiddenRows).
        var filters = BuildFilters(sheet, autoFilter, range, out _);

        // G2/G32: sheet.ActiveValueFilterColumns/ValueFilterHiddenRows form an ownership pair that
        // FilterCommand.RecomputeHiddenRows relies on to know which rows it may safely un-hide later
        // (see Sheet.ActiveValueFilterColumns/ValueFilterHiddenRows doc comments). Plain value-list
        // filter columns parsed from the AutoFilter XML must be re-registered into
        // ActiveValueFilterColumns here so that invariant holds after a load, exactly as
        // FilterCommand.Apply itself would have registered them when the filter was first applied.
        // J30: ActiveValueFilterColumns has no separate "include blank" flag — the interactive
        // checklist path (MainWindow.DataFilterCommands.cs) represents an allowed blank as a literal
        // "" entry in the allowed-values list, since FilterValueFormatter.ToText/
        // XlsxFilterValueTextFormatter.ToFilterText both format a blank cell as "". A blank="1"
        // filter loaded from XML must register that same "" sentinel here, or a later
        // FilterCommand.RecomputeHiddenRows on any OTHER column (which rebuilds every active column's
        // matcher from ActiveValueFilterColumns with zero blank-awareness) will re-hide blank rows
        // this filter meant to keep visible.
        foreach (var filter in filters)
        {
            if (filter.AllowedValues is null)
                continue;

            sheet.ActiveValueFilterColumns[filter.Column] = filter.IncludeBlank && !filter.AllowedValues.Contains("")
                ? [.. filter.AllowedValues, ""]
                : [.. filter.AllowedValues];
        }

        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
        {
            if (!RowMatchesAllFilters(sheet, row, filters))
            {
                sheet.FilterHiddenRows.Add(row);
                if (RowFailsOnlyValueListFilters(sheet, row, filters))
                    sheet.ValueFilterHiddenRows.Add(row);

                // R95-io-autofilter-load-hiddenrows-1: the row's raw XML "hidden" bit
                // (unconditionally loaded into sheet.HiddenRows by ApplySheetXmlLayout BEFORE this
                // method runs) is fully explained by this reloaded filter's own criteria -- Excel
                // writes the same single hidden bit for a manually-hidden row and a filtered-out row,
                // so reclassify it here the same way FilterCommand.Apply's live RecomputeHiddenRows
                // hides rows purely via FilterHiddenRows/ValueFilterHiddenRows, never HiddenRows.
                // Leaving the row double-counted in HiddenRows would make it permanently un-clearable:
                // every filter-clearing path (FilterCommand.RecomputeHiddenRows,
                // ToggleWorksheetAutoFilterCommand.Apply) only ever mutates FilterHiddenRows-adjacent
                // sets, never HiddenRows, so Clear Filter could never surface the row again.
                sheet.HiddenRows.Remove(row);
            }
        }
    }

    /// <summary>
    /// True if <paramref name="row"/> is hidden because it fails at least one plain value-list
    /// filter column (the mechanism owning <see cref="Sheet.ValueFilterHiddenRows"/>), regardless of
    /// whether it also fails a Top10/Average filter column. Mirrors FilterCommand.RecomputeHiddenRows,
    /// which adds a row to ValueFilterHiddenRows whenever any active value-filter column excludes it.
    /// </summary>
    private static bool RowFailsOnlyValueListFilters(
        Sheet sheet,
        uint row,
        IReadOnlyList<WorksheetAutoFilterState> filters)
    {
        foreach (var filter in filters)
        {
            if (filter.AllowedValues is null)
                continue;

            var text = XlsxFilterValueTextFormatter.ToFilterText(sheet.GetValue(row, filter.Column));
            if (text.Length == 0 && filter.IncludeBlank)
                continue;
            if (!filter.AllowedValues.Contains(text))
                return true;
        }

        return false;
    }

    private static List<WorksheetAutoFilterState> BuildFilters(
        Sheet sheet,
        WorksheetAutoFilterModel autoFilter,
        GridRange range,
        out int unfilteredColumnCount)
    {
        var filters = new List<WorksheetAutoFilterState>();
        unfilteredColumnCount = 0;

        // R65-services-autofilter-6-3: Top10/Average filter columns must rank/average over rows
        // still visible under every OTHER active column's filter, exactly like the live
        // TopBottomFilterCommand/AverageFilterCommand apply path scopes via
        // FilterHiddenRowUpdater.IsHiddenByAnyOtherActiveMechanism -- otherwise a Top-N combined
        // with a value-list filter on load ranks over the whole column and produces a different
        // (over-hidden) result than re-applying the same filters live would. Value-list filters are
        // unambiguous and order-independent, so they are all built first below; the ranked
        // (Top10/Average) columns are resolved in a second pass against the value-list filters plus
        // whichever ranked columns were already resolved earlier in that pass.
        var pendingRanked = new List<(uint Column, WorksheetAutoFilterTop10Model? Top10, bool AverageAbove)>();
        foreach (var filterColumn in autoFilter.FilterColumns)
        {
            if (filterColumn.ColumnId < 0)
                continue;
            if (filterColumn.CustomFilters.Count > 0 ||
                filterColumn.CustomFiltersAndRaw is not null ||
                filterColumn.NativeCustomFiltersAttributes?.Count > 0 ||
                filterColumn.DateGroups.Count > 0 ||
                filterColumn.NativeFiltersAttributes?.Count > 0 ||
                filterColumn.ColorFilter is not null ||
                filterColumn.IconFilter is not null ||
                filterColumn.NativeFilterXmls.Count > 0)
            {
                continue;
            }

            var column = range.Start.Col + (uint)filterColumn.ColumnId;
            if (filterColumn.Top10 is { } top10)
            {
                pendingRanked.Add((column, top10, false));
                continue;
            }

            if (filterColumn.DynamicFilter is { } dynamicFilter)
            {
                if (!IsAverageDynamicFilter(dynamicFilter, out var above))
                    continue;

                pendingRanked.Add((column, null, above));
                continue;
            }

            // A value-list filter column with zero allowed values and no "include blank" flag has no
            // actual filter criterion -- it is a button-only <filterColumn colId="n" showButton="0"/>
            // (the showButton attribute lands in NativeAttributes, which is why it still passes the
            // ReadFilterColumns inclusion guard above). Treat it as unfiltered rather than materializing
            // an empty allowed-set, which would otherwise make every row fail RowMatchesAllFilters and
            // hide the entire data range.
            if (filterColumn.Values.Count == 0 && !filterColumn.IncludeBlank)
            {
                unfilteredColumnCount++;
                continue;
            }

            filters.Add(new WorksheetAutoFilterState(
                column,
                new HashSet<string>(filterColumn.Values, StringComparer.OrdinalIgnoreCase),
                filterColumn.IncludeBlank,
                null));
        }

        foreach (var (column, top10, averageAbove) in pendingRanked)
        {
            var keptRows = top10 is { } top10Model
                ? BuildTop10KeptRows(sheet, range, column, top10Model, filters)
                : BuildAverageKeptRows(sheet, range, column, averageAbove, filters);
            filters.Add(new WorksheetAutoFilterState(column, null, false, keptRows));
        }

        return filters;
    }

    private static bool IsAverageDynamicFilter(WorksheetAutoFilterDynamicFilterModel dynamicFilter, out bool above)
    {
        above = true;
        if (string.Equals(dynamicFilter.Type, "aboveAverage", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(dynamicFilter.Type, "belowAverage", StringComparison.OrdinalIgnoreCase))
        {
            above = false;
            return true;
        }

        return false;
    }

    private static HashSet<uint> BuildAverageKeptRows(
        Sheet sheet,
        GridRange range,
        uint column,
        bool above,
        IReadOnlyList<WorksheetAutoFilterState> otherFilters)
    {
        var numericRows = new List<(uint Row, double Value)>();
        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
        {
            if (!RowMatchesAllFilters(sheet, row, otherFilters))
                continue;
            if (sheet.GetValue(row, column) is NumberValue number)
                numericRows.Add((row, number.Value));
        }

        if (numericRows.Count == 0)
            return [];

        var average = numericRows.Average(item => item.Value);
        return numericRows
            .Where(item => above ? item.Value > average : item.Value < average)
            .Select(item => item.Row)
            .ToHashSet();
    }

    private static HashSet<uint> BuildTop10KeptRows(
        Sheet sheet,
        GridRange range,
        uint column,
        WorksheetAutoFilterTop10Model top10,
        IReadOnlyList<WorksheetAutoFilterState> otherFilters)
    {
        var value = top10.Value ?? 10;
        if (value <= 0)
            return [];

        var rankedRows = new List<(uint Row, double Value)>();
        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
        {
            if (!RowMatchesAllFilters(sheet, row, otherFilters))
                continue;
            if (sheet.GetValue(row, column) is NumberValue number)
                rankedRows.Add((row, number.Value));
        }

        var keepCount = top10.Percent
            ? (uint)Math.Ceiling(rankedRows.Count * Math.Min(value, 100) / 100.0)
            : (uint)Math.Floor(value);
        if (top10.FilterValue is { } threshold)
        {
            return rankedRows
                .Where(item => top10.Top ? item.Value >= threshold : item.Value <= threshold)
                .Select(item => item.Row)
                .ToHashSet();
        }

        return rankedRows
            .OrderBy(item => top10.Top ? -item.Value : item.Value)
            .ThenBy(item => item.Row)
            .Take((int)Math.Min(keepCount, (uint)rankedRows.Count))
            .Select(item => item.Row)
            .ToHashSet();
    }

    private static bool RowMatchesAllFilters(
        Sheet sheet,
        uint row,
        IReadOnlyList<WorksheetAutoFilterState> filters)
    {
        foreach (var filter in filters)
        {
            if (filter.AllowedRows is not null)
            {
                if (!filter.AllowedRows.Contains(row))
                    return false;
                continue;
            }

            var text = XlsxFilterValueTextFormatter.ToFilterText(sheet.GetValue(row, filter.Column));
            if (text.Length == 0 && filter.IncludeBlank)
                continue;
            if (filter.AllowedValues is null || !filter.AllowedValues.Contains(text))
                return false;
        }

        return true;
    }

    private sealed record WorksheetAutoFilterState(
        uint Column,
        HashSet<string>? AllowedValues,
        bool IncludeBlank,
        HashSet<uint>? AllowedRows);
}
