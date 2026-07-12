using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Converts spreadsheet formulas between FreeX's A1 model form and OpenDocument's OpenFormula
/// reference syntax. ODF wraps every cell/range reference in square brackets and prefixes the column
/// with a dot for the current sheet (<c>[.A1]</c>), or <c>[$'Sheet 2'.A1]</c> / <c>[$Sheet2.A1]</c>
/// for a cross-sheet reference. A range is <c>[.A1:.B2]</c>. A FreeX 3-D sheet-span reference (e.g.
/// <c>Sheet1:Sheet3!A1</c>, produced/consumed by Parser.cs's span grammar) is represented in
/// OpenFormula using distinct start/end sheet names on an ordinary bracketed range — <c>[$Sheet1.A1:
/// $Sheet3.A1]</c> — the same shape LibreOffice itself emits for a genuine 3-D reference. The stored
/// formula carries a leading <c>of:=</c> (or just <c>=</c>); this converter works on the body after
/// that prefix.
///
/// Only reference-shaped tokens are rewritten: string literals (<c>"…"</c> with the <c>""</c> escape)
/// pass through verbatim, function names / numbers / operators are untouched. The scan is deliberately
/// conservative — a token that does not parse as a reference is emitted exactly as-is.
/// </summary>
internal static class OdsFormulaConverter
{
    /// <summary>
    /// Converts an A1 formula body (no leading '=') to OpenFormula bracketed syntax. Sheet names that
    /// need quoting are wrapped in single quotes per ODF. FreeX's parser (like Excel's US locale) uses
    /// ',' as the function-argument separator, but OpenFormula requires ';' (Parser.cs only accepts
    /// ';' inside array constants); every top-level, non-string-literal ',' is therefore translated to
    /// ';' so multi-argument functions (IF, VLOOKUP, SUMIF, ...) stay valid in LibreOffice/Calc.
    /// Inside an array constant's <c>{...}</c>, FreeX already uses ';' as its own row separator
    /// (Parser.cs ParseArrayConstant), so translating its ',' column separator to ';' too would make
    /// row and column separators collide; OpenFormula's array syntax instead uses '|' for rows (its
    /// ';' matches the ordinary column/argument separator), so a ';' seen inside <c>{...}</c> is
    /// translated to '|' instead.
    /// </summary>
    public static string ToOdf(string a1Formula)
    {
        var builder = new StringBuilder(a1Formula.Length + 8);
        var i = 0;
        var arrayDepth = 0;
        while (i < a1Formula.Length)
        {
            var c = a1Formula[i];
            if (c == '"')
            {
                var end = SkipStringLiteral(a1Formula, i);
                builder.Append(a1Formula, i, end - i);
                i = end;
                continue;
            }

            var prev = i > 0 ? a1Formula[i - 1] : '\0';
            if (!IsIdentifierChar(prev) && prev != ']' &&
                TryReadA1Reference(a1Formula, i, out var end2, out var odf))
            {
                builder.Append(odf);
                i = end2;
                continue;
            }

            if (c == '{')
                arrayDepth++;
            else if (c == '}' && arrayDepth > 0)
                arrayDepth--;

            builder.Append(arrayDepth > 0 && c == ';' ? '|' : c == ',' ? ';' : c);
            i++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Converts an OpenFormula bracketed formula body to FreeX A1 form. Bracketed references become
    /// plain A1 (current-sheet) or <c>Sheet!A1</c> (cross-sheet); a leading <c>of:</c> namespace prefix
    /// on function names is stripped. OpenFormula's ';' argument separator is translated back to the
    /// ',' FreeX's parser expects (mirror of the translation <see cref="ToOdf"/> performs). Inside an
    /// array constant's <c>{...}</c>, OpenFormula's '|' row separator is translated back to FreeX's
    /// ';' (its ';' column separator still becomes ',', same as the ordinary case), mirroring
    /// <see cref="ToOdf"/>'s array-specific rewrite.
    /// </summary>
    public static string ToA1(string odfFormula)
    {
        // Drop the optional "of:" namespace prefix sometimes seen on the whole expression.
        var s = odfFormula;
        var builder = new StringBuilder(s.Length);
        var i = 0;
        var arrayDepth = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '"')
            {
                var end = SkipStringLiteral(s, i);
                builder.Append(s, i, end - i);
                i = end;
                continue;
            }

            if (c == '[')
            {
                var close = s.IndexOf(']', i + 1);
                if (close > i)
                {
                    var inner = s.Substring(i + 1, close - i - 1);
                    builder.Append(ConvertBracketRefToA1(inner));
                    i = close + 1;
                    continue;
                }
            }

            // Strip an "of:" function-namespace prefix (e.g. "of:SUM" -> "SUM").
            if ((c == 'o' || c == 'O') && i + 3 <= s.Length &&
                s.AsSpan(i).StartsWith("of:", StringComparison.OrdinalIgnoreCase) &&
                (i == 0 || !IsIdentifierChar(s[i - 1])))
            {
                i += 3;
                continue;
            }

            if (c == '{')
                arrayDepth++;
            else if (c == '}' && arrayDepth > 0)
                arrayDepth--;

            builder.Append(arrayDepth > 0 && c == '|' ? ';' : c == ';' ? ',' : c);
            i++;
        }

        return builder.ToString();
    }

