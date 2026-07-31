using FreeX.Core.Model;

namespace FreeX.App.Presentation;

public sealed record FormulaReferenceHighlight(
    int TextStart,
    int TextLength,
    int PaletteIndex,
    string Text,
    string? SheetName,
    GridRange? Range,
    string? SheetEndName = null,
    string? ExternalWorkbookName = null);

/// <summary>
/// Decoded sheet qualifier metadata shared by formula highlighting and formula reference editing.
/// The source span remains available through <see cref="TextLength"/> and <see cref="AfterQualifier"/>.
/// </summary>
public readonly record struct FormulaReferenceSheetQualifier(
    string StartSheetName,
    string? EndSheetName,
    int AfterQualifier,
    int TextLength,
    string? ExternalWorkbookName = null)
{
    public bool IsSpan => !string.IsNullOrWhiteSpace(EndSheetName);
}

public static class FormulaReferenceHighlightPlanner
{
    private const int PaletteSize = 6;

    public static IReadOnlyList<FormulaReferenceHighlight> GetHighlights(
        string text,
        SheetId currentSheetId,
        Func<string, SheetId?>? resolveSheetId)
        => GetHighlights(text, currentSheetId, resolveSheetId, resolveStructuredReference: null);

    public static IReadOnlyList<FormulaReferenceHighlight> GetHighlights(
        string text,
        SheetId currentSheetId,
        Func<string, SheetId?>? resolveSheetId,
        Func<string, string, GridRange?>? resolveStructuredReference)
        => GetHighlights(text, currentSheetId, resolveSheetId, resolveStructuredReference, null);

    public static IReadOnlyList<FormulaReferenceHighlight> GetHighlights(
        string text,
        SheetId currentSheetId,
        Func<string, SheetId?>? resolveSheetId,
        Func<string, string, GridRange?>? resolveStructuredReference,
        Func<SheetId, int?>? resolveSheetIndex)
    {
        if (!text.StartsWith("=", StringComparison.Ordinal))
            return [];

        var highlights = new List<FormulaReferenceHighlight>();
        var index = 1;
        while (index < text.Length)
        {
            if (text[index] == '"')
            {
                index = SkipStringLiteral(text, index);
                continue;
            }

            if (TryReadReference(text, index, currentSheetId, resolveSheetId, highlights.Count % PaletteSize, resolveSheetIndex, out var highlight, out var nextIndex))
            {
                highlights.Add(highlight);
                index = nextIndex;
                continue;
            }

            if (TryReadStructuredReference(text, index, resolveStructuredReference, highlights.Count % PaletteSize, out var structuredHighlight, out var structuredNextIndex))
            {
                highlights.Add(structuredHighlight);
                index = structuredNextIndex;
                continue;
            }

            if (text[index] == '[')
            {
                index = SkipStructuredReferenceSelector(text, index);
                continue;
            }

            index = Math.Max(index + 1, nextIndex);
        }

        return highlights;
    }

    private static bool TryReadStructuredReference(
        string text,
        int start,
        Func<string, string, GridRange?>? resolveStructuredReference,
        int paletteIndex,
        out FormulaReferenceHighlight highlight,
        out int nextIndex)
    {
        highlight = default!;
        nextIndex = start + 1;

        if (resolveStructuredReference is null || !IsReferenceBoundaryBefore(text, start))
            return false;

        var tableName = "";
        var selectorStart = start;
        if (text[start] != '[')
        {
            var tableEnd = start;
            while (tableEnd < text.Length && IsUnquotedSheetNameChar(text[tableEnd]))
                tableEnd++;

            if (tableEnd == start || tableEnd >= text.Length || text[tableEnd] != '[')
                return false;

            tableName = text[start..tableEnd];
            selectorStart = tableEnd;
        }

        if (!TryReadBalancedStructuredSelector(text, selectorStart, out var selector, out var selectorEnd))
        {
            nextIndex = selectorEnd;
            return false;
        }

        if (!IsReferenceBoundaryAfter(text, selectorEnd))
        {
            nextIndex = selectorEnd;
            return false;
        }

        var range = resolveStructuredReference(tableName, selector);
        if (range is null)
        {
            nextIndex = selectorEnd;
            return false;
        }

        nextIndex = selectorEnd;
        highlight = new FormulaReferenceHighlight(
            start,
            selectorEnd - start,
            paletteIndex,
            text[start..selectorEnd],
            SheetName: null,
            range);
        return true;
    }

