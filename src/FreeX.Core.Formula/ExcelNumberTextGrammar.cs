using System.Globalization;
using System.Text;

namespace FreeX.Core.Formula;

/// <summary>
/// Excel's en-US text-to-number grammar, measured against real Excel (VALUE / NUMBERVALUE) rather
/// than inferred from <see cref="double.TryParse(string, NumberStyles, IFormatProvider, out double)"/>.
/// The two differ in many small ways that matter for parity: .NET lets a currency symbol trail, lets
/// a sign trail, accepts any whitespace, and has no concept of Excel's rule that every thousands group
/// after the first holds at least three digits ("1,2345" is 12345 but "1,23" is #VALUE!), of mixed
/// fractions ("1 1/2" is 1.5), or of the numeric range Excel's parser refuses (|x| of 1E308 or more).
/// <para>Strict mode is VALUE's: one optional sign, one optional "$", at most one "%" (never together
/// with "$"), one optional "(...)" wrapper that negates, thousands groups validated. Lenient mode is
/// what NUMBERVALUE feeds after it has stripped whitespace and group separators: any number of
/// percent signs, "$" allowed beside them, no grouping or mixed-fraction rules.</para>
/// </summary>
internal static class ExcelNumberTextGrammar
{
    // Excel refuses typed/parsed numbers outside this magnitude band: 1E308 and above is #VALUE!,
    // and anything below the smallest normal double is flushed (0 for DBL_MIN itself, #VALUE! under).
    private const double MaxMagnitude = 9.99999999999999E+307;
    private const double MinNormal = 2.2250738585072014E-308;

