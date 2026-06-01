using System.Buffers;
using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed record WatchWindowEntry(
    SheetId SheetId,
    string SheetName,
    CellAddress Address,
    string ValueText,
    string? FormulaText);

public static class WatchWindowService
{
    private const int ColumnKeyBits = 15;
    private const int RowAndColumnKeyBits = 36;

    public static bool AddWatch(Workbook workbook, CellAddress address)
    {
        if (workbook.WatchedCells.Contains(address))
            return false;

        workbook.WatchedCells.Add(address);
        return true;
    }

    public static int AddWatches(Workbook workbook, GridRange range)
    {
        var changed = 0;
        if (range.CellCount <= int.MaxValue - workbook.WatchedCells.Count)
            workbook.WatchedCells.EnsureCapacity(workbook.WatchedCells.Count + (int)range.CellCount);

        if (workbook.WatchedCells.Count == 0)
        {
            for (var row = range.Start.Row; row <= range.End.Row; row++)
            {
                for (var col = range.Start.Col; col <= range.End.Col; col++)
                {
                    workbook.WatchedCells.Add(new CellAddress(range.Start.Sheet, row, col));
                    changed++;
                }
            }

            return changed;
        }

        var existing = new HashSet<CellAddress>(workbook.WatchedCells);
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
            {
                var address = new CellAddress(range.Start.Sheet, row, col);
                if (!existing.Add(address))
                    continue;

                workbook.WatchedCells.Add(address);
                changed++;
            }
        }

        return changed;
    }

    public static bool RemoveWatch(Workbook workbook, CellAddress address) =>
        workbook.WatchedCells.Remove(address);

    public static int RemoveWatches(Workbook workbook, GridRange range)
    {
        var changed = 0;
        for (var index = workbook.WatchedCells.Count - 1; index >= 0; index--)
        {
            if (!range.Contains(workbook.WatchedCells[index]))
                continue;

            workbook.WatchedCells.RemoveAt(index);
            changed++;
        }

        return changed;
    }

    public static IReadOnlyList<CellAddress> GetDeleteTargets(
        IEnumerable<CellAddress> selectedAddresses,
        CellAddress? fallbackAddress)
    {
        var targets = new List<CellAddress>();
        var seen = new HashSet<CellAddress>();

        foreach (var address in selectedAddresses)
        {
            if (seen.Add(address))
                targets.Add(address);
        }

        if (targets.Count == 0 && fallbackAddress is { } fallback)
            targets.Add(fallback);

        return targets;
    }

    public static IReadOnlyList<WatchWindowEntry> GetEntries(Workbook workbook)
    {
        if (workbook.WatchedCells.Count == 0)
            return [];

        var entries = new List<WatchWindowEntry>(workbook.WatchedCells.Count);
        var sheetIndexes = new Dictionary<SheetId, int>(workbook.Sheets.Count);
        for (var index = 0; index < workbook.Sheets.Count; index++)
        {
            var sheet = workbook.Sheets[index];
            sheetIndexes[sheet.Id] = index;
        }

        var sortKeys = ArrayPool<ulong>.Shared.Rent(workbook.WatchedCells.Count);
        var addresses = ArrayPool<CellAddress>.Shared.Rent(workbook.WatchedCells.Count);
        try
        {
            var validCount = 0;
            foreach (var address in workbook.WatchedCells)
            {
                if (!sheetIndexes.TryGetValue(address.Sheet, out var sheetIndex))
                    continue;

                sortKeys[validCount] = CreateSortKey(sheetIndex, address);
                addresses[validCount] = address;
                validCount++;
            }

            Array.Sort(sortKeys, addresses, 0, validCount);

            for (var index = 0; index < validCount; index++)
            {
                var address = addresses[index];
                var sheet = workbook.Sheets[GetSheetIndex(sortKeys[index])];
                var cell = sheet.GetCell(address);
                entries.Add(new WatchWindowEntry(
                    sheet.Id,
                    sheet.Name,
                    address,
                    FormatValue(cell?.Value ?? BlankValue.Instance),
                    cell?.HasFormula == true ? "=" + cell.FormulaText : null));
            }

            return entries;
        }
        finally
        {
            ArrayPool<ulong>.Shared.Return(sortKeys);
            ArrayPool<CellAddress>.Shared.Return(addresses);
        }
    }

    private static ulong CreateSortKey(int sheetIndex, CellAddress address) =>
        ((ulong)sheetIndex << RowAndColumnKeyBits) |
        ((ulong)address.Row << ColumnKeyBits) |
        address.Col;

    private static int GetSheetIndex(ulong sortKey) => (int)(sortKey >> RowAndColumnKeyBits);

    private static string FormatValue(ScalarValue value) => value switch
    {
        NumberValue number => number.Value.ToString("G15", CultureInfo.CurrentCulture),
        TextValue text => text.Value,
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        ErrorValue error => error.Code,
        BlankValue => "",
        _ => value.ToString() ?? ""
    };
}