    // ---- A1 -> ODF -------------------------------------------------------------------------------

    private static bool TryReadA1Reference(string s, int start, out int end, out string odf)
    {
        end = start;
        odf = "";

        // Optional sheet prefix: SheetName! or 'Sheet Name'!, or a 3-D sheet-span prefix
        // (StartSheet:EndSheet! / 'StartSheet:EndSheet'!, e.g. Sheet1:Sheet3!A1). A span is
        // represented in OpenFormula as an ordinary bracketed range whose two endpoints carry
        // different sheet names (the same shape FormatOdfRange already produces for
        // "Data!A1:Other!B2"), so once the span prefix is recognized the rest of this method's
        // existing range machinery handles it unchanged.
        var i = start;
        string? sheetPrefix = null;
        string? spanEndSheet = null;
        if (TryReadSheetSpanPrefix(s, ref i, out var spanStartSheet, out var spanEndSheetName))
        {
            sheetPrefix = spanStartSheet;
            spanEndSheet = spanEndSheetName;
        }
        else if (TryReadSheetPrefix(s, ref i, out var sheetName))
        {
            sheetPrefix = sheetName;
        }

        if (!TryReadCellRef(s, ref i, out var firstRef))
        {
            return false;
        }

        // Range?
        if (i < s.Length && s[i] == ':')
        {
            var afterColon = i + 1;
            var j = afterColon;
            // A range endpoint may itself carry a sheet prefix in 3-D refs; handle the common 1-sheet
            // case. A span's own range body is never itself re-sheet-qualified (Parser.cs rejects
            // e.g. "Sheet1:Sheet3!A1:Sheet1!B5"), so only look for one when this isn't already a span.
            string? secondSheet = null;
            if (spanEndSheet is null && TryReadSheetPrefix(s, ref j, out var sheet2))
                secondSheet = sheet2;
            if (TryReadCellRef(s, ref j, out var secondRef))
            {
                end = j;
                odf = FormatOdfRange(sheetPrefix, firstRef, spanEndSheet ?? secondSheet ?? sheetPrefix, secondRef);
                return true;
            }
        }

        end = i;
        odf = spanEndSheet is null
            ? FormatOdfRef(sheetPrefix, firstRef)
            // Bare single-cell span (e.g. Sheet1:Sheet3!A1, no range body): the same cell on both
            // endpoints, differing only by sheet — exactly what real 3-D references over a single
            // cell mean.
            : FormatOdfRange(sheetPrefix, firstRef, spanEndSheet, firstRef);
        return true;
    }