    private static bool TryReadBalancedStructuredSelector(string text, int start, out string selector, out int end)
    {
        selector = "";
        end = start + 1;
        if (start >= text.Length || text[start] != '[')
            return false;

        var depth = 0;
        for (var index = start; index < text.Length; index++)
        {
            if (text[index] == '[')
            {
                depth++;
                continue;
            }

            if (text[index] != ']')
                continue;

            depth--;
            if (depth == 0)
            {
                end = index + 1;
                selector = text[(start + 1)..index].Trim();
                return selector.Length > 0;
            }
        }

        end = text.Length;
        return false;
    }

    private static bool TryReadReference(
        string text,
        int start,
        SheetId currentSheetId,
        Func<string, SheetId?>? resolveSheetId,
        int paletteIndex,
        Func<SheetId, int?>? resolveSheetIndex,
        out FormulaReferenceHighlight highlight,
        out int nextIndex)
    {
        highlight = default!;
        nextIndex = start + 1;

        if (!IsReferenceBoundaryBefore(text, start))
            return false;

        var referenceStart = start;
        string? sheetName = null;
        string? sheetEndName = null;
        string? externalWorkbookName = null;
        var sheetId = currentSheetId;
        var canRenderReference = true;
        var cellStart = start;

        if (TryParseSheetQualifier(text, start, out var qualifier))
        {
            sheetName = qualifier.StartSheetName;
            sheetEndName = qualifier.EndSheetName;
            externalWorkbookName = qualifier.ExternalWorkbookName;
            referenceStart = start;
            cellStart = qualifier.AfterQualifier;
            var resolvedStartSheetId = externalWorkbookName is null
                ? resolveSheetId?.Invoke(sheetName)
                : null;
            sheetId = resolvedStartSheetId ?? currentSheetId;
            if (externalWorkbookName is not null)
            {
                // The source workbook is not loaded into this presentation context yet. Keep
                // the full external token highlighted, but do not project its coordinates onto
                // a same-named local sheet or draw a false local grid overlay.
                canRenderReference = false;
            }
            else if (sheetEndName is not null)
            {
                var endSheetId = resolveSheetId?.Invoke(sheetEndName);
                canRenderReference = resolvedStartSheetId is not null && endSheetId is not null &&
                    IsCurrentSheetInSpan(currentSheetId, resolvedStartSheetId.Value, endSheetId.Value, resolveSheetIndex);
                // Parse the body against a resolved endpoint even when the active worksheet is
                // outside the span. This keeps an otherwise valid 3-D token atomic in the outer
                // scanner, preventing its A1:B2 body from being rediscovered as a local reference.
                sheetId = resolvedStartSheetId ?? currentSheetId;
                if (canRenderReference)
                    sheetId = currentSheetId;
            }
        }

        // Whole-column (A:A) / whole-row (3:3) references have no row-in-the-first-token or
        // column-in-the-first-token respectively, so TryReadCell below (which requires both a
        // column and a row) can never parse them. Try that shape first; Excel boxes these
        // references in the formula bar exactly like any other reference.
        if (TryReadWholeColumnOrRow(text, cellStart, sheetId, out var wholeRange, out var wholeEnd))
        {
            if (!IsReferenceBoundaryAfter(text, wholeEnd))
            {
                nextIndex = wholeEnd;
                return false;
            }

            nextIndex = wholeEnd;
            if (!canRenderReference && externalWorkbookName is null)
                return false;

            highlight = new FormulaReferenceHighlight(
                referenceStart,
                wholeEnd - referenceStart,
                paletteIndex,
                text[referenceStart..wholeEnd],
                sheetName,
                canRenderReference ? wholeRange : null,
                sheetEndName,
                externalWorkbookName);
            return true;
        }

        if (!TryReadCell(text, cellStart, sheetId, out var firstCell, out var cellEnd, out var invalidEnd))
        {
            nextIndex = Math.Max(nextIndex, invalidEnd);
            return false;
        }

        var secondCell = firstCell;
        var referenceEnd = cellEnd;
        if (cellEnd < text.Length && text[cellEnd] == ':')
        {
            var rangeCellStart = cellEnd + 1;
            if (TryParseSheetQualifier(text, rangeCellStart, out var endQualifier))
            {
                if (sheetName is null ||
                    (sheetEndName is null && string.Equals(sheetName, endQualifier.StartSheetName, StringComparison.OrdinalIgnoreCase)))
                {
                    rangeCellStart = endQualifier.AfterQualifier;
                }
            }

            if (TryReadCell(text, rangeCellStart, sheetId, out var parsedSecondCell, out var secondEnd, out _))
            {
                secondCell = parsedSecondCell;
                referenceEnd = secondEnd;
            }
        }

        if (!IsReferenceBoundaryAfter(text, referenceEnd))
        {
            nextIndex = referenceEnd;
            return false;
        }

        nextIndex = referenceEnd;
        if (!canRenderReference && externalWorkbookName is null)
            return false;

        var range = new GridRange(firstCell, secondCell);
        highlight = new FormulaReferenceHighlight(
            referenceStart,
            referenceEnd - referenceStart,
            paletteIndex,
            text[referenceStart..referenceEnd],
            sheetName,
            canRenderReference ? range : null,
            sheetEndName,
            externalWorkbookName);
        return true;
    }

