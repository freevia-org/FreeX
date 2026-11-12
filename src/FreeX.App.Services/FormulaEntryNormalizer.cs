using System.Text;

namespace FreeX.App.Services;

/// <summary>
/// The small corrections Excel applies to a formula as it is committed, before the formula is parsed:
/// function names and A1 cell references are upper-cased (<c>=sum(a1:b2)</c> becomes
/// <c>=SUM(A1:B2)</c>) and closing parentheses that are only missing at the end are added
/// (<c>=SUM(A1:A3</c> becomes <c>=SUM(A1:A3)</c>). Text in string literals, quoted sheet names and
/// structured-reference brackets is left exactly as typed, as is spacing.
/// </summary>
public static class FormulaEntryNormalizer
{
    /// <summary>Upper-cases function names and A1 references outside literals.</summary>
    public static string NormalizeCasing(string formula)
    {
        ArgumentNullException.ThrowIfNull(formula);

        var result = new StringBuilder(formula.Length);
        var index = 0;
        while (index < formula.Length)
        {
            var c = formula[index];
            if (c == '"')
            {
                index = CopyQuoted(formula, index, '"', result);
                continue;
            }

            if (c == '\'')
            {
                index = CopyQuoted(formula, index, '\'', result);
                continue;
            }

            if (c == '[')
            {
                index = CopyBracketed(formula, index, result);
                continue;
            }

            if (IsIdentifierStart(c) && (index == 0 || !IsIdentifierPart(formula[index - 1])))
            {
                var end = index;
                while (end < formula.Length && IsIdentifierPart(formula[end]))
                    end++;

                var token = formula[index..end];
                var next = end < formula.Length ? formula[end] : '\0';
                result.Append(ShouldUpperCase(token, next) ? token.ToUpperInvariant() : token);
                index = end;
                continue;
            }

            result.Append(c);
            index++;
        }

        return result.ToString();
    }

    /// <summary>
    /// Returns <paramref name="formula"/> with any closing parentheses that are missing at its end
    /// appended, or the formula unchanged when its parentheses already balance (or close too often,
    /// which is a real error left for the parser to report).
    /// </summary>
    public static string CloseMissingParentheses(string formula)
    {
        ArgumentNullException.ThrowIfNull(formula);

        var open = 0;
        var index = 0;
        while (index < formula.Length)
        {
            var c = formula[index];
            if (c is '"' or '\'')
            {
                index = SkipQuoted(formula, index, c);
                continue;
            }

            if (c == '[')
            {
                index = SkipBracketed(formula, index);
                continue;
            }

            if (c == '(')
                open++;
            else if (c == ')')
                open--;

            if (open < 0)
                return formula;

            index++;
        }

        return open > 0 ? formula + new string(')', open) : formula;
    }

    private static bool ShouldUpperCase(string token, char next)
    {
        // A function call: name immediately followed by "(" (also dotted names such as STDEV.P).
        if (next == '(')
            return true;

        // A sheet name before "!" keeps the user's spelling, as in Excel.
        if (next == '!')
            return false;

        return IsA1CellReference(token) || IsBooleanLiteral(token);
    }

    private static bool IsBooleanLiteral(string token) =>
        token.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("FALSE", StringComparison.OrdinalIgnoreCase);

    // "$a$1", "ab12", "XFD1048576": 1-3 column letters then a row number, each optionally anchored.
    private static bool IsA1CellReference(string token)
    {
        var index = 0;
        if (index < token.Length && token[index] == '$')
            index++;

        var letterStart = index;
        while (index < token.Length && char.IsAsciiLetter(token[index]))
            index++;

        var letters = index - letterStart;
        if (letters is < 1 or > 3)
            return false;

        if (string.CompareOrdinal(token[letterStart..index].ToUpperInvariant().PadLeft(3, 'A'), "XFD") > 0 && letters == 3)
            return false;

        if (index < token.Length && token[index] == '$')
            index++;

        var digitStart = index;
        while (index < token.Length && char.IsAsciiDigit(token[index]))
            index++;

        return index == token.Length && index > digitStart && token[digitStart] != '0';
    }

    private static bool IsIdentifierStart(char c) => char.IsAsciiLetter(c) || c is '_' or '\\' or '$';

    private static bool IsIdentifierPart(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '$' or '\\';

    private static int CopyQuoted(string text, int start, char quote, StringBuilder result)
    {
        var end = SkipQuoted(text, start, quote);
        result.Append(text, start, end - start);
        return end;
    }

    // Skips a quoted run, honouring the doubled-quote escape ("" or '').
    private static int SkipQuoted(string text, int start, char quote)
    {
        var index = start + 1;
        while (index < text.Length)
        {
            if (text[index] == quote)
            {
                if (index + 1 < text.Length && text[index + 1] == quote)
                {
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            index++;
        }

        return text.Length;
    }

    private static int CopyBracketed(string text, int start, StringBuilder result)
    {
        var end = SkipBracketed(text, start);
        result.Append(text, start, end - start);
        return end;
    }

    // Structured references nest brackets: Table1[[#Headers],[Col]].
    private static int SkipBracketed(string text, int start)
    {
        var depth = 0;
        var index = start;
        while (index < text.Length)
        {
            if (text[index] == '[')
                depth++;
            else if (text[index] == ']')
            {
                depth--;
                if (depth == 0)
                    return index + 1;
            }

            index++;
        }

        return text.Length;
    }
}
