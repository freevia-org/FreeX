using System.Text.RegularExpressions;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static partial class FormulaReferenceStyleService
{
    public static string ToR1C1(string a1FormulaText, CellAddress anchor) =>
        ReplaceOutsideIgnoredSpans(a1FormulaText, A1ReferenceRegex(), FindA1IgnoredIndexes(a1FormulaText), match =>
        {
            var colAbsolute = match.Groups["colAbs"].Value == "$";
            var rowAbsolute = match.Groups["rowAbs"].Value == "$";
            var col = CellAddress.ColumnNameToNumber(match.Groups["col"].Value);
            if (!uint.TryParse(match.Groups["row"].Value, out var row))
                return match.Value;

            if (row is < 1 or > CellAddress.MaxRow || col is < 1 or > CellAddress.MaxCol)
                return match.Value;

            return FormatR1C1(row, col, rowAbsolute, colAbsolute, anchor);
        });

    public static string ToA1(string r1c1FormulaText, CellAddress anchor) =>
        ReplaceOutsideIgnoredSpans(r1c1FormulaText, R1C1ReferenceRegex(), FindFormulaIgnoredIndexes(r1c1FormulaText), match =>
        {
            var rowText = match.Groups["row"].Value;
            var colText = match.Groups["col"].Value;
            if (!TryResolveR1C1Part(rowText, anchor.Row, out var row, out var rowAbsolute) ||
                !TryResolveR1C1Part(colText, anchor.Col, out var col, out var colAbsolute))
            {
                return match.Value;
            }

            if (row is < 1 or > CellAddress.MaxRow || col is < 1 or > CellAddress.MaxCol)
                return match.Value;

            return $"{(colAbsolute ? "$" : "")}{CellAddress.NumberToColumnName((uint)col)}{(rowAbsolute ? "$" : "")}{row}";
        });

    private static string ReplaceOutsideIgnoredSpans(
        string text,
        Regex regex,
        HashSet<int> ignoredIndexes,
        MatchEvaluator evaluator)
    {
        return regex.Replace(text, match =>
            ignoredIndexes.Contains(match.Index)
                ? match.Value
                : evaluator(match));
    }

    private static HashSet<int> FindA1IgnoredIndexes(string text) => FindFormulaIgnoredIndexes(text);

    private static HashSet<int> FindFormulaIgnoredIndexes(string text)
    {
        var indexes = FindStringLiteralIndexes(text);
        AddQuotedSheetQualifierIndexes(text, indexes);
        AddExternalWorkbookNameIndexes(text, indexes);
        AddStructuredReferenceIndexes(text, indexes);
        return indexes;
    }

    private static HashSet<int> FindStringLiteralIndexes(string text)
    {
        var indexes = new HashSet<int>();
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '"')
            {
                if (inString)
                    indexes.Add(i);
                continue;
            }

            if (inString && i + 1 < text.Length && text[i + 1] == '"')
            {
                indexes.Add(i);
                indexes.Add(i + 1);
                i++;
                continue;
            }

            indexes.Add(i);
            inString = !inString;
        }

        return indexes;
    }

    private static void AddQuotedSheetQualifierIndexes(string text, HashSet<int> indexes)
    {
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                if (inString && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inString = !inString;
                continue;
            }

            if (inString || text[i] != '\'')
                continue;

            var close = FindClosingSheetQualifierQuote(text, i + 1);
            if (close < 0 || !IsFollowedBySheetReferenceBang(text, close + 1))
                continue;

            for (var j = i; j <= close; j++)
                indexes.Add(j);
            i = close;
        }
    }

    private static int FindClosingSheetQualifierQuote(string text, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] != '\'')
                continue;

            if (i + 1 < text.Length && text[i + 1] == '\'')
            {
                i++;
                continue;
            }

            return i;
        }

        return -1;
    }

    private static bool IsFollowedBySheetReferenceBang(string text, int start)
    {
        var i = start;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;

        return i < text.Length && text[i] == '!';
    }

    private static void AddExternalWorkbookNameIndexes(string text, HashSet<int> indexes)
    {
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                if (inString && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inString = !inString;
                continue;
            }

            if (inString || text[i] != '[' || !LooksLikeExternalWorkbookOpen(text, i))
                continue;

            for (var j = i + 1; j < text.Length; j++)
            {
                indexes.Add(j);
                if (text[j] == ']')
                    break;
            }
        }
    }

    private static bool LooksLikeExternalWorkbookOpen(string text, int bracketIndex)
    {
        var i = bracketIndex - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
            i--;

        return i < 0 || text[i] is '\'' or '(' or ',' or '=' or '+' or '-' or '*' or '/' or '^' or '&' or ':' or ';';
    }

    private static void AddStructuredReferenceIndexes(string text, HashSet<int> indexes)
    {
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                if (inString && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inString = !inString;
                continue;
            }

            if (inString || text[i] != '[' || !LooksLikeStructuredReferenceOpen(text, i))
                continue;

            var depth = 1;
            for (var j = i + 1; j < text.Length; j++)
            {
                indexes.Add(j);
                if (text[j] == '[')
                    depth++;
                else if (text[j] == ']' && --depth == 0)
                    break;
            }
        }
    }

    private static bool LooksLikeStructuredReferenceOpen(string text, int bracketIndex)
    {
        var i = bracketIndex - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
            i--;

        return i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == ']');
    }

    private static string FormatR1C1(uint row, uint col, bool rowAbsolute, bool colAbsolute, CellAddress anchor)
    {
        var rowPart = rowAbsolute ? row.ToString() : FormatRelativePart((long)row - anchor.Row);
        var colPart = colAbsolute ? col.ToString() : FormatRelativePart((long)col - anchor.Col);
        return $"R{rowPart}C{colPart}";
    }

    private static string FormatRelativePart(long offset) => offset == 0 ? "" : $"[{offset}]";

    private static bool TryResolveR1C1Part(string text, uint anchorValue, out long value, out bool absolute)
    {
        if (string.IsNullOrEmpty(text))
        {
            absolute = false;
            value = anchorValue;
            return true;
        }

        if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal))
        {
            absolute = false;
            if (!long.TryParse(text[1..^1], out var offset))
            {
                value = 0;
                return false;
            }

            try
            {
                value = checked(anchorValue + offset);
                return true;
            }
            catch (OverflowException)
            {
                value = 0;
                return false;
            }
        }

        absolute = true;
        return long.TryParse(text, out value);
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?<colAbs>\$?)(?<col>[A-Za-z]{1,3})(?<rowAbs>\$?)(?<row>[1-9][0-9]*)(?![A-Za-z0-9_])")]
    private static partial Regex A1ReferenceRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])R(?<row>(?:\[-?\d+\]|\d*)?)C(?<col>(?:\[-?\d+\]|\d*)?)(?![A-Za-z0-9_])", RegexOptions.IgnoreCase)]
    private static partial Regex R1C1ReferenceRegex();
}
