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
            var cycled = string.IsNullOrEmpty(secondCellReference)
                ? $"{qualifier}{cycledCellReference}"
                : $"{qualifier}{cycledCellReference}:{CycleMatchingReference(secondCellReference)}";
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

    private static bool IsValidA1Reference(string column, string rowText)
    {
        if (!uint.TryParse(rowText, out var row) || row is < 1 or > CellAddress.MaxRow)
            return false;

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

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?<qualifier>(?:(?:'(?:[^']|'')+'|(?:\[[^\]]+\])?[A-Za-z_][A-Za-z0-9_ .]*)(?::(?:'(?:[^']|'')+'|(?:\[[^\]]+\])?[A-Za-z_][A-Za-z0-9_ .]*))?!)?)(?<cell>\$?[A-Z]{1,3}\$?[1-9][0-9]{0,6})(?::(?<cell2>\$?[A-Z]{1,3}\$?[1-9][0-9]{0,6}))?(?![A-Za-z0-9_])", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex A1ReferenceRegex();

    [GeneratedRegex(@"^\$?(?<column>[A-Z]{1,3})\$?(?<row>[1-9][0-9]{0,6})$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex A1PartsRegex();
}
