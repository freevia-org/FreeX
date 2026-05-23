using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record FormulaReferenceHighlight(
    int TextStart,
    int TextLength,
    int PaletteIndex,
    string Text,
    string? SheetName,
    GridRange? Range);

public static class FormulaReferenceHighlightPlanner
{
    private const int PaletteSize = 6;

    public static IReadOnlyList<FormulaReferenceHighlight> GetHighlights(
        string text,
        SheetId currentSheetId,
        Func<string, SheetId?>? resolveSheetId)
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

            if (TryReadReference(text, index, currentSheetId, resolveSheetId, highlights.Count % PaletteSize, out var highlight, out var nextIndex))
            {
                highlights.Add(highlight);
                index = nextIndex;
                continue;
            }

            index = Math.Max(index + 1, nextIndex);
        }

        return highlights;
    }

    private static bool TryReadReference(
        string text,
        int start,
        SheetId currentSheetId,
        Func<string, SheetId?>? resolveSheetId,
        int paletteIndex,
        out FormulaReferenceHighlight highlight,
        out int nextIndex)
    {
        highlight = default!;
        nextIndex = start + 1;

        if (!IsReferenceBoundaryBefore(text, start))
            return false;

        var referenceStart = start;
        string? sheetName = null;
        var sheetId = currentSheetId;
        var cellStart = start;

        if (TryReadSheetQualifier(text, start, out var parsedSheetName, out var afterQualifier))
        {
            sheetName = parsedSheetName;
            referenceStart = start;
            cellStart = afterQualifier;
            sheetId = resolveSheetId?.Invoke(sheetName) ?? currentSheetId;
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
            if (TryReadSheetQualifier(text, rangeCellStart, out var endSheetName, out var afterEndQualifier))
            {
                if (sheetName is null || string.Equals(sheetName, endSheetName, StringComparison.OrdinalIgnoreCase))
                    rangeCellStart = afterEndQualifier;
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
        var range = new GridRange(firstCell, secondCell);
        highlight = new FormulaReferenceHighlight(
            referenceStart,
            referenceEnd - referenceStart,
            paletteIndex,
            text[referenceStart..referenceEnd],
            sheetName,
            range);
        return true;
    }

    private static bool TryReadSheetQualifier(string text, int start, out string sheetName, out int afterQualifier)
    {
        sheetName = "";
        afterQualifier = start;

        if (start >= text.Length)
            return false;

        if (text[start] == '\'')
            return TryReadQuotedSheetQualifier(text, start, out sheetName, out afterQualifier);

        var index = start;
        while (index < text.Length && IsUnquotedSheetNameChar(text[index]))
            index++;

        if (index == start || index >= text.Length || text[index] != '!')
            return false;

        sheetName = text[start..index];
        afterQualifier = index + 1;
        return true;
    }

    private static bool TryReadQuotedSheetQualifier(string text, int start, out string sheetName, out int afterQualifier)
    {
        sheetName = "";
        afterQualifier = start;
        var chars = new List<char>();
        var index = start + 1;

        while (index < text.Length)
        {
            if (text[index] == '\'')
            {
                if (index + 1 < text.Length && text[index + 1] == '\'')
                {
                    chars.Add('\'');
                    index += 2;
                    continue;
                }

                if (index + 1 < text.Length && text[index + 1] == '!')
                {
                    sheetName = new string(chars.ToArray());
                    afterQualifier = index + 2;
                    return sheetName.Length > 0;
                }

                return false;
            }

            chars.Add(text[index]);
            index++;
        }

        return false;
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

    private static bool IsReferenceBoundaryBefore(string text, int start) =>
        start <= 0 || !IsIdentifierContinuation(text[start - 1]);

    private static bool IsReferenceBoundaryAfter(string text, int end) =>
        end >= text.Length || !IsIdentifierContinuation(text[end]);

    private static bool IsIdentifierContinuation(char ch) =>
        char.IsLetterOrDigit(ch) || ch is '_' or '$' or '.';

    private static bool IsUnquotedSheetNameChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch is '_' or '.';
}
