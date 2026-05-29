using FreeX.Core.Model;
using System.Globalization;

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

        var filters = BuildFilters(sheet, autoFilter, range).ToList();
        if (filters.Count != autoFilter.FilterColumns.Count)
            return;

        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
        {
            if (!RowMatchesAllFilters(sheet, row, filters))
                sheet.FilterHiddenRows.Add(row);
        }
    }

    private static IEnumerable<WorksheetAutoFilterState> BuildFilters(Sheet sheet, WorksheetAutoFilterModel autoFilter, GridRange range)
    {
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
                yield return new WorksheetAutoFilterState(
                    column,
                    null,
                    false,
                    BuildTop10KeptRows(sheet, range, column, top10));
                continue;
            }

            if (filterColumn.DynamicFilter is { } dynamicFilter)
            {
                if (!IsAverageDynamicFilter(dynamicFilter, out var above))
                    continue;

                yield return new WorksheetAutoFilterState(
                    column,
                    null,
                    false,
                    BuildAverageKeptRows(sheet, range, column, above));
                continue;
            }

            yield return new WorksheetAutoFilterState(
                column,
                new HashSet<string>(filterColumn.Values, StringComparer.OrdinalIgnoreCase),
                filterColumn.IncludeBlank,
                null);
        }
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

    private static HashSet<uint> BuildAverageKeptRows(Sheet sheet, GridRange range, uint column, bool above)
    {
        var numericRows = new List<(uint Row, double Value)>();
        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
        {
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

    private static HashSet<uint> BuildTop10KeptRows(Sheet sheet, GridRange range, uint column, WorksheetAutoFilterTop10Model top10)
    {
        var value = top10.Value ?? 10;
        if (value <= 0)
            return [];

        var rankedRows = new List<(uint Row, double Value)>();
        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
        {
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

            var text = ToFilterText(sheet.GetValue(row, filter.Column));
            if (text.Length == 0 && filter.IncludeBlank)
                continue;
            if (filter.AllowedValues is null || !filter.AllowedValues.Contains(text))
                return false;
        }

        return true;
    }

    private static string ToFilterText(ScalarValue value) => value switch
    {
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        DateTimeValue dateTime => dateTime.ToDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ErrorValue error => error.Code,
        _ => string.Empty
    };

    private sealed record WorksheetAutoFilterState(
        uint Column,
        HashSet<string>? AllowedValues,
        bool IncludeBlank,
        HashSet<uint>? AllowedRows);
}
