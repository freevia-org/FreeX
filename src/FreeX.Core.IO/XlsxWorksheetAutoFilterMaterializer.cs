using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetAutoFilterMaterializer
{
    internal static HashSet<uint> FindNativeFilterHiddenRows(Sheet sheet)
    {
        var autoFilter = sheet.AutoFilter;
        if (autoFilter is null || autoFilter.FilterColumns.Count == 0 || string.IsNullOrWhiteSpace(autoFilter.Reference))
            return [];

        GridRange range;
        try
        {
            range = GridRange.Parse(autoFilter.Reference, sheet.Id);
        }
        catch
        {
            return [];
        }

        var filters = BuildFilters(sheet, autoFilter, range, out var unsupportedColumnCount, out _);
        if (unsupportedColumnCount == 0)
            return [];

        // Rows that still fail the materializable filters are explained by those filters and must
        // remain filter-owned only. The residual FilterHiddenRows are the rows Excel hid for a
        // native-only criterion that FreeX cannot re-evaluate on a later load, so the writer must
        // retain their raw row-hidden bit for save-load-save fidelity.
        var nativeRows = new HashSet<uint>();
        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
        {
            if (sheet.FilterHiddenRows.Contains(row) && RowMatchesAllFilters(sheet, row, filters))
                nativeRows.Add(row);
        }

        return nativeRows;
    }

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
        // manage to build is materialized below. When BuildFilters reports unsupportedColumnCount > 0,
        // a row that passes every filter we COULD build but is still raw-hidden must be explained by
        // one of the skipped columns' real (unrepresentable) criteria -- see the fallback in the loop
        // below (R98-io-autofilter-unsupported-hiddenrows-1), which mirrors
        // XlsxStructuredTableModelMapper.MaterializeFilters' identical fallback for structured tables.
        var filters = BuildFilters(sheet, autoFilter, range, out var unsupportedColumnCount, out var singleUnsupportedColumn);

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
            else if (unsupportedColumnCount > 0 && sheet.HiddenRows.Contains(row))
            {
                // R98-io-autofilter-unsupported-hiddenrows-1: at least one filterColumn used criteria
                // BuildFilters cannot represent (CustomFilters/DateGroups/ColorFilter/IconFilter/
                // unrecognized native filter attributes -- e.g. a Custom Number/Text Filter, the
                // default date-grouped Year/Month/Day checklist, or a Cell/Font Color filter). This row
                // passes every filter we COULD build, yet its raw XML hidden bit is still set, so that
                // bit can only be explained by the skipped column's real (unrepresentable) Excel
                // criteria hiding it -- mirroring XlsxStructuredTableModelMapper.MaterializeFilters'
                // identical fallback for structured tables. Reclassify it into FilterHiddenRows (not
                // ValueFilterHiddenRows -- no value-list filter owns it) so Clear Filter / Toggle
                // AutoFilter off can restore it exactly like any other filter-hidden row, instead of it
                // being stranded forever in HiddenRows (which no filter-clearing path ever touches).
                sheet.FilterHiddenRows.Add(row);
                sheet.HiddenRows.Remove(row);

                // R98-io-autofilter-unsupported-hiddenrows-1: when exactly ONE column is unsupported
                // (the common case -- a single Custom/date-group/color/icon filter), register this row
                // into Sheet.ColumnFilterOwnedRows for that column too, mirroring how a LIVE
                // TopBottomFilterCommand/AverageFilterCommand apply registers ownership
                // (FilterHiddenRowUpdater.ApplyColumnOwnedVisibility). Without this, only "Toggle
                // AutoFilter off" (which unconditionally clears the whole range) could ever restore the
                // row -- "Clear Filter From <Column>" on this exact column
                // (FilterCommand.ClearColumnOwnedRange) would find nothing owned and leave it hidden.
                // Left null (no registration) when a second, different unsupported column also exists,
                // since which one actually hid any given row is then ambiguous -- Toggle AutoFilter off
                // still restores it in that case.
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
        out int unsupportedColumnCount,
        out uint? singleUnsupportedColumn)
    {
        var filters = new List<WorksheetAutoFilterState>();
        unsupportedColumnCount = 0;
        // R98-io-autofilter-unsupported-hiddenrows-1: tracks the one column responsible for every
        // unsupported filterColumn seen so far, so MaterializeFilters' fallback can register
        // Sheet.ColumnFilterOwnedRows ownership for it and let FilterCommand's per-column "Clear
        // Filter From <Column>" restore its rows too (not just Toggle AutoFilter off). Once a SECOND,
        // different unsupported column is seen (or one can't even be attributed to a real column --
        // an out-of-range colId), which unsupported column actually hid any given still-raw-hidden row
        // becomes ambiguous, so ownership registration is disabled (left null) rather than guessed.
        var unsupportedColumnsSeen = new HashSet<uint>();
        var hasUnattributableUnsupportedColumn = false;

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
            {
                // R98-io-autofilter-unsupported-hiddenrows-1: an out-of-range colId can't be attributed
                // to a real column, but it still means this <filterColumn> element's real Excel
                // criteria went unrepresented -- count it so MaterializeFilters' fallback can reclassify
                // any raw-hidden row this AutoFilter's XML left unexplained by a supported filter.
                unsupportedColumnCount++;
                hasUnattributableUnsupportedColumn = true;
                continue;
            }

            var column = range.Start.Col + (uint)filterColumn.ColumnId;
            // Re-evaluate only the unambiguous recovery case: one supported custom-filter column
            // and no persisted row visibility. Existing row bits and mixed-column criteria retain
            // the native fallback ownership used by Clear Filter and save/load/save round trips.
            if (filterColumn.CustomFilters.Count > 0 &&
                autoFilter.FilterColumns.Count == 1 &&
                !HasRawHiddenRows(sheet, range) &&
                filterColumn.CustomFiltersAndRaw is null &&
                filterColumn.NativeCustomFiltersAttributes is null &&
                XlsxWorksheetAutoFilterCustomFilterMatcher.TryCreate(filterColumn, out var customMatcher))
            {
                filters.Add(new WorksheetAutoFilterState(
                    column,
                    null,
                    false,
                    null,
                    customMatcher));
                continue;
            }

            if (filterColumn.CustomFilters.Count > 0 ||
                filterColumn.CustomFiltersAndRaw is not null ||
                filterColumn.NativeCustomFiltersAttributes?.Count > 0 ||
                filterColumn.DateGroups.Count > 0 ||
                filterColumn.NativeFiltersAttributes?.Count > 0 ||
                filterColumn.ColorFilter is not null ||
                filterColumn.IconFilter is not null ||
                filterColumn.NativeFilterXmls.Count > 0)
            {
                // R98-io-autofilter-unsupported-hiddenrows-1: this column's real Excel criteria (Custom
                // Number/Text Filter, the default date-grouped Year/Month/Day checklist, a Cell/Font
                // Color filter, or some other native-only filter kind) cannot be represented here.
                unsupportedColumnCount++;
                unsupportedColumnsSeen.Add(column);
                continue;
            }

            if (filterColumn.Top10 is { } top10)
            {
                pendingRanked.Add((column, top10, false));
                continue;
            }

            if (filterColumn.DynamicFilter is { } dynamicFilter)
            {
                if (!IsAverageDynamicFilter(dynamicFilter, out var above))
                {
                    // A dynamicFilter type other than above/belowAverage (e.g. "today", "nextMonth",
                    // "Q1", ...) is not currently represented either -- count it the same way.
                    unsupportedColumnCount++;
                    unsupportedColumnsSeen.Add(column);
                    continue;
                }

                pendingRanked.Add((column, null, above));
                continue;
            }

            // A value-list filter column with zero allowed values and no "include blank" flag has no
            // actual filter criterion -- it is a button-only <filterColumn colId="n" showButton="0"/>
            // (the showButton attribute lands in NativeAttributes, which is why it still passes the
            // ReadFilterColumns inclusion guard above). Treat it as unfiltered rather than materializing
            // an empty allowed-set, which would otherwise make every row fail RowMatchesAllFilters and
            // hide the entire data range. This is a legitimate "nothing to filter" column, not an
            // unsupported one, so it must NOT bump unsupportedColumnCount.
            if (filterColumn.Values.Count == 0 && !filterColumn.IncludeBlank)
                continue;

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

        singleUnsupportedColumn = !hasUnattributableUnsupportedColumn && unsupportedColumnsSeen.Count == 1
            ? unsupportedColumnsSeen.Single()
            : null;
        return filters;
    }

    private static bool HasRawHiddenRows(Sheet sheet, GridRange range) =>
        sheet.HiddenRows.Any(row => row > range.Start.Row && row <= range.End.Row);

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

            if (filter.CustomMatcher is not null)
            {
                if (!filter.CustomMatcher(sheet.GetValue(row, filter.Column)))
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
        HashSet<uint>? AllowedRows,
        Func<ScalarValue, bool>? CustomMatcher = null);
}
