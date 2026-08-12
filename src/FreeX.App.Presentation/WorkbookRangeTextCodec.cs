using FreeX.Core.Model;

namespace FreeX.App.Presentation;

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

        try
        {
            if (parts.Length == 2 && TryParseWholeColumnOrRowRange(sheetId, parts[0], parts[1], out range))
                return true;

            if (!CellReferenceInputParser.TryParseCell(parts[0], sheetId, out var start))
                return false;
            var end = start;
            if (parts.Length == 2 && !CellReferenceInputParser.TryParseCell(parts[1], sheetId, out end))
                return false;
            range = new GridRange(start, end);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool TryParseOnCurrentSheet(SheetId sheetId, string input, out GridRange range) =>
        TryParse(sheetId, input, static _ => null, out range);

    public static IReadOnlyList<string> SplitReferences(string input, bool allowSemicolon = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        var references = new List<string>();
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
            else if (!inQuotedSheetName && (input[index] == ',' || allowSemicolon && input[index] == ';'))
            {
                AddSegment(input[start..index]);
                start = index + 1;
            }
        }

        AddSegment(input[start..]);
        return references;

        void AddSegment(string segment)
        {
            var trimmed = segment.Trim();
            if (trimmed.Length > 0)
                references.Add(trimmed);
        }
    }

    public static bool TryResolveReferenceSheet(
        SheetId defaultSheetId,
        string reference,
        Func<string, SheetId?> resolveSheetId,
        out SheetId sheetId,
        out string addressReference)
    {
        sheetId = defaultSheetId;
        addressReference = reference;
        var bangIndex = FindLastUnquotedBang(reference);
        if (bangIndex < 0)
            return true;

        var sheetName = UnquoteSheetName(reference[..bangIndex].Trim());
        if (resolveSheetId(sheetName) is not { } resolvedSheetId)
            return false;

        sheetId = resolvedSheetId;
        addressReference = reference[(bangIndex + 1)..].Trim();
        return true;
    }

    public static string Format(GridRange range, SheetId currentSheetId, Func<SheetId, string?> resolveSheetName)
    {
        var reference = $"{range.Start.ToA1()}:{range.End.ToA1()}";
        var sheetName = resolveSheetName(range.Start.Sheet);
        return sheetName is null || range.Start.Sheet.Equals(currentSheetId)
            ? reference
            : $"{SheetNameFormatter.QuoteIfNeeded(sheetName)}!{reference}";
    }

    private static bool TryParseWholeColumnOrRowRange(SheetId sheetId, string left, string right, out GridRange range)
    {
        if (TryParseColumn(left, out var startColumn) && TryParseColumn(right, out var endColumn))
        {
            range = new GridRange(
                new CellAddress(sheetId, 1, startColumn),
                new CellAddress(sheetId, CellAddress.MaxRow, endColumn));
            return true;
        }
        if (TryParseRow(left, out var startRow) && TryParseRow(right, out var endRow))
        {
            range = new GridRange(
                new CellAddress(sheetId, startRow, 1),
                new CellAddress(sheetId, endRow, CellAddress.MaxCol));
            return true;
        }
        range = default;
        return false;
    }

    private static bool TryParseColumn(string input, out uint column)
    {
        column = 0;
        var text = input.AsSpan().Trim().TrimStart('$');
        if (text.IsEmpty || !text.ToString().All(char.IsLetter))
            return false;
        column = CellAddress.ColumnNameToNumber(text.ToString());
        return column is > 0 and <= CellAddress.MaxCol;
    }

    private static bool TryParseRow(string input, out uint row)
    {
        row = 0;
        var text = input.AsSpan().Trim().TrimStart('$');
        return !text.IsEmpty && text.ToString().All(char.IsDigit) &&
               uint.TryParse(text, out row) && row is > 0 and <= CellAddress.MaxRow;
    }

    private static int FindLastUnquotedBang(string reference)
    {
        var inQuotedSheetName = false;
        var lastBang = -1;
        for (var index = 0; index < reference.Length; index++)
        {
            if (reference[index] == '\'')
            {
                if (index + 1 < reference.Length && reference[index + 1] == '\'')
                    index++;
                else
                    inQuotedSheetName = !inQuotedSheetName;
            }
            else if (reference[index] == '!' && !inQuotedSheetName)
            {
                lastBang = index;
            }
        }
        return lastBang;
    }

    private static string UnquoteSheetName(string sheetName) =>
        sheetName.Length >= 2 && sheetName[0] == '\'' && sheetName[^1] == '\''
            ? sheetName[1..^1].Replace("''", "'", StringComparison.Ordinal)
            : sheetName;
}
