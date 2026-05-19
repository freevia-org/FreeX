using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
        var shown = addresses.Take(MaxShownAddresses).Select(address => FormatAddress(workbook, address));
        var suffix = addresses.Count > MaxShownAddresses
            ? $"\n...and {addresses.Count - MaxShownAddresses} more."
            : "";
        return string.Join(", ", shown) + suffix;
    }
}
