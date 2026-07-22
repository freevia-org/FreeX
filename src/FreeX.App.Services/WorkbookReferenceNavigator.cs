using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class WorkbookReferenceNavigator
{
    public static bool TryParseAddress(string text, SheetId sheetId, out CellAddress address)
    {
        var normalized = NormalizeAbsoluteA1Reference(text);
        return normalized is not null && CellAddress.TryParse(normalized, sheetId, out address) ||
            TryParseAbsoluteR1C1CellReference(text, sheetId, out address);
    }

    public static IReadOnlyList<string> BuildReferenceChoices(
        string defaultAddress,
        IEnumerable<string>? recentReferences,
        IEnumerable<string>? definedNames)
    {
        var choices = new List<string>();
        Add(defaultAddress);
        foreach (var reference in recentReferences ?? [])
            Add(reference);
        foreach (var name in (definedNames ?? []).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            Add(name);

        return choices.Count == 0 ? ["A1"] : choices;

        void Add(string? reference)
        {
            var trimmed = reference?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return;

            if (choices.Any(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
                return;

            choices.Add(trimmed);
        }
    }

    public static bool TryParseReference(
        string text,
        SheetId sheetId,
        IReadOnlyDictionary<string, GridRange>? definedNames,
        out CellAddress address)
    {
        if (TryParseReferenceRange(text, sheetId, definedNames, out var range))
        {
            address = range.Start;
            return true;
        }

        address = default;
        return false;
    }

    public static bool TryParseReferenceRange(
        string text,
        SheetId sheetId,
        IReadOnlyDictionary<string, GridRange>? definedNames,
        out GridRange range) =>
        TryParseReferenceRange(text, sheetId, static _ => null, definedNames, out range);

    public static bool TryParseReferenceRange(
        string text,
        SheetId defaultSheetId,
        Func<string, SheetId?> resolveSheetId,
        IReadOnlyDictionary<string, GridRange>? definedNames,
        out GridRange range) =>
        TryParseReferenceRange(text, defaultSheetId, resolveSheetId, definedNames, resolveScopedName: null, out range);

    /// <summary>
    /// Sheet-scope-aware overload matching formula evaluation's name-resolution precedence
    /// (<c>Workbook.TryGetNamedRange(name, contextSheetId, out range)</c>): a name scoped to
    /// <paramref name="defaultSheetId"/> takes precedence over a same-named workbook-global name.
    /// Pass <paramref name="resolveScopedName"/> as e.g. <c>(n, sheetId) =&gt; workbook.TryGetNamedRange(n, sheetId, out var r) ? r : null</c>.
    /// The <c>sheetId</c> argument passed to <paramref name="resolveScopedName"/> is the QUALIFIER's
    /// sheet when <paramref name="text"/> carries an explicit sheet prefix (e.g. "Sheet2!Rate" looks
    /// up the scoped name on Sheet2, not on <paramref name="defaultSheetId"/>), falling back to
    /// <paramref name="defaultSheetId"/> when the input had no sheet prefix.
    /// </summary>
    public static bool TryParseReferenceRange(
        string text,
        SheetId defaultSheetId,
        Func<string, SheetId?> resolveSheetId,
        IReadOnlyDictionary<string, GridRange>? definedNames,
        Func<string, SheetId, GridRange?>? resolveScopedName,
        out GridRange range)
    {
        if (TryParseAddress(text, defaultSheetId, out var address))
        {
            range = new GridRange(address, address);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(text) &&
            TryParseRange(defaultSheetId, text, resolveSheetId, out range))
            return true;

        var trimmedName = text.Trim();

        // A sheet-qualified reference to a defined name (e.g. "Sheet2!Rate") is legal in formulas
        // and in Excel's own Name Box, but the name itself is never stored with its sheet prefix
        // baked into the key -- strip it the same way TryParseRange does internally before falling
        // back to the plain name lookup, so "Sheet2!Rate" resolves the same as "Rate" instead of
        // silently failing to match any key.
        var nameLookupText = trimmedName;
        var scopedNameSheetId = defaultSheetId;
        if (TryResolveReferenceSheet(defaultSheetId, trimmedName, resolveSheetId, out var resolvedSheetId, out var strippedRemainder))
        {
            // The qualifier's sheet (e.g. "Sheet2" in "Sheet2!Rate") is the scope a sheet-scoped name
            // lookup must use, not the caller's active/default sheet -- otherwise a name scoped to
            // Sheet2 would be looked up against the wrong sheet whenever the Name Box's active sheet
            // differs from the one the user explicitly qualified.
            scopedNameSheetId = resolvedSheetId;
            if (!string.IsNullOrWhiteSpace(strippedRemainder))
                nameLookupText = strippedRemainder;
        }

        if (resolveScopedName?.Invoke(nameLookupText, scopedNameSheetId) is { } scopedRange)
        {
            range = scopedRange;
            return true;
        }

        if (definedNames is not null &&
            definedNames.TryGetValue(nameLookupText, out var namedRange))
        {
            range = namedRange;
            return true;
        }

        range = default;
        return false;
    }

    public static string Format(GridRange range, SheetId currentSheetId, Func<SheetId, string?> resolveSheetName)
    {
        var reference = $"{range.Start.ToA1()}:{range.End.ToA1()}";
        var sheetName = resolveSheetName(range.Start.Sheet);
        return sheetName is null || range.Start.Sheet.Equals(currentSheetId)
            ? reference
            : $"{SheetNameFormatter.QuoteIfNeeded(sheetName)}!{reference}";
    }

    private static bool TryParseRange(
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
            // Whole-column (A:A, C:E) and whole-row (5:5, 5:9) references, matching Excel's Name Box
            // and Go To reference syntax: each side is a bare column-letter run or a bare row-digit
            // run (never both mixed within the same reference).
            if (parts.Length == 2 && TryParseFullColumnOrRowRange(sheetId, parts[0], parts[1], out range))
                return true;

            if (!TryParseAddress(parts[0], sheetId, out var start))
                return false;

            var end = start;
            if (parts.Length == 2 && !TryParseAddress(parts[1], sheetId, out end))
                return false;

            range = new GridRange(start, end);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseFullColumnOrRowRange(SheetId sheetId, string left, string right, out GridRange range)
    {
        if (TryParseColumnLetters(left, out var startCol) && TryParseColumnLetters(right, out var endCol))
        {
            range = new GridRange(
                new CellAddress(sheetId, 1, startCol),
                new CellAddress(sheetId, CellAddress.MaxRow, endCol));
            return true;
        }

        if (TryParseRowNumber(left, out var startRow) && TryParseRowNumber(right, out var endRow))
        {
            range = new GridRange(
                new CellAddress(sheetId, startRow, 1),
                new CellAddress(sheetId, endRow, CellAddress.MaxCol));
            return true;
        }

        range = default;
        return false;
    }

    private static bool TryParseColumnLetters(string text, out uint column)
    {
        column = 0;
        var value = text.AsSpan().Trim();
        if (value.Length > 0 && value[0] == '$')
            value = value[1..];

        if (value.IsEmpty)
            return false;

        foreach (var c in value)
        {
            if (!char.IsLetter(c))
                return false;
        }

        column = CellAddress.ColumnNameToNumber(value.ToString());
        return column is > 0 and <= CellAddress.MaxCol;
    }

    private static bool TryParseRowNumber(string text, out uint row)
    {
        row = 0;
        var value = text.AsSpan().Trim();
        if (value.Length > 0 && value[0] == '$')
            value = value[1..];

        if (value.IsEmpty)
            return false;

        foreach (var c in value)
        {
            if (!char.IsDigit(c))
                return false;
        }

        return uint.TryParse(value, out row) && row is > 0 and <= CellAddress.MaxRow;
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

        var sheetName = UnquoteSheetName(reference[..bangIndex].Trim());
        if (resolveSheetId(sheetName) is not { } resolvedSheetId)
            return false;

        sheetId = resolvedSheetId;
        addressReference = reference[(bangIndex + 1)..].Trim();
        return true;
    }

    private static string? NormalizeAbsoluteA1Reference(string input)
    {
        var value = input.AsSpan().Trim();
        if (value.IsEmpty)
            return null;

        Span<char> buffer = stackalloc char[value.Length];
        var index = 0;
        var write = 0;

        ConsumeOptionalAbsoluteMarker(value, ref index);

        if (!ConsumeColumn(value, buffer, ref index, ref write))
            return null;

        ConsumeOptionalAbsoluteMarker(value, ref index);

        if (!ConsumeRow(value, buffer, ref index, ref write) || index != value.Length)
            return null;

        return new string(buffer[..write]);
    }

    private static void ConsumeOptionalAbsoluteMarker(ReadOnlySpan<char> value, ref int index)
    {
        if (index < value.Length && value[index] == '$')
            index++;
    }

    private static bool ConsumeColumn(ReadOnlySpan<char> value, Span<char> buffer, ref int index, ref int write)
    {
        var start = index;
        while (index < value.Length && char.IsLetter(value[index]))
            buffer[write++] = value[index++];

        return index != start;
    }

    private static bool ConsumeRow(ReadOnlySpan<char> value, Span<char> buffer, ref int index, ref int write)
    {
        var start = index;
        while (index < value.Length && char.IsDigit(value[index]))
            buffer[write++] = value[index++];

        return index != start;
    }

    private static bool TryParseAbsoluteR1C1CellReference(string input, SheetId sheetId, out CellAddress address)
    {
        address = default;
        var value = input.AsSpan().Trim();
        if (value.Length < 4 || !IsR1C1Prefix(value[0], 'R'))
            return false;

        var index = 1;
        if (!TryReadR1C1Number(value, ref index, CellAddress.MaxRow, out var row))
            return false;

        if (index >= value.Length || !IsR1C1Prefix(value[index], 'C'))
            return false;

        index++;
        if (!TryReadR1C1Number(value, ref index, CellAddress.MaxCol, out var column) || index != value.Length)
            return false;

        address = new CellAddress(sheetId, row, column);
        return true;
    }

    private static bool TryReadR1C1Number(ReadOnlySpan<char> value, ref int index, uint max, out uint number)
    {
        number = 0;
        var start = index;
        while (index < value.Length && char.IsDigit(value[index]))
        {
            number = number * 10 + (uint)(value[index] - '0');
            if (number > max)
                return false;

            index++;
        }

        return index > start && number > 0;
    }

    private static bool IsR1C1Prefix(char actual, char expected) =>
        char.ToUpperInvariant(actual) == expected;

    private static string UnquoteSheetName(string sheetName)
    {
        if (sheetName.Length >= 2 && sheetName[0] == '\'' && sheetName[^1] == '\'')
            return sheetName[1..^1].Replace("''", "'", StringComparison.Ordinal);

        return sheetName;
    }

}