    /// <summary>
    /// Maps non-ASCII decimal digits (Arabic-Indic, Devanagari, ...) to ASCII. Excel accepts them in
    /// number and time text but rejects full-width digits, so U+FF10..U+FF19 are left alone (and then
    /// fail the grammar), exactly as Excel does.
    /// </summary>
    internal static string NormalizeDigits(string text)
    {
        StringBuilder? sb = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c > 127 && c is not (>= '０' and <= '９') && char.IsDigit(c))
            {
                sb ??= new StringBuilder(text, 0, i, text.Length);
                sb.Append((char)('0' + CharUnicodeInfo.GetDecimalDigitValue(c)));
            }
            else
            {
                sb?.Append(c);
            }
        }

        return sb is null ? text : sb.ToString();
    }

    public static bool TryParseStrict(string text, out double value) => Parse(text, lenient: false, out value);

    public static bool TryParseLenient(string text, out double value) => Parse(text, lenient: true, out value);

    private static bool Parse(string s, bool lenient, out double value)
    {
        value = 0;
        int n = s.Length;
        int i = 0;
        bool negative = false, sign = false, dollar = false, paren = false;
        int percent = 0;

        while (true)
        {
            while (i < n && s[i] == ' ') i++;
            if (i >= n) return false;
            char c = s[i];
            if (c == '(' && !paren && !sign) { paren = true; i++; continue; }
            if (c == '$' && !dollar) { dollar = true; i++; continue; }
            if ((c == '+' || c == '-') && !sign && !paren) { sign = true; negative = c == '-'; i++; continue; }
            if (c == '%' && (lenient || percent == 0)) { percent++; i++; continue; }
            break;
        }

        if (!TryParseCore(s, ref i, lenient, out double magnitude)) return false;

        bool closed = false;
        while (true)
        {
            while (i < n && s[i] == ' ') i++;
            if (i >= n) break;
            char c = s[i];
            if (c == '%' && (lenient || percent == 0)) { percent++; i++; continue; }
            if (c == ')' && paren && !closed) { closed = true; i++; continue; }
            return false;
        }

        if (paren && !closed) return false;
        if (!lenient && dollar && percent > 0) return false;

        double result = magnitude;
        for (int p = 0; p < percent; p++) result /= 100.0;
        if (negative ^ paren) result = -result;

        double abs = Math.Abs(result);
        // Beyond Excel's range the text is "a number too large": report it as +/-Infinity and let the
        // caller decide (VALUE and NUMBERVALUE answer #VALUE!; the operand coercion used by other
        // functions has always surfaced it as a non-finite number that each function rejects itself).
        if (!double.IsFinite(result) || abs > MaxMagnitude)
        {
            value = result < 0 ? double.NegativeInfinity : double.PositiveInfinity;
            return true;
        }

        if (abs != 0 && abs < MinNormal) return false;
        if (abs == MinNormal) result = 0;

        value = result == 0 ? 0 : result;
        return true;
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static bool TryParseCore(string s, ref int i, bool lenient, out double magnitude)
    {
        magnitude = 0;
        int n = s.Length;
        var intDigits = new StringBuilder();
        var fracDigits = new StringBuilder();

        if (i < n && IsDigit(s[i]))
        {
            int run = i;
            while (i < n && IsDigit(s[i])) i++;
            intDigits.Append(s, run, i - run);

            // Excel's grouping rule: the first group is non-zero-led and every group after a comma
            // holds three or more digits ("1,2345,678" is fine, "1,0" / "12,34,567" / "1,000," are not).
            if (!lenient && i < n && s[i] == ',')
            {
                if (s[run] == '0') return false;
                while (i < n && s[i] == ',')
                {
                    int j = i + 1;
                    int groupStart = j;
                    while (j < n && IsDigit(s[j])) j++;
                    if (j - groupStart < 3) return false;
                    intDigits.Append(s, groupStart, j - groupStart);
                    i = j;
                }
            }

            // Mixed fraction "W N/D": exactly one space, no spaces inside the fraction.
            if (!lenient && i < n && s[i] == ' ' && TryParseMixedFraction(s, i + 1, out int numerator, out int denominator, out int end))
            {
                if (denominator == 0) return false;
                if (!double.TryParse(intDigits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out double whole))
                    return false;
                magnitude = whole + (double)numerator / denominator;
                i = end;
                return true;
            }
        }

        if (i < n && s[i] == '.')
        {
            i++;
            int run = i;
            while (i < n && IsDigit(s[i])) i++;
            fracDigits.Append(s, run, i - run);
        }

        if (intDigits.Length == 0 && fracDigits.Length == 0) return false;

        long exponent = 0;
        if (i < n && (s[i] == 'e' || s[i] == 'E'))
        {
            int j = i + 1;
            bool expNegative = false;
            if (j < n && (s[j] == '+' || s[j] == '-')) { expNegative = s[j] == '-'; j++; }
            int digitsStart = j;
            while (j < n && IsDigit(s[j])) j++;
            if (j > digitsStart)
            {
                // A literal exponent beyond the double range is #VALUE! even for a zero mantissa.
                var expText = s.AsSpan(digitsStart, j - digitsStart).TrimStart('0');
                exponent = expText.Length > 3 ? 1000 : expText.Length == 0 ? 0 : long.Parse(expText, CultureInfo.InvariantCulture);
                if (exponent > 400)
                {
                    // Out of range even for a zero mantissa ("0e500" is #VALUE!); otherwise it simply
                    // overflows/underflows below.
                    if (!ContainsNonZeroDigit(intDigits) && !ContainsNonZeroDigit(fracDigits)) return false;
                    exponent = 400;
                }

                if (expNegative) exponent = -exponent;
                i = j;
            }
        }

        string text = (intDigits.Length == 0 ? "0" : intDigits.ToString())
            + (fracDigits.Length == 0 ? string.Empty : "." + fracDigits)
            + "E" + exponent.ToString(CultureInfo.InvariantCulture);
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out magnitude)) return false;

        // A non-zero mantissa that underflows to zero ("1e-324") is #VALUE!, not 0.
        if (magnitude == 0 && (ContainsNonZeroDigit(intDigits) || ContainsNonZeroDigit(fracDigits))) return false;
        return true;
    }

    private static bool ContainsNonZeroDigit(StringBuilder digits)
    {
        for (int i = 0; i < digits.Length; i++)
            if (digits[i] != '0') return true;
        return false;
    }

    private static bool TryParseMixedFraction(string s, int start, out int numerator, out int denominator, out int end)
    {
        numerator = 0;
        denominator = 0;
        end = start;
        int n = s.Length;
        int i = start;
        int numStart = i;
        while (i < n && IsDigit(s[i])) i++;
        if (i == numStart || i - numStart > 9 || i >= n || s[i] != '/') return false;
        numerator = int.Parse(s.AsSpan(numStart, i - numStart), NumberStyles.None, CultureInfo.InvariantCulture);
        i++;
        int denStart = i;
        while (i < n && IsDigit(s[i])) i++;
        if (i == denStart || i - denStart > 9) return false;
        denominator = int.Parse(s.AsSpan(denStart, i - denStart), NumberStyles.None, CultureInfo.InvariantCulture);
        if (i < n && s[i] is not (' ' or '%' or ')')) return false;
        end = i;
        return true;
    }
}
