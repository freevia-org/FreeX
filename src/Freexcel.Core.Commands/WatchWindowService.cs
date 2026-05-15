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

    public static IReadOnlyList<WatchWindowEntry> GetEntries(Workbook workbook)
    {
        var entries = new List<WatchWindowEntry>();
        foreach (var address in workbook.WatchedCells)
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
