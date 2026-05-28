using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class WorkbookRangeTextCodec
{
    public static bool TryParseMany(
        SheetId defaultSheetId,
        string input,
        Func<string, SheetId?> resolveSheetId,
        out IReadOnlyList<GridRange> ranges)
    {
        var parsed = new List<GridRange>();
        foreach (var reference in SplitReferences(input))
        {
            if (!TryParse(defaultSheetId, reference, resolveSheetId, out var range))
            {
                ranges = [];
                return false;
            }

            parsed.Add(range);
        }

        ranges = parsed;
        return parsed.Count > 0;
    }

    public static bool TryParse(
        SheetId defaultSheetId,
        string input,
        Func<string, SheetId?> resolveSheetId,
        out GridRange range)
    {
        range = default;
        var normalized = input.Trim();
        if (!TryResolveReferenceSheet(defaultSheetId, normalized, resolveSheetId, out var sheetId, out normalized))
            return false;

        var parts = normalized.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 2)
            return false;

        return TryParseRangeParts(parts, sheetId, out range);
    }

    private static bool TryResolveReferenceSheet(
        SheetId defaultSheetId,
        string reference,
        Func<string, SheetId?> resolveSheetId,
        out SheetId sheetId,
        out string addressReference)
    {
        sheetId = defaultSheetId;
        addressReference = reference;

        var bangIndex = reference.LastIndexOf('!');
        if (bangIndex < 0)
            return true;

        var sheetName = PivotUiPlanner.UnquoteSheetName(reference[..bangIndex].Trim());
        if (resolveSheetId(sheetName) is not { } resolvedSheetId)
            return false;

        sheetId = resolvedSheetId;
        addressReference = reference[(bangIndex + 1)..].Trim();
        return true;
    }

    private static bool TryParseRangeParts(string[] parts, SheetId sheetId, out GridRange range)
    {
        range = default;
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

    private static IEnumerable<string> SplitReferences(string input)
    {
        var start = 0;
        var inQuotedSheetName = false;
        for (var index = 0; index < input.Length; index++)
        {
            if (input[index] == '\'')
            {
                if (index + 1 < input.Length && input[index + 1] == '\'')
                {
                    index++;
                    continue;
                }

                inQuotedSheetName = !inQuotedSheetName;
            }
            else if (input[index] == ',' && !inQuotedSheetName)
            {
                var segment = input[start..index].Trim();
                if (segment.Length > 0)
                    yield return segment;
                start = index + 1;
            }
        }

        var finalSegment = input[start..].Trim();
        if (finalSegment.Length > 0)
            yield return finalSegment;
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
