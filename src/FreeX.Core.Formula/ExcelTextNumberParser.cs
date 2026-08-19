using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FreeX.Core.Formula;

internal static class ExcelTextNumberParser
{
    private static readonly Regex FakeLeapDayTextRegex = new(
        @"^(?:2/29/1900|02/29/1900|1900-02-29)(?:\s+(.+))?$",
        RegexOptions.IgnoreCase);
    private static readonly Regex MonthNameRegex = new(
        @"\b(?:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)",
        RegexOptions.IgnoreCase);
    private static readonly Regex AmPmRegex = new(
        @"\b(?:am|pm)\b",
        RegexOptions.IgnoreCase);

    // Matches a number whose thousands grouping is correct: each group to the left of the decimal
    // (or end) is exactly 3 digits, except the first group which may be 1–3 digits.  Optional leading
    // sign, optional leading/trailing currency symbol (with an optional single whitespace character
    // between the symbol and the digits, since real-world text commonly writes e.g. "1.234,56 €"
    // with a space before the symbol), optional decimal fraction, and an optional matched pair of
    // parentheses (Excel's accounting-format negative wrapper) around the whole thing — a leading "("
    // requires a trailing ")" and vice versa, via the "paren" conditional group.
    // The grouping/decimal separators and currency symbol are the culture's
    // (culture.NumberFormat.NumberGroupSeparator / NumberDecimalSeparator / CurrencySymbol) rather
    // than hardcoded ',' / '.' / '$', because a culture such as de-DE groups with '.' and uses ',' as
    // the decimal point, and a culture such as fr-FR or de-DE uses a currency symbol other than '$'
    // — see GetValidGroupingRegex. Compiled regexes are cached per (group separator, decimal
    // separator, currency symbol) triple since CultureInfo.CurrentCulture is typically stable across
    // many calls on the same thread.
    // Examples that pass (en-US: group=',' decimal='.'): 1,234  $1,234.50  -1,234,567  1,234,567.5
    //   (1,234.50)  ($1,234.50)
    // Examples that fail: 1,2  12,34  1,2345  1,234,5  (1,234.50  1,234.50)
    private static readonly ConcurrentDictionary<(string GroupSeparator, string DecimalSeparator, string CurrencySymbol), Regex> ValidGroupingRegexCache = new();

    private static Regex GetValidGroupingRegex(CultureInfo culture)
    {
        string groupSeparator = culture.NumberFormat.NumberGroupSeparator;
        string decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;
        string currencySymbol = culture.NumberFormat.CurrencySymbol;
        return ValidGroupingRegexCache.GetOrAdd((groupSeparator, decimalSeparator, currencySymbol), static key =>
        {
            string currency = key.CurrencySymbol.Length > 0 ? Regex.Escape(key.CurrencySymbol) : string.Empty;
            return new Regex(
                $@"^(?<paren>\()?[+-]?(?:{currency}\s?)?\d{{1,3}}(?:{Regex.Escape(key.GroupSeparator)}\d{{3}})*(?:{Regex.Escape(key.DecimalSeparator)}\d*)?(?:\s?{currency})?[+-]?(?(paren)\)|)$",
                RegexOptions.None);
        });
    }