    /// <summary>
    /// Parses a normal or Excel 3-D sheet qualifier. Both <c>Sheet1:Sheet3!</c> and the
    /// quoted whole-span form <c>'Sheet 1:Sheet 3'!</c> are accepted, including doubled
    /// apostrophes inside quoted names.
    /// </summary>
    public static bool TryParseSheetQualifier(
        string text,
        int start,
        out FormulaReferenceSheetQualifier qualifier)
    {
        qualifier = default;
        if (start < 0 || start >= text.Length)
            return false;

        var qualifierStart = start;
        string? externalWorkbookName = null;
        if (TryReadExternalWorkbookPrefix(text, start, out var workbookName, out var afterWorkbook))
        {
            externalWorkbookName = workbookName;
            start = afterWorkbook;
        }

        if (!TryReadSheetNamePart(text, start, out var firstName, out var afterFirst, out var firstWasQuoted))
            return false;

        if (externalWorkbookName is null && TrySplitExternalWorkbookName(firstName, out var embeddedWorkbook, out var embeddedSheet))
        {
            externalWorkbookName = embeddedWorkbook;
            firstName = embeddedSheet;
        }

        string? endName = null;
        var afterName = afterFirst;
        if (afterFirst < text.Length && text[afterFirst] == ':')
        {
            if (!TryReadSheetNamePart(text, afterFirst + 1, out endName, out afterName, out _))
                return false;
        }

        if (firstWasQuoted && endName is null)
        {
            var separator = firstName.IndexOf(':');
            if (separator > 0 && separator < firstName.Length - 1)
            {
                endName = firstName[(separator + 1)..];
                firstName = firstName[..separator];
            }
        }

        if (afterName >= text.Length || text[afterName] != '!')
        {
            // A quoted qualifier can quote the complete span, so split its decoded content.
            if (!firstWasQuoted || endName is not null)
                return false;

            var separator = firstName.IndexOf(':');
            if (separator <= 0 || separator == firstName.Length - 1 || afterFirst >= text.Length || text[afterFirst] != '!')
                return false;

            endName = firstName[(separator + 1)..];
            firstName = firstName[..separator];
            afterName = afterFirst;
        }

        var afterQualifier = afterName + 1;
        qualifier = new FormulaReferenceSheetQualifier(
            firstName,
            endName,
            afterQualifier,
            afterQualifier - qualifierStart,
            externalWorkbookName);
        return true;
    }

    private static bool TryReadExternalWorkbookPrefix(
        string text,
        int start,
        out string workbookName,
        out int afterPrefix)
    {
        workbookName = "";
        afterPrefix = start;
        if (start >= text.Length || text[start] != '[')
            return false;

        var close = text.IndexOf(']', start + 1);
        if (close <= start + 1)
            return false;

        workbookName = text[(start + 1)..close];
        afterPrefix = close + 1;
        return true;
    }

