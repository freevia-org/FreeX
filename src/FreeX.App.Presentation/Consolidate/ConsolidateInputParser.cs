using FreeX.Core.Model;

namespace FreeX.App.Presentation.Consolidate;

public static class ConsolidateInputParser
{
    public static bool TryParseSourceRanges(
        string input,
        SheetId sheetId,
        out IReadOnlyList<GridRange> ranges,
        out string? invalidPart) =>
        TryParseSourceRanges(input, sheetId, _ => null, out ranges, out invalidPart);

    public static bool TryParseSourceRanges(
        string input,
        SheetId defaultSheetId,
        Func<string, SheetId?> resolveSheetId,
        out IReadOnlyList<GridRange> ranges,
        out string? invalidPart)
    {
        var parsedRanges = new List<GridRange>();
        invalidPart = null;

        foreach (var part in WorkbookRangeTextCodec.SplitReferences(input, allowSemicolon: true))
        {
            if (!WorkbookRangeTextCodec.TryParse(defaultSheetId, part, resolveSheetId, out var parsedRange))
            {
                invalidPart = part;
                ranges = [];
                return false;
            }

            parsedRanges.Add(parsedRange);
        }

        if (parsedRanges.Count == 0)
        {
            invalidPart = input.Trim();
            ranges = [];
            return false;
        }

        ranges = parsedRanges;
        return true;
    }

    public static bool TryParseDestination(string input, SheetId sheetId, out CellAddress destination) =>
        TryParseDestination(input, sheetId, _ => null, out destination);

    public static bool TryParseDestination(
        string input,
        SheetId defaultSheetId,
        Func<string, SheetId?> resolveSheetId,
        out CellAddress destination)
    {
        destination = default;
        if (!WorkbookRangeTextCodec.TryParse(defaultSheetId, input, resolveSheetId, out var range))
            return false;

        if (range.Start != range.End)
            return false;

        destination = range.Start;
        return true;
    }

}
