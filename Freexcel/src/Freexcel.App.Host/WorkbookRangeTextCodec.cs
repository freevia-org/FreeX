using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class WorkbookRangeTextCodec
{
    public static bool TryParse(
        SheetId defaultSheetId,
        string input,
        Func<string, SheetId?> resolveSheetId,
        out GridRange range)
    {
        range = default;
        var normalized = input.Trim();
        var sheetId = defaultSheetId;
        var bangIndex = normalized.LastIndexOf('!');
        if (bangIndex >= 0)
        {
            var sheetName = PivotUiPlanner.UnquoteSheetName(normalized[..bangIndex].Trim());
            if (resolveSheetId(sheetName) is not { } resolvedSheetId)
                return false;

            sheetId = resolvedSheetId;
            normalized = normalized[(bangIndex + 1)..].Trim();
        }

        var parts = normalized.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 2)
            return false;

        try
        {
            var start = CellAddress.Parse(parts[0], sheetId);
            var end = parts.Length == 1 ? start : CellAddress.Parse(parts[1], sheetId);
            range = new GridRange(start, end);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string Format(GridRange range, SheetId currentSheetId, Func<SheetId, string?> resolveSheetName)
    {
        var reference = $"{range.Start.ToA1()}:{range.End.ToA1()}";
        var sheetName = resolveSheetName(range.Start.Sheet);
        return sheetName is null || range.Start.Sheet.Equals(currentSheetId)
            ? reference
            : $"{PivotUiPlanner.QuoteSheetNameForReference(sheetName)}!{reference}";
    }
}
