using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class WorkbookReferenceNavigator
{
    public static bool TryParseAddress(string text, SheetId sheetId, out CellAddress address)
    {
        var normalized = AbsoluteCellReferenceNormalizer.Normalize(text);
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

    public static bool TryParseReferenceRanges(
        string text,
        SheetId sheetId,
        IReadOnlyDictionary<string, GridRange>? definedNames,
        out IReadOnlyList<GridRange> ranges) =>
        TryParseReferenceRanges(text, sheetId, static _ => null, definedNames, out ranges);

    public static bool TryParseReferenceRanges(
        string text,
        SheetId defaultSheetId,
        Func<string, SheetId?> resolveSheetId,
        IReadOnlyDictionary<string, GridRange>? definedNames,
        out IReadOnlyList<GridRange> ranges) =>
        TryParseReferenceRanges(text, defaultSheetId, resolveSheetId, definedNames, resolveScopedName: null, out ranges);

    /// <summary>
    /// Multi-area sibling of <see cref="TryParseReferenceRange(string,SheetId,Func{string,SheetId?},IReadOnlyDictionary{string,GridRange},Func{string,SheetId,GridRange?},out GridRange)"/>,
    /// matching Excel's Name Box behavior of splitting a comma-separated reference (e.g. "A1,C3" or
    /// "A1:B2,D4") into a disjoint multi-area selection -- the same result Ctrl+clicking each area
    /// individually would produce. A single-area reference containing no top-level comma (including a
    /// plain defined-name lookup, which can never contain a comma -- commas are illegal in Excel
    /// defined-name syntax) parses identically to the singular overload. A comma inside a quoted sheet
    /// name (e.g. "'Q1, Actuals'!A1") is not treated as an area separator. Fails (returning no ranges)
    /// if any individual area fails to parse, matching Excel's all-or-nothing Name Box behavior.
    /// </summary>
    public static bool TryParseReferenceRanges(
        string text,
        SheetId defaultSheetId,
        Func<string, SheetId?> resolveSheetId,
        IReadOnlyDictionary<string, GridRange>? definedNames,
        Func<string, SheetId, GridRange?>? resolveScopedName,
        out IReadOnlyList<GridRange> ranges)
    {
        var parsed = new List<GridRange>();
        foreach (var area in SplitReferenceAreas(text))
        {
            if (!TryParseReferenceRange(area, defaultSheetId, resolveSheetId, definedNames, resolveScopedName, out var range))
            {
                ranges = [];
                return false;
            }

            parsed.Add(range);
        }

        ranges = parsed;
        return parsed.Count > 0;
    }

    // Splits on top-level commas only -- a comma inside a single-quoted sheet name (which may
    // legally contain one, e.g. 'Q1, Actuals'!A1) is not an area separator. Mirrors
    // WorkbookRangeTextCodec.SplitReferences.
    private static IEnumerable<string> SplitReferenceAreas(string input)
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

    /// <summary>
    /// Whether <paramref name="text"/> already names an existing named FORMULA/constant (e.g.
    /// "TaxRate" = "0.08") rather than a plain named range -- used by a Name Box "define on Enter"
    /// path to refuse silently redefining that name as a range. A named formula/constant has no
    /// <c>GridRange</c> to navigate to, so <see cref="TryParseReferenceRange(string,SheetId,Func{string,SheetId?},IReadOnlyDictionary{string,GridRange},Func{string,SheetId,GridRange?},out GridRange)"/>
    /// always falls through for one (correctly, since there is nothing to select); this method gives
    /// the create path a way to distinguish "no such name at all" (safe to create) from "a
    /// formula/constant already owns this name" (must not silently clobber it with a range). Applies
    /// the same sheet-qualifier stripping and scope-then-global fallback as the range overload so
    /// "Sheet2!Rate" resolves against Sheet2's scope exactly like the range lookup does.
    /// </summary>
    public static bool NameExistsAsFormula(
        string text,
        SheetId defaultSheetId,
        Func<string, SheetId?> resolveSheetId,
        IReadOnlyDictionary<string, string>? namedFormulas,
        Func<string, SheetId, string?>? resolveScopedFormula = null)
    {
        var trimmedName = text.Trim();
        var nameLookupText = trimmedName;
        var scopedNameSheetId = defaultSheetId;
        if (TryResolveReferenceSheet(defaultSheetId, trimmedName, resolveSheetId, out var resolvedSheetId, out var strippedRemainder))
        {
            scopedNameSheetId = resolvedSheetId;
            if (!string.IsNullOrWhiteSpace(strippedRemainder))
                nameLookupText = strippedRemainder;
        }

        if (resolveScopedFormula?.Invoke(nameLookupText, scopedNameSheetId) is not null)
            return true;

        return namedFormulas is not null && namedFormulas.ContainsKey(nameLookupText);
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
