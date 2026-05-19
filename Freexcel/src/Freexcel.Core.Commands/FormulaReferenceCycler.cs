using System.Text.RegularExpressions;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static partial class FormulaReferenceCycler
{
    public static bool TryCycleReferenceAtCaret(
        string text,
        int caretIndex,
        out string result,
        out int selectionStart,
        out int selectionLength)
    {
        result = text;
        selectionStart = Math.Clamp(caretIndex, 0, text.Length);
        selectionLength = 0;

        foreach (Match match in A1ReferenceRegex().Matches(text))
        {
            var cellIndex = match.Groups["cell"].Index;
            if (IsInsideStructuredReference(text, cellIndex) || IsInsideStringLiteral(text, cellIndex))
                continue;

            if (!CaretTouchesMatch(caretIndex, match))
                continue;

            var reference = match.Value;
            var qualifier = match.Groups["qualifier"].Value;
            var cellReference = match.Groups["cell"].Value;
            if (!TryCycleReference(cellReference, out var cycledCellReference))
                continue;

            var secondCellReference = match.Groups["cell2"].Value;
            var secondQualifier = match.Groups["qualifier2"].Value;
            var cycled = string.IsNullOrEmpty(secondCellReference)
                ? $"{qualifier}{cycledCellReference}"
                : $"{qualifier}{cycledCellReference}:{secondQualifier}{CycleMatchingReference(secondCellReference)}";
            result = text.Remove(match.Index, match.Length).Insert(match.Index, cycled);
            selectionStart = match.Index;
            selectionLength = cycled.Length;
            return true;
        }

        foreach (Match match in ColumnReferenceRegex().Matches(text))
        {
            var columnIndex = match.Groups["column"].Index;
            if (IsInsideStructuredReference(text, columnIndex) || IsInsideStringLiteral(text, columnIndex))
                continue;

            if (!CaretTouchesMatch(caretIndex, match))
                continue;

            var qualifier = match.Groups["qualifier"].Value;
            var columnReference = match.Groups["column"].Value;
            var secondColumnReference = match.Groups["column2"].Value;
            if (!TryCycleColumnReference(columnReference, out var cycledColumnReference))
                continue;

            var cycled = $"{qualifier}{cycledColumnReference}:{CycleMatchingColumnReference(secondColumnReference)}";
            result = text.Remove(match.Index, match.Length).Insert(match.Index, cycled);
            selectionStart = match.Index;
            selectionLength = cycled.Length;
            return true;
        }

        foreach (Match match in RowReferenceRegex().Matches(text))
        {
            var rowIndex = match.Groups["row"].Index;
            if (IsInsideStructuredReference(text, rowIndex) || IsInsideStringLiteral(text, rowIndex))
                continue;

            if (!CaretTouchesMatch(caretIndex, match))
                continue;

            var qualifier = match.Groups["qualifier"].Value;
            var rowReference = match.Groups["row"].Value;
            var secondRowReference = match.Groups["row2"].Value;
            if (!TryCycleRowReference(rowReference, out var cycledRowReference))
                continue;

            var cycled = $"{qualifier}{cycledRowReference}:{CycleMatchingRowReference(secondRowReference)}";
            result = text.Remove(match.Index, match.Length).Insert(match.Index, cycled);
            selectionStart = match.Index;
            selectionLength = cycled.Length;
            return true;
        }

        return false;
    }

    private static string CycleMatchingReference(string reference) =>
        TryCycleReference(reference, out var cycled)
            ? cycled
            : reference;

    private static string CycleMatchingColumnReference(string reference) =>
        TryCycleColumnReference(reference, out var cycled)
            ? cycled
            : reference;

    private static string CycleMatchingRowReference(string reference) =>
        TryCycleRowReference(reference, out var cycled)
            ? cycled
            : reference;

    private static bool CaretTouchesMatch(int caretIndex, Match match) =>
        caretIndex >= match.Index && caretIndex <= match.Index + match.Length;

    private static bool IsInsideStructuredReference(string text, int index)
    {
        var open = text.LastIndexOf('[', Math.Clamp(index, 0, text.Length - 1));
        if (open < 0)
            return false;

        var close = text.IndexOf(']', open);
        return close >= index;
    }

    private static bool IsInsideStringLiteral(string text, int index)
    {
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '"')
                continue;

            if (inString && i + 1 < text.Length && text[i + 1] == '"')
            {
                i++;
                continue;
            }

            if (i >= index)
                return inString;

            inString = !inString;
        }

        return false;
    }

    private static bool TryCycleReference(string reference, out string cycled)
    {
        cycled = reference;
        var match = A1PartsRegex().Match(reference);
        if (!match.Success)
            return false;

        var column = match.Groups["column"].Value.ToUpperInvariant();
        var row = match.Groups["row"].Value;
        if (!IsValidA1Reference(column, row))
            return false;

        var absoluteColumn = reference.StartsWith('$');
        var absoluteRow = reference.Contains($"${row}", StringComparison.Ordinal);
        cycled = (absoluteColumn, absoluteRow) switch
        {
            (false, false) => $"${column}${row}",
            (true, true) => $"{column}${row}",
            (false, true) => $"${column}{row}",
            (true, false) => $"{column}{row}"
        };
        return true;
    }

    private static bool TryCycleColumnReference(string reference, out string cycled)
    {
        cycled = reference;
        var match = ColumnPartsRegex().Match(reference);
        if (!match.Success)
            return false;

        var column = match.Groups["column"].Value.ToUpperInvariant();
        if (!IsValidColumnReference(column))
            return false;

        var absoluteColumn = reference.StartsWith('$');
        cycled = absoluteColumn ? column : $"${column}";
        return true;
    }

    private static bool TryCycleRowReference(string reference, out string cycled)
    {
        cycled = reference;
        var match = RowPartsRegex().Match(reference);
        if (!match.Success)
            return false;

        var row = match.Groups["row"].Value;
        if (!IsValidRowReference(row))
            return false;

        var absoluteRow = reference.StartsWith('$');
        cycled = absoluteRow ? row : $"${row}";
        return true;
    }

    private static bool IsValidA1Reference(string column, string rowText)
    {
        if (!IsValidRowReference(rowText))
            return false;

        return IsValidColumnReference(column);
    }

    private static bool IsValidRowReference(string rowText) =>
        uint.TryParse(rowText, out var row) && row is >= 1 and <= CellAddress.MaxRow;

    private static bool IsValidColumnReference(string column)
    {
        try
        {
            var col = CellAddress.ColumnNameToNumber(column);
            return col is >= 1 and <= CellAddress.MaxCol;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?<qualifier>(?:(?:'(?:[^']|'')+'|(?:\[[^\]]+\])?[A-Za-z_][A-Za-z0-9_ .]*)(?::(?:'(?:[^']|'')+'|(?:\[[^\]]+\])?[A-Za-z_][A-Za-z0-9_ .]*))?!)?)(?<cell>\$?[A-Z]{1,3}\$?[1-9][0-9]{0,6})(?::(?<qualifier2>(?:(?:'(?:[^']|'')+'|(?:\[[^\]]+\])?[A-Za-z_][A-Za-z0-9_ .]*)(?::(?:'(?:[^']|'')+'|(?:\[[^\]]+\])?[A-Za-z_][A-Za-z0-9_ .]*))?!)?)(?<cell2>\$?[A-Z]{1,3}\$?[1-9][0-9]{0,6}))?(?![A-Za-z0-9_])", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex A1ReferenceRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?<qualifier>(?:(?:'(?:[^']|'')+'|(?:\[[^\]]+\])?[A-Za-z_][A-Za-z0-9_ .]*)(?::(?:'(?:[^']|'')+'|(?:\[[^\]]+\])?[A-Za-z_][A-Za-z0-9_ .]*))?!)?)(?<column>\$?[A-Z]{1,3}):(?<column2>\$?[A-Z]{1,3})(?![A-Za-z0-9_])", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ColumnReferenceRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?<qualifier>(?:(?:'(?:[^']|'')+'|(?:\[[^\]]+\])?[A-Za-z_][A-Za-z0-9_ .]*)(?::(?:'(?:[^']|'')+'|(?:\[[^\]]+\])?[A-Za-z_][A-Za-z0-9_ .]*))?!)?)(?<row>\$?[1-9][0-9]{0,6}):(?<row2>\$?[1-9][0-9]{0,6})(?![A-Za-z0-9_])", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex RowReferenceRegex();

    [GeneratedRegex(@"^\$?(?<column>[A-Z]{1,3})\$?(?<row>[1-9][0-9]{0,6})$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex A1PartsRegex();

    [GeneratedRegex(@"^\$?(?<column>[A-Z]{1,3})$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ColumnPartsRegex();

    [GeneratedRegex(@"^\$?(?<row>[1-9][0-9]{0,6})$", RegexOptions.Compiled)]
    private static partial Regex RowPartsRegex();
}
