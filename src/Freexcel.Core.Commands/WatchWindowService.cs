using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed record WatchWindowEntry(
    SheetId SheetId,
    string SheetName,
    CellAddress Address,
    string ValueText,
    string? FormulaText);

public static class WatchWindowService
{
    public static bool AddWatch(Workbook workbook, CellAddress address)
    {
        if (workbook.WatchedCells.Contains(address))
            return false;

        workbook.WatchedCells.Add(address);
        return true;
    }

    public static int AddWatches(Workbook workbook, GridRange range)
    {
        var added = 0;
        foreach (var address in range.AllCells())
        {
            if (AddWatch(workbook, address))
                added++;
        }

        return added;
    }

    public static bool RemoveWatch(Workbook workbook, CellAddress address) =>
        workbook.WatchedCells.Remove(address);

    public static int RemoveWatches(Workbook workbook, GridRange range)
    {
        var removed = 0;
        foreach (var address in range.AllCells())
        {
            if (RemoveWatch(workbook, address))
                removed++;
        }

        return removed;
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
        var entries = new List<WatchWindowEntry>();
        var sheetOrder = workbook.Sheets
            .Select((sheet, index) => (sheet.Id, index))
            .ToDictionary(item => item.Id, item => item.index);

        foreach (var address in workbook.WatchedCells
                     .OrderBy(address => sheetOrder.GetValueOrDefault(address.Sheet, int.MaxValue))
                     .ThenBy(address => address.Row)
                     .ThenBy(address => address.Col))
        {
            var sheet = workbook.GetSheet(address.Sheet);
            if (sheet is null)
                continue;

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