    /// <summary>
    /// When the culture's group separator is itself a single whitespace character (e.g. U+202F
    /// narrow no-break space for fr-FR, or U+00A0 non-breaking space for fi-FI), real-world text
    /// commonly uses a different whitespace code point for the same visual grouping — an ordinary
    /// space U+0020 (what a keyboard actually produces) or a plain non-breaking space U+00A0 (common
    /// from other apps/websites), rather than the exact code point CultureInfo's current ICU data
    /// happens to report. .NET's own <see cref="double.TryParse(string, NumberStyles, IFormatProvider, out double)"/>
    /// does not treat these as interchangeable with the culture's exact separator (verified: it parses
    /// the culture's own U+202F and an ordinary U+0020 for fr-FR, but rejects U+00A0), so normalize
    /// every whitespace character in the text to the culture's canonical separator up front, before
    /// grouping detection, regex validation, and the final numeric parse all run against it.
    /// No-op (returns <paramref name="text"/> unchanged) when the group separator isn't a single
    /// whitespace character, so non-space-separator cultures (en-US ',', de-DE '.') are untouched.
    /// </summary>
    /// <remarks>
    /// r151-remediation: exposed as <c>internal</c> so the clipboard paste path
    /// (FreeX.Core.Commands, PasteCommandFactory.TryParseCultureGroupedNumber) can share it
    /// instead of growing a third hand-written copy of the same rule -- the exact class of defect
    /// round 151 was sweeping for. FreeX.Core.Commands already has InternalsVisibleTo here.
    /// </remarks>
    internal static string NormalizeGroupSeparatorSpaceVariants(string text, string groupSeparator)
    {
        if (groupSeparator.Length != 1 || !char.IsWhiteSpace(groupSeparator[0]))
            return text;

        char canonical = groupSeparator[0];
        char[]? buffer = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != canonical && char.IsWhiteSpace(c))
            {
                buffer ??= text.ToCharArray();
                buffer[i] = canonical;
            }
        }

        return buffer is null ? text : new string(buffer);
    }

    // NumberStyles without AllowThousands — used for the first parse attempt so that
    // comma-separated inputs with bad grouping do not silently succeed.
    private const NumberStyles StylesWithoutThousands =
        NumberStyles.AllowLeadingSign  |
        NumberStyles.AllowTrailingSign |
        NumberStyles.AllowParentheses  |
        NumberStyles.AllowDecimalPoint |
        NumberStyles.AllowExponent     |
        NumberStyles.AllowCurrencySymbol;

    /// <summary>
    /// Clones <see cref="CultureInfo.CurrentCulture"/> with Excel's fixed two-digit-year pivot
    /// (00-29 -> 2000-2029, 30-99 -> 1930-1999) instead of .NET's default
    /// <see cref="Calendar.TwoDigitYearMax"/>, which trails ~50 years ahead of the current date
    /// (e.g. 2049 in 2026) and drifts over time. Mirrors <c>BuiltInFunctions.DateTime.cs</c>'s
    /// <c>CreateExcelTwoDigitYearCulture</c> (same pivot, same clone-and-override pattern, and
    /// likewise using <see cref="CultureInfo.CurrentCulture"/> rather than a fixed en-US culture)
    /// so VALUE()/implicit-arithmetic text coercion agrees with DATEVALUE and NUMBERVALUE under
    /// the same system locale. Not cached statically - built fresh on every call - because
    /// <see cref="CultureInfo.CurrentCulture"/> can change at runtime.
    /// </summary>
    private static CultureInfo CreateCurrentCultureWithExcelTwoDigitYearCutoff()
    {
        var culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
        culture.DateTimeFormat.Calendar.TwoDigitYearMax = 2029;
        return culture;
    }

    public static bool TryParse(string text, out double number) => TryParse(text, out number, uses1904DateSystem: false);

    /// <param name="uses1904DateSystem">
    /// Whether the owning workbook uses the 1904 date system. A successfully-parsed date is
    /// converted to a serial number via <see cref="ExcelDateSystem.DateToSerial(DateTime, bool)"/>
    /// with this flag so the resulting serial is expressed in the same epoch the workbook's
    /// number formatter will later use to render it — mismatching the two epochs silently
    /// shifts the displayed date by the ~4-year (1462-day) gap between the 1900 and 1904 systems.
    /// </param>
    public static bool TryParse(string text, out double number, bool uses1904DateSystem)
    {
        // Built once per call (not cached statically) so a change to CultureInfo.CurrentCulture
        // between calls is always honored - see CreateCurrentCultureWithExcelTwoDigitYearCutoff.
        var culture = CreateCurrentCultureWithExcelTwoDigitYearCutoff();
        var trimmed = text.Trim();

        int pctCount = 0;
        while (trimmed.EndsWith('%'))
        {
            pctCount++;
            trimmed = trimmed[..^1].TrimEnd();
        }

        if (pctCount > 0 &&
            TryParseNumericStrict(trimmed, culture, out var pct, out _))
        {
            for (int i = 0; i < pctCount; i++)
                pct /= 100.0;

            number = pct;
            return true;
        }

        if (TryParseNumericStrict(trimmed, culture, out number, out bool rejectedNumericComma))
            return true;

        // If TryParseNumericStrict identified the input as a malformed numeric (contains commas
        // but grouping is invalid), do not fall through to DateTime — "1,2" should not become
        // January 2nd.
        if (rejectedNumericComma)
        {
            number = 0;
            return false;
        }

        if (TryParseExcelFakeLeapDayText(trimmed, culture, out number))
            return true;

        // Gate: only attempt DateTime.TryParse when the text contains at least one ASCII digit.
        // This blocks bare month names ("March", "Monday") which .NET's DateTime parser accepts
        // as current-year dates, producing a result that changes year-to-year — Excel yields #VALUE!.
        if (ContainsAsciiDigit(trimmed) &&
            DateTime.TryParse(trimmed, culture, DateTimeStyles.None, out var dt))
        {
            number = IsTimeOnlyText(trimmed)
                ? dt.TimeOfDay.TotalDays
                : ExcelDateSystem.DateToSerial(dt, uses1904DateSystem);
            return true;
        }

        number = 0;
        return false;
    }

    /// <summary>
    /// Parses a numeric string with strict thousands-grouping validation.
    /// Accepts the same styles as the old <c>NumberStyles.Any</c> except that when the input
    /// contains the culture's group separator (e.g. ',' for en-US, '.' for de-DE), the grouping
    /// placement must be exactly correct: groups of 3 digits, with the leading group containing
    /// 1–3 digits and no group separator after the decimal point.
    /// <para>
    /// <paramref name="rejectedNumericComma"/> is set to <c>true</c> when the text contained
    /// the group separator, looked numeric in structure, but failed grouping validation — the
    /// caller should not then fall through to a date/time parse because "1,2" must not become
    /// January 2nd (and, under a culture whose group separator isn't ',', the equivalent malformed
    /// grouped text in that separator).
    /// </para>
    /// </summary>
    private static bool TryParseNumericStrict(string text, CultureInfo culture, out double number, out bool rejectedNumericComma)
    {
        rejectedNumericComma = false;

        string groupSeparator = culture.NumberFormat.NumberGroupSeparator;
        text = NormalizeGroupSeparatorSpaceVariants(text, groupSeparator);
        bool hasGroupSeparator = groupSeparator.Length > 0 && text.Contains(groupSeparator, StringComparison.Ordinal);

        // Fast path: no group separator → no grouping issue, parse without AllowThousands.
        if (!hasGroupSeparator)
            return double.TryParse(text, StylesWithoutThousands, culture, out number);

        // Has the group separator: first try without AllowThousands.  StylesWithoutThousands
        // rejects a character that isn't the culture's decimal separator, so this succeeds
        // directly for cultures (e.g. de-DE) whose decimal separator is the same character as
        // some other culture's group separator, and typically fails when that character really
        // is being used for grouping (e.g. en-US's ',').
        if (double.TryParse(text, StylesWithoutThousands, culture, out number))
            return true;

        // Group separator present and didn't parse without AllowThousands.
        // Validate grouping shape before allowing thousands parsing.
        if (!GetValidGroupingRegex(culture).IsMatch(text))
        {
            // Only mark as a rejected numeric attempt when the text starts with digit/sign/currency
            // so that date strings like "March 14, 2026" can still reach the DateTime path.
            rejectedNumericComma = LooksNumeric(text, culture);
            number = 0;
            return false;
        }

        return double.TryParse(text, NumberStyles.Any, culture, out number);
    }

    private static bool TryParseExcelFakeLeapDayText(string text, CultureInfo culture, out double serial)
    {
        serial = 0;
        var match = FakeLeapDayTextRegex.Match(text);
        if (!match.Success) return false;

        serial = 60;
        if (match.Groups[1].Success)
        {
            if (!DateTime.TryParse(match.Groups[1].Value, culture, DateTimeStyles.None, out var time))
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
    /// that failed grouping validation should block the DateTime fallback. The currency check
    /// uses the culture's own <see cref="NumberFormatInfo.CurrencySymbol"/> (e.g. '€' for
    /// fr-FR/de-DE) rather than a hardcoded '$', so a leading-symbol amount is recognized as
    /// numeric-looking under any culture, not just those whose symbol happens to be '$'.
    /// "1,2" starts with '1' → true (block DateTime).
    /// "March 14, 2026" starts with 'M' → false (allow DateTime).
    /// </summary>
    private static bool LooksNumeric(string text, CultureInfo culture)
    {
        if (text.Length == 0) return false;
        char first = text[0];
        if (first is (>= '0' and <= '9') or '+' or '-' or '(')
            return true;

        string currencySymbol = culture.NumberFormat.CurrencySymbol;
        return currencySymbol.Length > 0 && text.StartsWith(currencySymbol, StringComparison.Ordinal);
    }
}
