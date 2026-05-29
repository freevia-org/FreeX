using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class FormulaAuditFormatter
{
    private const int MaxShownAddresses = 12;

    public static string FormatAddress(Workbook workbook, CellAddress address)
    {
        var sheetName = workbook.GetSheet(address.Sheet)?.Name ?? "Sheet";
        return $"{sheetName}!{address.ToA1()}";
    }

    public static string FormatAddresses(Workbook workbook, IReadOnlyList<CellAddress> addresses)
    {
        var shown = BuildShownAddresses(workbook, addresses);
        return string.Join(", ", shown) + FormatOverflowSuffix(addresses.Count);
    }

    private static List<string> BuildShownAddresses(Workbook workbook, IReadOnlyList<CellAddress> addresses)
    {
        var shownCount = Math.Min(addresses.Count, MaxShownAddresses);
        var shown = new List<string>(shownCount);

        for (var index = 0; index < shownCount; index++)
            shown.Add(FormatAddress(workbook, addresses[index]));

        return shown;
    }

    private static string FormatOverflowSuffix(int addressCount)
    {
        var hiddenCount = addressCount - MaxShownAddresses;
        return hiddenCount > 0 ? $"\n...and {hiddenCount} more." : "";
    }
}
