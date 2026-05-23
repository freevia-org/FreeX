using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class AdvancedFilterInputParser
{
    public static bool TryParseRange(
        SheetId defaultSheetId,
        string input,
        Func<string, SheetId?> resolveSheetId,
        out GridRange range) =>
        WorkbookRangeTextCodec.TryParse(defaultSheetId, input, resolveSheetId, out range);

    public static bool TryParseCopyDestination(string input, SheetId sheetId, out CellAddress? destination)
    {
        destination = null;
        if (string.IsNullOrWhiteSpace(input))
            return true;

        if (!CellAddress.TryParse(input.Trim(), sheetId, out var parsed))
            return false;

        destination = parsed;
        return true;
    }

    public static bool TryParseCopyDestinationRange(string input, SheetId sheetId, out GridRange? destination)
    {
        destination = null;
        if (string.IsNullOrWhiteSpace(input))
            return true;

        if (!WorkbookRangeTextCodec.TryParse(sheetId, input, _ => null, out var parsed))
            return false;

        if (parsed.Start.Row != parsed.End.Row)
            return false;

        destination = parsed;
        return true;
    }

    public static bool ParseUniqueOnly(string input)
    {
        var normalized = input.Trim();
        return normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("y", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}
