using FreeX.Core.Model;

namespace FreeX.App.Presentation.FormulaAuditing;

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
        var shownCount = Math.Min(addresses.Count, MaxShownAddresses);
        var shown = new List<string>(shownCount);
        for (var index = 0; index < shownCount; index++)
            shown.Add(FormatAddress(workbook, addresses[index]));

        var hiddenCount = addresses.Count - MaxShownAddresses;
        var suffix = hiddenCount > 0 ? $"\n...and {hiddenCount} more." : string.Empty;
        return string.Join(", ", shown) + suffix;
    }
}