    private static bool TrySplitExternalWorkbookName(
        string sheetName,
        out string workbookName,
        out string remainingSheetName)
    {
        workbookName = "";
        remainingSheetName = sheetName;
        if (!sheetName.StartsWith("[", StringComparison.Ordinal))
            return false;

        var close = sheetName.IndexOf(']');
        if (close <= 1 || close == sheetName.Length - 1)
            return false;

        workbookName = sheetName[1..close];
        remainingSheetName = sheetName[(close + 1)..];
        return true;
    }

    private static bool TryReadSheetNamePart(
        string text,
        int start,
        out string sheetName,
        out int afterName,
        out bool wasQuoted)
    {
        sheetName = "";
        afterName = start;
        wasQuoted = false;
        if (start >= text.Length)
            return false;

        if (text[start] != '\'')
        {
            var index = start;
            while (index < text.Length && IsUnquotedSheetNameChar(text[index]))
                index++;
            if (index == start)
                return false;

            sheetName = text[start..index];
            afterName = index;
            return true;
        }

        var quoteIndex = start + 1;
        while (quoteIndex < text.Length)
        {
            if (text[quoteIndex] != '\'')
            {
                quoteIndex++;
                continue;
            }

            if (quoteIndex + 1 < text.Length && text[quoteIndex + 1] == '\'')
            {
                quoteIndex += 2;
                continue;
            }

            var rawSlice = text[(start + 1)..quoteIndex];
            sheetName = rawSlice.Contains("''", StringComparison.Ordinal)
                ? rawSlice.Replace("''", "'", StringComparison.Ordinal)
                : rawSlice;
            afterName = quoteIndex + 1;
            wasQuoted = sheetName.Length > 0;
            return wasQuoted;
        }

        return false;
    }

    private static bool IsCurrentSheetInSpan(
        SheetId currentSheetId,
        SheetId startSheetId,
        SheetId endSheetId,
        Func<SheetId, int?>? resolveSheetIndex)
    {
        if (resolveSheetIndex is null)
            return currentSheetId == startSheetId || currentSheetId == endSheetId;

        var currentIndex = resolveSheetIndex(currentSheetId);
        var startIndex = resolveSheetIndex(startSheetId);
        var endIndex = resolveSheetIndex(endSheetId);
        return currentIndex is { } current && startIndex is { } first && endIndex is { } last &&
            current >= Math.Min(first, last) && current <= Math.Max(first, last);
    }

    private static bool TryReadCell(
        string text,
        int start,
        SheetId sheetId,
        out CellAddress cell,
        out int end,
        out int invalidEnd)
    {
        cell = default;
        end = start;
        invalidEnd = start + 1;

        var index = start;
        if (index < text.Length && text[index] == '$')
            index++;

        var columnStart = index;
        while (index < text.Length && char.IsAsciiLetter(text[index]))
            index++;

        if (index == columnStart)
            return false;

        var columnText = text[columnStart..index];
        if (index < text.Length && text[index] == '$')
            index++;

        var rowStart = index;
        while (index < text.Length && char.IsDigit(text[index]))
            index++;

        invalidEnd = index;
        if (index == rowStart)
            return false;

        if (index < text.Length && IsIdentifierContinuation(text[index]))
            return false;

        var column = CellAddress.ColumnNameToNumber(columnText);
        if (!uint.TryParse(text[rowStart..index], out var row) ||
            row is 0 or > CellAddress.MaxRow ||
            column is 0 or > CellAddress.MaxCol)
        {
            return false;
        }

        cell = new CellAddress(sheetId, row, column);
        end = index;
        return true;
    }

    /// <summary>
    /// Tries to read a whole-column pair ("A:A", "$A:$C") or whole-row pair ("3:3", "$1:$5")
    /// starting at <paramref name="start"/>, mirroring the formula parser's convention that a
    /// whole-column reference spans row 1..MaxRow and a whole-row reference spans col A..MaxCol
    /// (Parser.cs ParseFullColumnRangePart/ParseFullRowRangePart).
    /// </summary>
    private static bool TryReadWholeColumnOrRow(
        string text,
        int start,
        SheetId sheetId,
        out GridRange range,
        out int end)
    {
        if (TryReadWholeColumnPair(text, start, sheetId, out range, out end))
            return true;

        return TryReadWholeRowPair(text, start, sheetId, out range, out end);
    }