    /// <summary>
    /// Attempts to read FreeX's 3-D sheet-span prefix — <c>StartSheet:EndSheet!</c>, or the
    /// whole-span-quoted <c>'StartSheet:EndSheet'!</c> form FormulaSerializer emits when either sheet
    /// name needs quoting (Parser.cs's ':' cannot appear inside a real sheet name, so a colon found
    /// inside a quoted name unambiguously marks the span separator) — used by 3-D references like
    /// <c>SUM(Sheet1:Sheet3!A1)</c>. Distinct from an ordinary single-sheet prefix
    /// (<see cref="TryReadSheetPrefix"/>): here the ':' separates two sheet names ahead of a single
    /// '!', rather than joining two fully sheet-qualified "Sheet!Cell:Sheet!Cell" endpoints (which
    /// TryReadA1Reference's range branch already handles for its second endpoint). Only the
    /// both-quoted-together and both-unquoted forms are recognized — the two shapes
    /// FormulaSerializer.WriteSheetSpanName can actually produce; mixed quoting (only one side
    /// quoted) is left unrecognized and falls through unchanged, same as before this method existed.
    /// </summary>
    private static bool TryReadSheetSpanPrefix(string s, ref int index, out string startSheet, out string endSheet)
    {
        startSheet = "";
        endSheet = "";
        var i = index;
        if (i >= s.Length) return false;

        if (s[i] == '\'')
        {
            // Whole span quoted together: 'Start:End'!...
            var sb = new StringBuilder();
            var j = i + 1;
            while (j < s.Length)
            {
                if (s[j] == '\'')
                {
                    if (j + 1 < s.Length && s[j + 1] == '\'') { sb.Append('\''); j += 2; continue; }
                    j++;
                    break;
                }
                sb.Append(s[j]);
                j++;
            }
            if (j >= s.Length || s[j] != '!')
                return false;

            var content = sb.ToString();
            var colon = content.IndexOf(':');
            if (colon < 0)
                return false; // A plain quoted sheet name (no span) — let TryReadSheetPrefix handle it.

            startSheet = content[..colon];
            endSheet = content[(colon + 1)..];
            index = j + 1;
            return true;
        }

        // Unquoted start sheet name, up to the span ':'.
        var startNameBegin = i;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '.'))
            i++;
        if (i == startNameBegin || i >= s.Length || s[i] != ':')
            return false;

        // Unquoted end sheet name, up to the span '!'.
        var endNameBegin = i + 1;
        var k = endNameBegin;
        while (k < s.Length && (char.IsLetterOrDigit(s[k]) || s[k] == '_' || s[k] == '.'))
            k++;
        if (k == endNameBegin || k >= s.Length || s[k] != '!')
            return false;

        startSheet = s.Substring(startNameBegin, i - startNameBegin);
        endSheet = s.Substring(endNameBegin, k - endNameBegin);
        index = k + 1;
        return true;
    }

    private static bool TryReadSheetPrefix(string s, ref int index, out string sheetName)
    {
        sheetName = "";
        var i = index;
        if (i >= s.Length) return false;

        if (s[i] == '\'')
        {
            // Quoted sheet name with '' escapes, terminated by '!'.
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                if (s[i] == '\'')
                {
                    if (i + 1 < s.Length && s[i + 1] == '\'') { sb.Append('\''); i += 2; continue; }
                    i++;
                    break;
                }
                sb.Append(s[i]);
                i++;
            }
            if (i < s.Length && s[i] == '!')
            {
                sheetName = sb.ToString();
                index = i + 1;
                return true;
            }
            return false;
        }

        // Unquoted sheet name: letters/digits/._ up to a '!'.
        var startName = i;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '.'))
            i++;
        if (i > startName && i < s.Length && s[i] == '!')
        {
            sheetName = s.Substring(startName, i - startName);
            index = i + 1;
            return true;
        }
        return false;
    }

    private readonly record struct CellRef(bool ColAbs, string Col, bool RowAbs, string Row);

    private static bool TryReadCellRef(string s, ref int index, out CellRef cellRef)
    {
        cellRef = default;
        var i = index;

        var colAbs = false;
        if (i < s.Length && s[i] == '$') { colAbs = true; i++; }

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

        var rowAbs = false;
        if (i < s.Length && s[i] == '$') { rowAbs = true; i++; }

        var rowStart = i;
        while (i < s.Length && char.IsDigit(s[i]))
            i++;
        if (i == rowStart)
            return false;
        if (!uint.TryParse(s.AsSpan(rowStart, i - rowStart), out var rowNum) || rowNum == 0 || rowNum > CellAddress.MaxRow)
            return false;

        // Must end at a token boundary; a trailing identifier char or '(' means this was part of a name.
        if (i < s.Length && (IsIdentifierChar(s[i]) || s[i] == '('))
            return false;

        cellRef = new CellRef(colAbs, s.Substring(colStart, colDigits), rowAbs, s.Substring(rowStart, i - rowStart));
        index = i;
        return true;
    }

    private static string FormatOdfRef(string? sheet, CellRef r) =>
        "[" + SheetToken(sheet) + "." + Coord(r) + "]";

    private static string FormatOdfRange(string? sheet1, CellRef r1, string? sheet2, CellRef r2) =>
        "[" + SheetToken(sheet1) + "." + Coord(r1) + ":" + SheetToken(sheet2, forSecondInRange: true) + "." + Coord(r2) + "]";

    private static string SheetToken(string? sheet, bool forSecondInRange = false)
    {
        if (sheet is null)
            return forSecondInRange ? "" : "";
        var prefix = "$"; // we always emit absolute-sheet form, matching what LibreOffice writes
        return prefix + QuoteSheetIfNeeded(sheet);
    }

    private static string Coord(CellRef r) =>
        (r.ColAbs ? "$" : "") + r.Col + (r.RowAbs ? "$" : "") + r.Row;

    private static string QuoteSheetIfNeeded(string sheet)
    {
        var needsQuote = false;
        foreach (var ch in sheet)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '.') { needsQuote = true; break; }
        }
        if (sheet.Length > 0 && char.IsDigit(sheet[0]))
            needsQuote = true;
        return needsQuote ? "'" + sheet.Replace("'", "''", StringComparison.Ordinal) + "'" : sheet;
    }

    // ---- ODF bracket -> A1 -----------------------------------------------------------------------

    private static string ConvertBracketRefToA1(string inner)
    {
        // inner looks like ".A1", "$Sheet.A1", ".A1:.B2", "$Sheet.A1:.B2", "'My Sheet'.A1".
        // Detect a range by the ':' that separates two ".coord" parts.
        var colon = FindRangeColon(inner);
        if (colon >= 0)
        {
            var left = ConvertSingleBracketRef(inner.Substring(0, colon));
            var right = ConvertSingleBracketRef(inner.Substring(colon + 1));
            // If both endpoints share the same sheet prefix, drop the redundant sheet on the right.
            var rightEndpoint = ShouldStripRightEndpointSheet(left, right) ? StripSheet(right) : right;
            return left + ":" + rightEndpoint;
        }
        return ConvertSingleBracketRef(inner);
    }

    private static int FindRangeColon(string inner)
    {
        // A ':' at top level (not inside a quoted sheet name) separating two reference parts.
        var inQuote = false;
        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            if (c == '\'')
            {
                if (inQuote && i + 1 < inner.Length && inner[i + 1] == '\'') { i++; continue; }
                inQuote = !inQuote;
            }
            else if (c == ':' && !inQuote)
            {
                return i;
            }
        }
        return -1;
    }

    private static string ConvertSingleBracketRef(string part)
    {
        // part: optional "$sheet" or "$'sheet'" then "." then coord. Sometimes a leading "." only.
        string? sheet = null;
        var dot = FindSheetDot(part, out sheet);
        var coord = dot >= 0 ? part.Substring(dot + 1) : part;
        coord = coord.Trim();
        if (sheet is null)
            return coord;
        return QuoteSheetIfNeeded(sheet) + "!" + coord;
    }

    private static int FindSheetDot(string part, out string? sheet)
    {
        sheet = null;
        if (part.Length == 0)
            return -1;

        var i = 0;
        // Optional leading '$' (absolute sheet marker) — applies to the sheet, not retained in A1.
        if (part[i] == '$') i++;

        if (i < part.Length && part[i] == '.')
        {
            // ".A1" — current sheet, no name.
            return i;
        }

        if (i < part.Length && part[i] == '\'')
        {
            // Quoted sheet name.
            var sb = new StringBuilder();
            i++;
            while (i < part.Length)
            {
                if (part[i] == '\'')
                {
                    if (i + 1 < part.Length && part[i + 1] == '\'') { sb.Append('\''); i += 2; continue; }
                    i++;
                    break;
                }
                sb.Append(part[i]);
                i++;
            }
            if (i < part.Length && part[i] == '.')
            {
                sheet = sb.ToString();
                return i;
            }
            return -1;
        }

        // Unquoted sheet name up to the '.'.
        var nameStart = i;
        while (i < part.Length && part[i] != '.')
            i++;
        if (i < part.Length && part[i] == '.' && i > nameStart)
        {
            sheet = part.Substring(nameStart, i - nameStart);
            return i;
        }
        // No dot — treat the whole thing as a bare coord (defensive).
        return -1;
    }

    private static string StripSheet(string a1)
    {
        var bang = a1.IndexOf('!');
        return bang >= 0 ? a1[(bang + 1)..] : a1;
    }

    private static bool ShouldStripRightEndpointSheet(string left, string right)
    {
        var leftSheet = TryGetA1SheetName(left);
        var rightSheet = TryGetA1SheetName(right);

        return rightSheet is null ||
               (leftSheet is not null && string.Equals(leftSheet, rightSheet, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryGetA1SheetName(string a1)
    {
        var bang = FindA1SheetBang(a1);
        if (bang < 0)
            return null;

        var sheet = a1.Substring(0, bang);
        if (sheet.Length >= 2 && sheet[0] == '\'' && sheet[^1] == '\'')
            return sheet[1..^1].Replace("''", "'", StringComparison.Ordinal);

        return sheet;
    }

    private static int FindA1SheetBang(string a1)
    {
        var inQuote = false;
        for (var i = 0; i < a1.Length; i++)
        {
            var c = a1[i];
            if (c == '\'')
            {
                if (inQuote && i + 1 < a1.Length && a1[i + 1] == '\'') { i++; continue; }
                inQuote = !inQuote;
            }
            else if (c == '!' && !inQuote)
            {
                return i;
            }
        }

        return -1;
    }

    // ---- scanning helpers ------------------------------------------------------------------------

    private static int SkipStringLiteral(string s, int openQuote)
    {
        var i = openQuote + 1;
        while (i < s.Length)
        {
            if (s[i] == '"')
            {
                if (i + 1 < s.Length && s[i + 1] == '"') { i += 2; continue; }
                return i + 1;
            }
            i++;
        }
        return i;
    }

    private static bool IsColumnLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsIdentifierChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '\\' || c == '$';
}
