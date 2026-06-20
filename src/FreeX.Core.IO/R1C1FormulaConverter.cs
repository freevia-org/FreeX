using System.Globalization;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Shared R1C1 ⇄ A1 formula reference conversion. Excel's SpreadsheetML 2003 (.xml) and SYLK (.slk)
/// both store formulas in R1C1 notation (e.g. <c>=RC[-1]+R[-1]C</c>); the model stores A1, so adapters
/// convert on read/write. The scanner copies string literals (<c>"…"</c> with the <c>""</c> escape) and
/// quoted sheet names (<c>'…'</c>) verbatim and only rewrites reference-shaped tokens, leaving function
/// names, numbers, defined names and operators untouched. A token that does not parse as a reference is
/// left exactly as-is.
/// </summary>
internal static class R1C1FormulaConverter
{
    /// <summary>Converts every R1C1 reference in <paramref name="formula"/> to A1 relative to the owning cell.</summary>
    public static string ToA1(string formula, uint row, uint col) =>
        RewriteReferences(formula, isR1C1Source: true, row, col);

    /// <summary>Converts every A1 reference in <paramref name="formula"/> to R1C1 relative to the owning cell.</summary>
    public static string ToR1C1(string formula, uint row, uint col) =>
        RewriteReferences(formula, isR1C1Source: false, row, col);

    /// <summary>
    /// Heuristic: does this formula contain at least one R1C1-style reference? Used on read so we only
    /// convert formulas that actually need it (A1 formulas pass through untouched). A bare <c>R</c>/<c>C</c>
    /// with no digit/bracket is a defined name, not a ref.
    /// </summary>
    public static bool LooksLikeR1C1(string formula)
    {
        for (var i = 0; i < formula.Length; i++)
        {
            if (formula[i] == '"')
            {
                i = SkipStringLiteral(formula, i) - 1;
                continue;
            }

            if (!IsRefStartLetter(formula[i]) || IsIdentifierChar(PreviousNonSheetChar(formula, i)))
                continue;

            var end = i;
            if (TryParseR1C1Reference(formula, ref end, out _, out _, out _, out _))
                return true;
        }

        return false;
    }

    private static string RewriteReferences(string formula, bool isR1C1Source, uint row, uint col)
    {
        var builder = new StringBuilder(formula.Length + 8);
        var i = 0;
        while (i < formula.Length)
        {
            var c = formula[i];
            if (c == '"' || c == '\'')
            {
                var end = c == '"' ? SkipStringLiteral(formula, i) : SkipQuotedSheetName(formula, i);
                builder.Append(formula, i, end - i);
                i = end;
                continue;
            }

            // Only attempt a reference parse at a token boundary (previous char is not part of an
            // identifier), so we never split a function name like "ROUND" or a defined name "Rate".
            var prev = i > 0 ? formula[i - 1] : '\0';
            if (IsRefStartLetter(c) && !IsIdentifierChar(prev) && prev != '.')
            {
                var end = i;
                if (isR1C1Source
                        ? TryParseR1C1Reference(formula, ref end, out var refRow, out var refCol, out var rowRel, out var colRel)
                        : TryParseA1Reference(formula, ref end, out refRow, out refCol, out rowRel, out colRel))
                {
                    builder.Append(isR1C1Source
                        ? FormatA1(refRow, refCol, rowRel, colRel, row, col)
                        : FormatR1C1(refRow, refCol, rowRel, colRel, row, col));
                    i = end;
                    continue;
                }
            }

            builder.Append(c);
            i++;
        }

        return builder.ToString();
    }

    // ---- R1C1 reference parsing -------------------------------------------------------------------

    private static bool TryParseR1C1Reference(
        string s, ref int index, out long row, out long col, out bool rowRelative, out bool colRelative)
    {
        row = 0; col = 0; rowRelative = false; colRelative = false;
        var i = index;

        if (i >= s.Length || (s[i] != 'R' && s[i] != 'r'))
            return false;
        i++;

        if (!TryReadR1C1Part(s, ref i, out row, out rowRelative))
            return false;

        if (i >= s.Length || (s[i] != 'C' && s[i] != 'c'))
            return false;
        i++;

        if (!TryReadR1C1Part(s, ref i, out col, out colRelative))
            return false;

        // Must end at a token boundary; otherwise this is part of a longer identifier (e.g. "RC_total")
        // or a function call.
        if (i < s.Length && (IsIdentifierChar(s[i]) || s[i] == '('))
            return false;

        index = i;
        return true;
    }