    private static bool TryReadWholeColumnPair(
        string text,
        int start,
        SheetId sheetId,
        out GridRange range,
        out int end)
    {
        range = default;
        end = start;

        if (!TryReadColumnOnly(text, start, out var firstColumn, out var afterFirst))
            return false;

        if (afterFirst >= text.Length || text[afterFirst] != ':')
            return false;

        if (!TryReadColumnOnly(text, afterFirst + 1, out var secondColumn, out var afterSecond))
            return false;

        end = afterSecond;
        range = new GridRange(
            new CellAddress(sheetId, 1, firstColumn),
            new CellAddress(sheetId, CellAddress.MaxRow, secondColumn));
        return true;
    }

    private static bool TryReadWholeRowPair(
        string text,
        int start,
        SheetId sheetId,
        out GridRange range,
        out int end)
    {
        range = default;
        end = start;

        if (!TryReadRowOnly(text, start, out var firstRow, out var afterFirst))
            return false;

        if (afterFirst >= text.Length || text[afterFirst] != ':')
            return false;

        if (!TryReadRowOnly(text, afterFirst + 1, out var secondRow, out var afterSecond))
            return false;

        end = afterSecond;
        range = new GridRange(
            new CellAddress(sheetId, firstRow, 1),
            new CellAddress(sheetId, secondRow, CellAddress.MaxCol));
        return true;
    }

    /// <summary>
    /// Reads a bare column token ("A", "$AB") with no trailing row digits. Used only for the
    /// whole-column-pair shape ("A:A") -- a column token that IS followed by row digits is a
    /// normal cell reference and must fall through to <see cref="TryReadCell"/> instead.
    /// </summary>
    private static bool TryReadColumnOnly(string text, int start, out uint column, out int end)
    {
        column = 0;
        end = start;

        var index = start;
        if (index < text.Length && text[index] == '$')
            index++;

        var columnStart = index;
        while (index < text.Length && char.IsAsciiLetter(text[index]))
            index++;

        if (index == columnStart || (index < text.Length && char.IsDigit(text[index])))
            return false;

        var columnNumber = CellAddress.ColumnNameToNumber(text[columnStart..index]);
        if (columnNumber is 0 || columnNumber > CellAddress.MaxCol)
            return false;

        column = columnNumber;
        end = index;
        return true;
    }

    /// <summary>
    /// Reads a bare row token ("3", "$15") with no leading column letters. Used only for the
    /// whole-row-pair shape ("3:3").
    /// </summary>
    private static bool TryReadRowOnly(string text, int start, out uint row, out int end)
    {
        row = 0;
        end = start;

        var index = start;
        if (index < text.Length && text[index] == '$')
            index++;

        var rowStart = index;
        while (index < text.Length && char.IsDigit(text[index]))
            index++;

        if (index == rowStart)
            return false;

        if (!uint.TryParse(text[rowStart..index], out var rowNumber) ||
            rowNumber is 0 || rowNumber > CellAddress.MaxRow)
        {
            return false;
        }

        row = rowNumber;
        end = index;
        return true;
    }

    private static int SkipStringLiteral(string text, int start)
    {
        var index = start + 1;
        while (index < text.Length)
        {
            if (text[index] == '"')
            {
                index++;
                if (index < text.Length && text[index] == '"')
                {
                    index++;
                    continue;
                }

                return index;
            }

            index++;
        }

        return text.Length;
    }

    private static int SkipStructuredReferenceSelector(string text, int start)
    {
        var depth = 0;
        var index = start;

        while (index < text.Length)
        {
            if (text[index] == '[')
            {
                depth++;
            }
            else if (text[index] == ']')
            {
                depth--;
                if (depth == 0)
                    return index + 1;
            }

            index++;
        }

        return start + 1;
    }

    private static bool IsReferenceBoundaryBefore(string text, int start) =>
        start <= 0 || !IsIdentifierContinuation(text[start - 1]);

    private static bool IsReferenceBoundaryAfter(string text, int end) =>
        end >= text.Length || !IsIdentifierContinuation(text[end]);

    private static bool IsIdentifierContinuation(char ch) =>
        char.IsLetterOrDigit(ch) || ch is '_' or '$' or '.';

    private static bool IsUnquotedSheetNameChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch is '_' or '.';
}
