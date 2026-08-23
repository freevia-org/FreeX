using System.Text.RegularExpressions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class FormulaReferenceStyleService
{
    public static string ToR1C1(string a1FormulaText, CellAddress anchor) =>
        ReplaceOutsideIgnoredSpans(a1FormulaText, A1ReferenceRegex(), FindA1IgnoredIndexes(a1FormulaText), match =>
        {
            if (match.Groups["col"].Success)
            {
                var colAbsolute = match.Groups["colAbs"].Value == "$";
                var rowAbsolute = match.Groups["rowAbs"].Value == "$";
                var col = CellAddress.ColumnNameToNumber(match.Groups["col"].Value);
                if (!uint.TryParse(match.Groups["row"].Value, out var row))
                    return match.Value;

                if (row is < 1 or > CellAddress.MaxRow || col is < 1 or > CellAddress.MaxCol)
                    return match.Value;

                return FormatR1C1(row, col, rowAbsolute, colAbsolute, anchor);
            }

            // Whole-row range, e.g. "5:5" (a single entire row) or "1:3" (rows 1 through 3). Excel's
            // R1C1 display always uses the plain absolute R<n> form for these, regardless of whether the
            // A1 text carried a "$" - there is no meaningful "relative to the active cell" reading for an
            // entire row/column boundary written this way.
            if (match.Groups["rowRangeA"].Success)
            {
                if (!uint.TryParse(match.Groups["rowRangeA"].Value, out var rowA) ||
                    !uint.TryParse(match.Groups["rowRangeB"].Value, out var rowB))
                    return match.Value;

                if (rowA is < 1 or > CellAddress.MaxRow || rowB is < 1 or > CellAddress.MaxRow)
                    return match.Value;

                return rowA == rowB ? $"R{rowA}" : $"R{rowA}:R{rowB}";
            }

            // Whole-column range, e.g. "A:A" (a single entire column) or "A:C" (columns A through C).
            {
                var colA = CellAddress.ColumnNameToNumber(match.Groups["colRangeA"].Value);
                var colB = CellAddress.ColumnNameToNumber(match.Groups["colRangeB"].Value);

                if (colA is < 1 or > CellAddress.MaxCol || colB is < 1 or > CellAddress.MaxCol)
                    return match.Value;

                return colA == colB ? $"C{colA}" : $"C{colA}:C{colB}";
            }
        });

    public static string ToA1(string r1c1FormulaText, CellAddress anchor) =>
        ReplaceOutsideIgnoredSpans(r1c1FormulaText, R1C1ReferenceRegex(), FindFormulaIgnoredIndexes(r1c1FormulaText), match =>
        {
            if (match.Groups["row"].Success)
            {
                var rowText = match.Groups["row"].Value;
                var colText = match.Groups["col"].Value;
                if (!R1C1ReferencePartResolver.TryResolve(rowText, anchor.Row, out var row, out var rowAbsolute) ||
                    !R1C1ReferencePartResolver.TryResolve(colText, anchor.Col, out var col, out var colAbsolute))
                {
                    return match.Value;
                }

                // Out of range -> emit a #REF! for just this token so the stored A1 formula stays
                // parseable, mirroring R1C1FormulaConverter.FormatA1's handling for the file-format path.
                if (row is < 1 or > CellAddress.MaxRow || col is < 1 or > CellAddress.MaxCol)
                    return "#REF!";

                return $"{(colAbsolute ? "$" : "")}{CellAddress.NumberToColumnName((uint)col)}{(rowAbsolute ? "$" : "")}{row}";
            }

            // Whole-row reference range typed directly, e.g. "R1:R3".
            if (match.Groups["rowRangeA"].Success)
            {
                if (!R1C1ReferencePartResolver.TryResolve(match.Groups["rowRangeA"].Value, anchor.Row, out var rowA, out var rowAbsA) ||
                    !R1C1ReferencePartResolver.TryResolve(match.Groups["rowRangeB"].Value, anchor.Row, out var rowB, out var rowAbsB))
                    return match.Value;

                if (rowA is < 1 or > CellAddress.MaxRow || rowB is < 1 or > CellAddress.MaxRow)
                    return "#REF!";

                return $"{FormatA1RowSide(rowA, rowAbsA)}:{FormatA1RowSide(rowB, rowAbsB)}";
            }

            // Whole-column reference range typed directly, e.g. "C1:C3".
            if (match.Groups["colRangeA"].Success)
            {
                if (!R1C1ReferencePartResolver.TryResolve(match.Groups["colRangeA"].Value, anchor.Col, out var colA, out var colAbsA) ||
                    !R1C1ReferencePartResolver.TryResolve(match.Groups["colRangeB"].Value, anchor.Col, out var colB, out var colAbsB))
                    return match.Value;

                if (colA is < 1 or > CellAddress.MaxCol || colB is < 1 or > CellAddress.MaxCol)
                    return "#REF!";

                return $"{FormatA1ColSide(colA, colAbsA)}:{FormatA1ColSide(colB, colAbsB)}";
            }

            // A lone whole-row reference, e.g. "R5" (entire row 5) - Excel shows this in A1 as "5:5".
            if (match.Groups["rowOnly"].Success)
            {
                if (!R1C1ReferencePartResolver.TryResolve(match.Groups["rowOnly"].Value, anchor.Row, out var row, out var rowAbsolute))
                    return match.Value;

                if (row is < 1 or > CellAddress.MaxRow)
                    return "#REF!";

                var side = FormatA1RowSide(row, rowAbsolute);
                return $"{side}:{side}";
            }

            // A lone whole-column reference, e.g. "C1" (entire column 1/A) - Excel shows this in A1 as "A:A".
            {
                if (!R1C1ReferencePartResolver.TryResolve(match.Groups["colOnly"].Value, anchor.Col, out var col, out var colAbsolute))
                    return match.Value;

                if (col is < 1 or > CellAddress.MaxCol)
                    return "#REF!";

                var side = FormatA1ColSide(col, colAbsolute);
                return $"{side}:{side}";
            }
        });

    private static string FormatA1RowSide(long row, bool absolute) => $"{(absolute ? "$" : "")}{row}";

    private static string FormatA1ColSide(long col, bool absolute) =>
        $"{(absolute ? "$" : "")}{CellAddress.NumberToColumnName((uint)col)}";

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

    // A cell reference must not be followed by '(' - that shape is a function call whose name merely
    // looks like a column+row (e.g. LOG10(...) parses as col "LOG", row "10"), not a reference.
    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?:(?<colAbs>\$?)(?<col>[A-Za-z]{1,3})(?<rowAbs>\$?)(?<row>[1-9][0-9]*)|(?:\$)?(?<rowRangeA>[1-9][0-9]*):(?:\$)?(?<rowRangeB>[1-9][0-9]*)|(?:\$)?(?<colRangeA>[A-Za-z]{1,3}):(?:\$)?(?<colRangeB>[A-Za-z]{1,3}))(?![A-Za-z0-9_(])")]
    private static partial Regex A1ReferenceRegex();

    // Alternatives, tried in order: a full RxCy cell reference; a whole-row range "R1:R3"; a whole-column
    // range "C1:C3"; a lone whole-row reference "R5"; a lone whole-column reference "C1".
    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?:R(?<row>(?:\[-?\d+\]|\d*)?)C(?<col>(?:\[-?\d+\]|\d*)?)|R(?<rowRangeA>(?:\[-?\d+\]|\d+)):R(?<rowRangeB>(?:\[-?\d+\]|\d+))|C(?<colRangeA>(?:\[-?\d+\]|\d+)):C(?<colRangeB>(?:\[-?\d+\]|\d+))|R(?<rowOnly>(?:\[-?\d+\]|\d+))|C(?<colOnly>(?:\[-?\d+\]|\d+)))(?![A-Za-z0-9_])", RegexOptions.IgnoreCase)]
    private static partial Regex R1C1ReferenceRegex();
}