    private static bool TryReadR1C1Part(string s, ref int i, out long value, out bool relative)
    {
        value = 0;
        relative = true; // bare R / C means same row / col (relative offset 0)

        if (i < s.Length && s[i] == '[')
        {
            var close = s.IndexOf(']', i + 1);
            if (close < 0)
                return false;
            var inner = s.AsSpan(i + 1, close - i - 1);
            if (!long.TryParse(inner, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
                return false;
            relative = true;
            i = close + 1;
            return true;
        }

        // An absolute index is always written unsigned (e.g. R3C5). A leading '+'/'-' here is an
        // arithmetic operator after a bare R/C (e.g. "RC+1"), NOT a signed absolute index — only the
        // bracket form [n] carries a sign. So accept digits only.
        if (i < s.Length && char.IsDigit(s[i]))
        {
            var start = i;
            while (i < s.Length && char.IsDigit(s[i]))
                i++;

            if (!long.TryParse(s.AsSpan(start, i - start), NumberStyles.None, CultureInfo.InvariantCulture, out value))
                return false;
            relative = false; // absolute index
            return true;
        }

        return true; // bare R / C
    }

    private static string FormatA1(long row, long col, bool rowRel, bool colRel, uint baseRow, uint baseCol)
    {
        var absRow = rowRel ? (long)baseRow + row : row;
        var absCol = colRel ? (long)baseCol + col : col;

        // Out of range → emit a #REF! so the formula stays parseable instead of producing a bad address.
        if (absRow < 1 || absRow > CellAddress.MaxRow || absCol < 1 || absCol > CellAddress.MaxCol)
            return "#REF!";

        var colName = CellAddress.NumberToColumnName((uint)absCol);
        var colPrefix = colRel ? "" : "$";
        var rowPrefix = rowRel ? "" : "$";
        return $"{colPrefix}{colName}{rowPrefix}{absRow.ToString(CultureInfo.InvariantCulture)}";
    }

    // ---- A1 reference parsing (for the write direction) -------------------------------------------

    private static bool TryParseA1Reference(
        string s, ref int index, out long row, out long col, out bool rowRelative, out bool colRelative)
    {
        row = 0; col = 0; rowRelative = true; colRelative = true;
        var i = index;

        var colAbsolute = false;
        if (i < s.Length && s[i] == '$') { colAbsolute = true; i++; }

        var colStart = i;
        var colDigits = 0;
        uint colNum = 0;
        while (i < s.Length && IsColumnLetter(s[i]) && colDigits < 3)
        {
            colNum = colNum * 26 + (uint)(char.ToUpperInvariant(s[i]) - 'A' + 1);
            colDigits++;
            i++;
        }

        if (colDigits == 0 || colNum == 0 || colNum > CellAddress.MaxCol)
            return false;

        var rowAbsolute = false;
        if (i < s.Length && s[i] == '$') { rowAbsolute = true; i++; }

        var rowStart = i;
        while (i < s.Length && char.IsDigit(s[i]))
            i++;

        if (i == rowStart)
            return false;
        if (!uint.TryParse(s.AsSpan(rowStart, i - rowStart), out var rowNum) || rowNum == 0 || rowNum > CellAddress.MaxRow)
            return false;

        // Reject if followed by an identifier char (part of a longer name) or '(' (a function call like
        // LOG10(...) whose name happens to look like a column+row), so we never mistake a function for a
        // reference.
        if (i < s.Length && (IsIdentifierChar(s[i]) || s[i] == '('))
            return false;

        _ = colStart;
        row = rowNum;
        col = colNum;
        rowRelative = !rowAbsolute;
        colRelative = !colAbsolute;
        index = i;
        return true;
    }

    private static string FormatR1C1(long row, long col, bool rowRel, bool colRel, uint baseRow, uint baseCol)
    {
        var sb = new StringBuilder(8);
        sb.Append('R');
        if (rowRel)
        {
            var offset = row - baseRow;
            if (offset != 0)
                sb.Append('[').Append(offset.ToString(CultureInfo.InvariantCulture)).Append(']');
        }
        else
        {
            sb.Append(row.ToString(CultureInfo.InvariantCulture));
        }

        sb.Append('C');
        if (colRel)
        {
            var offset = col - baseCol;
            if (offset != 0)
                sb.Append('[').Append(offset.ToString(CultureInfo.InvariantCulture)).Append(']');
        }
        else
        {
            sb.Append(col.ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    // ---- scanning helpers -------------------------------------------------------------------------

    /// <summary>Advances past a <c>"…"</c> string literal (with <c>""</c> escapes). Returns index after the closing quote.</summary>
    private static int SkipStringLiteral(string s, int openQuote)
    {
        var i = openQuote + 1;
        while (i < s.Length)
        {
            if (s[i] == '"')
            {
                if (i + 1 < s.Length && s[i + 1] == '"')
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return i;
    }

    /// <summary>Advances past a <c>'…'</c> quoted sheet name (with <c>''</c> escapes).</summary>
    private static int SkipQuotedSheetName(string s, int openQuote)
    {
        var i = openQuote + 1;
        while (i < s.Length)
        {
            if (s[i] == '\'')
            {
                if (i + 1 < s.Length && s[i + 1] == '\'')
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return i;
    }

    private static bool IsRefStartLetter(char c) => c is 'R' or 'r' or 'C' or 'c' || IsColumnLetter(c) || c == '$';

    private static bool IsColumnLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsIdentifierChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '\\';

    private static char PreviousNonSheetChar(string s, int i) => i > 0 ? s[i - 1] : '\0';
}
