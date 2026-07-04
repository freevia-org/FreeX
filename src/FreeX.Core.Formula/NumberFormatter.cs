using System.Globalization;
using System.Text.RegularExpressions;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class NumberFormatter
{
    private static readonly Regex NumericQuotedTextRegex = new("\"[^\"]*\"");

    // Returned alongside display text so the grid can apply conditional colors.
    public readonly record struct FormatResult(string Text, string? ColorHex = null);

    public static string Format(ScalarValue value, string formatString)
        => FormatWithColor(value, formatString).Text;

    public static string Format(ScalarValue value, string formatString, int targetWidthCharacters)
        => FormatWithColor(value, formatString, targetWidthCharacters).Text;

    public static string Format(ScalarValue value, string formatString, bool uses1904DateSystem)
        => FormatWithColor(value, formatString, (int?)null, null, null, uses1904DateSystem).Text;

    public static FormatResult FormatWithColor(ScalarValue value, string formatString)
        => FormatWithColor(value, formatString, (int?)null);

    public static FormatResult FormatWithColor(ScalarValue value, string formatString, int targetWidthCharacters)
        => FormatWithColor(value, formatString, (int?)targetWidthCharacters);

    public static FormatResult FormatWithColor(ScalarValue value, string formatString, bool uses1904DateSystem)
        => FormatWithColor(value, formatString, (int?)null, null, null, uses1904DateSystem);

    public static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        WorkbookIndexedColorPalette indexedColors)
        => FormatWithColor(value, formatString, (int?)null, indexedColors, null);

    public static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        int targetWidthCharacters,
        WorkbookIndexedColorPalette indexedColors)
        => FormatWithColor(value, formatString, (int?)targetWidthCharacters, indexedColors, null);

    public static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        WorkbookIndexedColorPalette indexedColors,
        WorkbookTheme theme)
        => FormatWithColor(value, formatString, (int?)null, indexedColors, theme);

    public static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        WorkbookIndexedColorPalette indexedColors,
        WorkbookTheme theme,
        bool uses1904DateSystem)
        => FormatWithColor(value, formatString, (int?)null, indexedColors, theme, uses1904DateSystem);

    public static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        int targetWidthCharacters,
        WorkbookIndexedColorPalette indexedColors,
        WorkbookTheme theme)
        => FormatWithColor(value, formatString, (int?)targetWidthCharacters, indexedColors, theme);

    public static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        int targetWidthCharacters,
        WorkbookIndexedColorPalette indexedColors,
        WorkbookTheme theme,
        bool uses1904DateSystem)
        => FormatWithColor(value, formatString, (int?)targetWidthCharacters, indexedColors, theme, uses1904DateSystem);

    private static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        int? targetWidthCharacters,
        WorkbookIndexedColorPalette? indexedColors = null,
        WorkbookTheme? theme = null,
        bool uses1904DateSystem = false)
    {
        if (string.IsNullOrEmpty(formatString) || IsGeneralFormat(formatString))
            return new FormatResult(FormatGeneral(value, uses1904DateSystem));

        // Pure text format
        if (formatString == "@")
        {
            return value switch
            {
                TextValue t   => new FormatResult(t.Value),
                NumberValue n => new FormatResult(FormatGeneral(value, uses1904DateSystem)),
                _             => new FormatResult(FormatGeneral(value, uses1904DateSystem))
            };
        }

        if (value is DateTimeValue dateTimeValue &&
            ShouldAttemptSimpleDateTimeFormat(formatString) &&
            TryFormatSimpleDateTime(dateTimeValue.Value, formatString, targetWidthCharacters, uses1904DateSystem, out var simpleDateTime))
        {
            return simpleDateTime;
        }

        var sections = SplitSections(formatString);

        return value switch
        {
            NumberValue n   => FormatNumber(n.Value, sections, targetWidthCharacters, indexedColors, theme, uses1904DateSystem),
            DateTimeValue d => ShouldFormatDateTimeValue(sections)
                ? FormatDateTimeWithColor(d.Value, sections, targetWidthCharacters, indexedColors, theme, uses1904DateSystem)
                : FormatNumber(d.Value, sections, targetWidthCharacters, indexedColors, theme, uses1904DateSystem),
            TextValue t     => FormatTextWithColor(t.Value, sections, indexedColors, theme),
            BoolValue b     => new FormatResult(b.Value ? "TRUE" : "FALSE"),
            ErrorValue e    => new FormatResult(e.Code),
            BlankValue      => new FormatResult(""),
            _               => new FormatResult("")
        };
    }

    // ── General format ────────────────────────────────────────────────────────

    // ── Section splitting ─────────────────────────────────────────────────────

    // ── Number formatting ─────────────────────────────────────────────────────

    private static bool TryFormatSimpleDateTime(
        double oaDate,
        string formatString,
        int? targetWidthCharacters,
        bool uses1904DateSystem,
        out FormatResult result)
    {
        if (formatString.IndexOf(';') >= 0 ||
            (formatString.Length > 0 && formatString[0] == '['))
        {
            result = new FormatResult("");
            return false;
        }

        var text = TryFormatCachedSimpleDateTime(oaDate, formatString, uses1904DateSystem, out var cachedText)
            ? cachedText
            : FormatDateTime(oaDate, formatString, uses1904DateSystem);
        text = ApplyAccountingTargetWidth(text, formatString, targetWidthCharacters);
        result = new FormatResult(text);
        return true;
    }

    private static FormatResult FormatNumber(
        double value,
        string[] sections,
        int? targetWidthCharacters,
        WorkbookIndexedColorPalette? indexedColors,
        WorkbookTheme? theme,
        bool uses1904DateSystem = false)
    {
        if (sections.Length == 1 && (sections[0].Length == 0 || sections[0][0] != '['))
        {
            // A single-section format applies to negatives by formatting the MAGNITUDE and prepending a
            // leading minus to the whole result (so "-" sits before any prefix: -¥12.30, not ¥-12.30).
            // For prefix-free formats this is identical to the inline minus, so plain formats are unaffected.
            var sign = "";
            var magnitude = value;
            if (value < 0)
            {
                sign = "-";
                magnitude = -value;
            }

            var singleSectionText = sections[0] == ""
                ? ""
                : TryFormatPlainNumericSection(magnitude, sections[0], out var plainNumericText)
                    ? plainNumericText
                    : ApplyNumericFormat(magnitude, sections[0], uses1904DateSystem: uses1904DateSystem);
            singleSectionText = ApplyAccountingTargetWidth(singleSectionText, sections[0], targetWidthCharacters);
            // Excel never displays negative zero: if sign is "-" but the formatted text
            // is all zeros (after magnitude formatting), drop the sign.
            if (sign == "-" && IsAllZeroText(singleSectionText))
                sign = "";
            return new FormatResult(sign + singleSectionText);
        }

        var parsedSections = ParseSections(sections, indexedColors, theme, out var hasConditions);

        ParsedSection section;
        double displayValue = value;

        if (hasConditions)
        {
            var selectedIndex = FindParsedSectionIndex(
                parsedSections,
                section => section.Condition is not null && section.Condition.Matches(value));
            if (selectedIndex < 0)
            {
                selectedIndex = FindParsedSectionIndex(parsedSections, section => section.Condition is null);
                if (selectedIndex < 0)
                    selectedIndex = 0;
            }

            section = parsedSections[selectedIndex];
        }
        else
        {
            (section, displayValue) = SelectPositionalSection(value, parsedSections);
        }

        string text = section.Format == ""
            ? ""
            : ApplyNumericFormat(displayValue, section.Format, uses1904DateSystem: uses1904DateSystem);
        text = ApplyAccountingTargetWidth(text, section.Format, targetWidthCharacters);
        return new FormatResult(text, section.ColorHex);
    }

    private static int FindParsedSectionIndex(
        IReadOnlyList<ParsedSection> sections,
        Func<ParsedSection, bool> predicate)
    {
        for (var index = 0; index < sections.Count; index++)
        {
            if (predicate(sections[index]))
                return index;
        }

        return -1;
    }

    private static bool TryFormatPlainNumericSection(double value, string format, out string text)
    {
        text = "";
        if (!IsPlainNumericSection(format))
            return false;

        try
        {
            text = value.ToString(format, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPlainNumericSection(string format)
    {
        var hasPlaceholder = false;
        var lastToken = '\0';
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            switch (c)
            {
                case '0':
                case '#':
                    hasPlaceholder = true;
                    lastToken = c;
                    break;
                case '.':
                case ',':
                    lastToken = c;
                    break;
                default:
                    return false;
            }
        }

        return hasPlaceholder && lastToken != ',';
    }

    private static string ApplyNumericFormat(
        double value,
        string format,
        bool preserveAccountingZeroDashAlignment = false,
        bool uses1904DateSystem = false)
    {
        if (TryFormatCjkNativeNumberText(value, format, out var cjkNativeNumberText))
            return cjkNativeNumberText;

        var nativeDigitFormat = format;
        string NativeDigits(string text) => ApplyNativeDigitSubstitution(text, nativeDigitFormat);

        if (string.IsNullOrEmpty(format) || IsGeneralFormat(format))
            return FormatNumberGeneral(value);

        if (TryResolveSpecialDateTimeLocaleToken(format, out var specialDateTimeToken))
        {
            try
            {
                // Use DateTime.FromOADate for the special locale-token path so that the
                // roundtrip DateTime→ToOADate→FromOADate is lossless for modern dates.
                // ExcelDateSystem.SerialToDate is used for the regular date-format path
                // (where the 1900 phantom-leap-day correction matters), and also here when
                // the workbook uses the 1904 date system (where the epoch itself differs).
                var dt = uses1904DateSystem ? ExcelDateSystem.SerialToDate(value, uses1904DateSystem) : DateTime.FromOADate(value);
                return NativeDigits(FormatSpecialDateTimeLocaleValue(dt, specialDateTimeToken));
            }
            catch { return value.ToString(CultureInfo.InvariantCulture); }
        }
        format = PreserveLocaleCurrencyTokens(format, out var numberFormat, out var dateTimeFormat);

        // Elapsed-time brackets: [h], [m], [s] represent total elapsed hours/minutes/seconds
        // and must be handled before the generic bracket-stripping pass.
        if (format.IndexOf('[') >= 0)
        {
            var directiveFormat = PreprocessBracketFormatDirectives(format);
            if (directiveFormat.ElapsedTimeMatch.Success)
            {
                return NativeDigits(FormatElapsedTime(
                    value,
                    directiveFormat.Format,
                    directiveFormat.ElapsedTimeMatch));
            }

            format = directiveFormat.Format;
        }

        format = PreserveAccountingFillSpace(format);
        format = RemoveSpacingAndFillDirectives(format);
        (format, value) = ApplyTrailingCommaScaling(format, value);

        // Percentage: multiply value by 100 before formatting
        int activePercentCount = CountActivePercentTokens(format);
        if (activePercentCount > 0)
        {
            double pctValue = value * Math.Pow(100, activePercentCount);
            // .NET percentage format (P) multiplies by 100 and adds %; but format string
            // containing literal '%' means we multiply ourselves and use 'F' style.
            // Replace active percent tokens with quoted literals so they stay in-place.
            string numFmt = QuoteActivePercentTokens(format).Trim();
            try
            {
                return NativeDigits(pctValue.ToString(string.IsNullOrEmpty(numFmt) ? "0" : numFmt, numberFormat));
            }
            catch { return NativeDigits(pctValue.ToString("0", numberFormat) + "%"); }
        }

        // Date / time format
        if (IsDateTimeFormat(format))
        {
            try
            {
                var dt = ExcelDateSystem.SerialToDate(value, uses1904DateSystem);
                return NativeDigits(FormatDateTimeValue(dt, format, dateTimeFormat));
            }
            catch { return value.ToString(CultureInfo.InvariantCulture); }
        }

        if (IsSimpleFractionFormat(format))
            return NativeDigits(FormatSimpleFraction(value, format));

        if (IsScientificFormat(format))
            return NativeDigits(FormatScientific(value, format, numberFormat));

        // Accounting / text literals — strip quoted strings to expose the numeric pattern
        var stripped = format.IndexOf('"') >= 0
            ? NumericQuotedTextRegex.Replace(format, "")
            : format;
        var prefix = "";
        var suffix = "";

        // Extract prefix/suffix literal text (not part of numeric pattern)
        if (format != stripped || HasActiveQuestionPlaceholder(format))
        {
            (prefix, format, suffix) = ExtractNumericAffixes(format);

            if (format.All(c => c is '?' || char.IsWhiteSpace(c)) &&
                !ShouldRenderQuestionOnlyFormat(prefix, suffix))
                return NativeDigits(prefix + suffix);

            if (string.IsNullOrEmpty(format))
                return NativeDigits(prefix + suffix);
        }

        // Pass the cleaned format to .NET — it understands #,##0.00, 0.00, 0, # etc.
        if (HasActiveQuestionPlaceholder(format))
            return NativeDigits(prefix + FormatQuestionPlaceholderNumber(value, format, numberFormat) + suffix);

        string numStr;
        try   { numStr = value.ToString(format, numberFormat); }
        catch { numStr = value.ToString(numberFormat); }

        // .NET may produce "-0" (or "-0.00" etc.) when a very small negative number
        // rounds to zero under the format. Excel never displays negative zero — strip the sign.
        if (numStr.Length > 1 && numStr[0] == '-' && IsNegativeZeroRepresentation(numStr, numberFormat))
            numStr = numStr[1..];

        var result = NativeDigits(prefix + numStr + suffix);

        // Multi-section negative formats carry an explicit '-' in the prefix (e.g. the
        // second section of "0;-0;0;…" is "-0"). If the entire formatted string is a
        // negative-zero representation, drop the sign so Excel's behaviour is matched.
        if (result.Length > 1 && result[0] == '-' && IsAllZeroText(result[1..]))
            result = result[1..];

        return result;
    }

    private static int CountActivePercentTokens(string format)
    {
        int count = 0;
        bool inQuote = false;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (c == '\\' && i + 1 < format.Length)
            {
                i++;
                continue;
            }

            if (!inQuote && c == '%')
                count++;
        }

        return count;
    }

    private static string QuoteActivePercentTokens(string format)
    {
        var result = new System.Text.StringBuilder(format.Length);
        bool inQuote = false;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                result.Append(c);
                continue;
            }

            if (c == '\\' && i + 1 < format.Length)
            {
                result.Append(c);
                result.Append(format[++i]);
                continue;
            }

            if (!inQuote && c == '%')
            {
                result.Append("\"%\"");
                continue;
            }

            result.Append(c);
        }

        return result.ToString();
    }

    private static (string Format, double Value) ApplyTrailingCommaScaling(string format, double value)
    {
        if (CountTrailingScaleCommas(format) == 0)
            return (format, value);

        var sb = new System.Text.StringBuilder(format);
        bool inQuote = false;
        int scaleCommas = 0;

        for (int i = sb.Length - 1; i >= 0; i--)
        {
            char c = sb[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (inQuote)
                continue;

            if (char.IsWhiteSpace(c))
                continue;

            if (c == ',')
            {
                if (IsEscaped(sb, i))
                    break;

                scaleCommas++;
                sb.Remove(i, 1);
                continue;
            }

            break;
        }

        return (sb.ToString(), value / Math.Pow(1000, scaleCommas));
    }

    private static int CountTrailingScaleCommas(string format)
    {
        bool inQuote = false;
        int scaleCommas = 0;

        for (int i = format.Length - 1; i >= 0; i--)
        {
            char c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (inQuote)
                continue;

            if (char.IsWhiteSpace(c))
                continue;

            if (c == ',')
            {
                if (IsEscaped(format, i))
                    break;

                scaleCommas++;
                continue;
            }

            break;
        }

        return scaleCommas;
    }

    /// <summary>
    /// Returns true when a formatted string like "-0" or "-0.00" is a negative-zero
    /// representation — all digit characters after the minus sign are the numeric
    /// separator characters or "0". Excel never displays negative zero; callers
    /// should strip the leading "-" in that case.
    /// </summary>
    private static bool IsNegativeZeroRepresentation(string numStr, NumberFormatInfo fmt)
    {
        // numStr starts with "-"; scan the rest for any non-zero digit.
        for (int i = 1; i < numStr.Length; i++)
        {
            char c = numStr[i];
            // A non-zero digit means this is not "negative zero".
            if (char.IsDigit(c) && c != '0')
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns true when every digit character in <paramref name="text"/> is '0'
    /// (i.e. the text represents a zero value with possible non-digit decoration).
    /// Used to detect "negative zero" situations such as "0", "0.00", "0,000".
    /// </summary>
    private static bool IsAllZeroText(string text)
    {
        bool hasDigit = false;
        foreach (char c in text)
        {
            if (char.IsDigit(c))
            {
                hasDigit = true;
                if (c != '0')
                    return false;
            }
        }
        return hasDigit;
    }

    private static bool IsEscaped(string text, int index)
    {
        int slashCount = 0;
        for (int i = index - 1; i >= 0 && text[i] == '\\'; i--)
            slashCount++;

        return slashCount % 2 == 1;
    }

    private static bool IsEscaped(System.Text.StringBuilder text, int index)
    {
        int slashCount = 0;
        for (int i = index - 1; i >= 0 && text[i] == '\\'; i--)
            slashCount++;

        return slashCount % 2 == 1;
    }

    private static (string Prefix, string NumericFormat, string Suffix) ExtractNumericAffixes(string format)
    {
        var unquotedBuilder = new System.Text.StringBuilder(format.Length);
        int start = -1;
        int end = -1;
        bool inQuote = false;

        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && c == '\\' && i + 1 < format.Length)
            {
                var escaped = format[++i];
                if (!IsNumericPlaceholder(escaped))
                    unquotedBuilder.Append('\\');
                unquotedBuilder.Append(escaped);
                continue;
            }

            int outputIndex = unquotedBuilder.Length;
            unquotedBuilder.Append(c);

            if (!inQuote && IsNumericPlaceholder(c))
            {
                if (start < 0)
                    start = outputIndex;
                end = outputIndex;
            }
        }

        string unquoted = unquotedBuilder.ToString();

        if (start < 0 || end < start)
            return (unquoted, "", "");

        return (unquoted[..start], unquoted[start..(end + 1)], unquoted[(end + 1)..]);
    }

    private static bool IsNumericPlaceholder(char c)
        => c is '0' or '#' or '?';

    private static bool IsGeneralFormat(string format) =>
        string.Equals(format, "General", StringComparison.OrdinalIgnoreCase);

    // ── Date/time formatting ──────────────────────────────────────────────────

}

