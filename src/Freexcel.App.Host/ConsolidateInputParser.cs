using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class ConsolidateInputParser
{
    public static bool TryParseSourceRanges(
        string input,
        SheetId sheetId,
        out IReadOnlyList<GridRange> ranges,
        out string? invalidPart)
    {
        var parsedRanges = new List<GridRange>();
        invalidPart = null;

        foreach (var part in input.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                parsedRanges.Add(GridRange.Parse(part, sheetId));
            }
            catch
            {
                invalidPart = part;
                ranges = [];
                return false;
            }
        }

        if (parsedRanges.Count == 0)
        {
            invalidPart = input;
            ranges = [];
            return false;
        }

        ranges = parsedRanges;
        return true;
    }

    public static bool TryParseDestination(string input, SheetId sheetId, out CellAddress destination) =>
        CellAddress.TryParse(input, sheetId, out destination);
}
