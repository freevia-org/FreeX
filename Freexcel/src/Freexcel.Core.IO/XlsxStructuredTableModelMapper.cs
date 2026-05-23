using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

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

    private static IEnumerable<StructuredTableFilterState> BuildFilters(StructuredTableModel table)
    {
        foreach (var filterColumn in table.FilterColumns)
        {
            var tableColumnIndex = filterColumn.ColumnId;
            if (tableColumnIndex < 0 || tableColumnIndex >= table.Columns.Count)
                continue;

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
            var text = ToFilterText(sheet.GetValue(row, filter.Column));
            if (text.Length == 0 && filter.IncludeBlank)
                continue;
            if (!filter.AllowedValues.Contains(text))
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

    private sealed record StructuredTableFilterState(
        uint Column,
        HashSet<string> AllowedValues,
        bool IncludeBlank);
}
