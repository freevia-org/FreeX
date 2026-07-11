using System.Globalization;
using System.Text.RegularExpressions;

namespace FreeX.Core.Formula;

internal static class ExcelTextNumberParser
{
    private static readonly CultureInfo UsCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly Regex FakeLeapDayTextRegex = new(
        @"^(?:2/29/1900|02/29/1900|1900-02-29)(?:\s+(.+))?$",
        RegexOptions.IgnoreCase);
    private static readonly Regex MonthNameRegex = new(
        @"\b(?:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)",
        RegexOptions.IgnoreCase);
    private static readonly Regex AmPmRegex = new(
        @"\b(?:am|pm)\b",
        RegexOptions.IgnoreCase);

    // Matches a number whose comma grouping is correct: each group to the left of the decimal (or end)
    // is exactly 3 digits, except the first group which may be 1–3 digits.  Optional leading sign,
    // optional leading/trailing currency symbol, optional decimal fraction, and an optional matched
    // pair of parentheses (Excel's accounting-format negative wrapper) around the whole thing — a
    // leading "(" requires a trailing ")" and vice versa, via the "paren" conditional group.
    // Examples that pass: 1,234  $1,234.50  -1,234,567  1,234,567.5  (1,234.50)  ($1,234.50)
    // Examples that fail: 1,2  12,34  1,2345  1,234,5  (1,234.50  1,234.50)
    private static readonly Regex ValidGroupingRegex = new(
        @"^(?<paren>\()?[+-]?\$?\d{1,3}(?:,\d{3})*(?:\.\d*)?\$?[+-]?(?(paren)\)|)$",
        RegexOptions.None);

    // NumberStyles without AllowThousands — used for the first parse attempt so that
    // comma-separated inputs with bad grouping do not silently succeed.
    private const NumberStyles StylesWithoutThousands =
        NumberStyles.AllowLeadingSign  |
        NumberStyles.AllowTrailingSign |
        NumberStyles.AllowParentheses  |
        NumberStyles.AllowDecimalPoint |
        NumberStyles.AllowExponent     |
        NumberStyles.AllowCurrencySymbol;

    public static bool TryParse(string text, out double number)
    {
        var trimmed = text.Trim();

        int pctCount = 0;
        while (trimmed.EndsWith('%'))
        {
            pctCount++;
            trimmed = trimmed[..^1].TrimEnd();
        }

        if (pctCount > 0 &&
            TryParseNumericStrict(trimmed, out var pct, out _))
        {
            for (int i = 0; i < pctCount; i++)
                pct /= 100.0;

            number = pct;
            return true;
        }

        if (TryParseNumericStrict(trimmed, out number, out bool rejectedNumericComma))
            return true;

        // If TryParseNumericStrict identified the input as a malformed numeric (contains commas
        // but grouping is invalid), do not fall through to DateTime — "1,2" should not become
        // January 2nd.
        if (rejectedNumericComma)
        {
            number = 0;
            return false;
        }

        if (TryParseExcelFakeLeapDayText(trimmed, out number))
            return true;

        // Gate: only attempt DateTime.TryParse when the text contains at least one ASCII digit.
        // This blocks bare month names ("March", "Monday") which .NET's DateTime parser accepts
        // as current-year dates, producing a result that changes year-to-year — Excel yields #VALUE!.
        if (ContainsAsciiDigit(trimmed) &&
            DateTime.TryParse(trimmed, UsCulture, DateTimeStyles.None, out var dt))
        {
            number = IsTimeOnlyText(trimmed)
                ? dt.TimeOfDay.TotalDays
                : ExcelDateSystem.DateToSerial(dt);
            return true;
        }

        number = 0;
        return false;
    }

    /// <summary>
    /// Parses a numeric string with strict thousands-grouping validation.
    /// Accepts the same styles as the old <c>NumberStyles.Any</c> except that when the input
    /// contains commas (group separators), the grouping placement must be exactly correct:
    /// groups of 3 digits, with the leading group containing 1–3 digits and no comma after
    /// the decimal point.
    /// <para>
    /// <paramref name="rejectedNumericComma"/> is set to <c>true</c> when the text contained
    /// commas, looked numeric in structure, but failed grouping validation — the caller should
    /// not then fall through to a date/time parse because "1,2" must not become January 2nd.
    /// </para>
    /// </summary>
    private static bool TryParseNumericStrict(string text, out double number, out bool rejectedNumericComma)
    {
        rejectedNumericComma = false;

        // Fast path: no comma → no grouping issue, parse without AllowThousands.
        if (!text.Contains(','))
            return double.TryParse(text, StylesWithoutThousands, UsCulture, out number);

        // Has commas: first try without AllowThousands.  StylesWithoutThousands rejects commas
        // in UsCulture so this will typically fail for comma-containing strings.
        if (double.TryParse(text, StylesWithoutThousands, UsCulture, out number))
            return true;

        // Commas present and didn't parse without AllowThousands.
        // Validate grouping shape before allowing thousands parsing.
        if (!ValidGroupingRegex.IsMatch(text))
        {
            // Only mark as a rejected numeric attempt when the text starts with digit/sign/currency
            // so that date strings like "March 14, 2026" can still reach the DateTime path.
            rejectedNumericComma = LooksNumeric(text);
            number = 0;
            return false;
        }

        return double.TryParse(text, NumberStyles.Any, UsCulture, out number);
    }

    private static bool TryParseExcelFakeLeapDayText(string text, out double serial)
    {
        serial = 0;
        var match = FakeLeapDayTextRegex.Match(text);
        if (!match.Success) return false;

        serial = 60;
        if (match.Groups[1].Success)
        {
            if (!DateTime.TryParse(match.Groups[1].Value, UsCulture, DateTimeStyles.None, out var time))
                return false;
            serial += time.TimeOfDay.TotalDays;
        }

        return true;
    }

    private static bool IsTimeOnlyText(string text)
    {
        if (text.Contains('/') || text.Contains('-')) return false;
        if (MonthNameRegex.IsMatch(text))
            return false;

        return text.Contains(':')
            || AmPmRegex.IsMatch(text);
    }

    private static bool ContainsAsciiDigit(string text)
    {
        foreach (char c in text)
        {
            if (c >= '0' && c <= '9')
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when the text starts with characters typical of a numeric value
    /// (digit, sign, or currency symbol) — used to decide whether a comma-containing string
    /// that failed grouping validation should block the DateTime fallback.
    /// "1,2" starts with '1' → true (block DateTime).
    /// "March 14, 2026" starts with 'M' → false (allow DateTime).
    /// </summary>
    private static bool LooksNumeric(string text)
    {
        if (text.Length == 0) return false;
        char first = text[0];
        return first is (>= '0' and <= '9') or '+' or '-' or '$' or '(';
    }
}
