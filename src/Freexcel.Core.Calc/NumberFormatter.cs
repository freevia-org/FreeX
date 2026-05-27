using System.Globalization;
using System.Text.RegularExpressions;
using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

public static partial class NumberFormatter
{
    private static readonly Regex NumericElapsedTokenRegex = new(@"\[([hH])\]|\[([mM])\]|\[([sS])\]");
    private static readonly Regex NumericBracketDirectiveRegex = new(@"\[[^\]]*\]");
    private static readonly Regex NumericQuotedTextRegex = new("\"[^\"]*\"");

    // Returned alongside display text so the grid can apply conditional colors.
    public sealed record FormatResult(string Text, string? ColorHex = null);

    public static string Format(ScalarValue value, string formatString)
        => FormatWithColor(value, formatString).Text;

    public static string Format(ScalarValue value, string formatString, int targetWidthCharacters)
        => FormatWithColor(value, formatString, targetWidthCharacters).Text;

    public static FormatResult FormatWithColor(ScalarValue value, string formatString)
        => FormatWithColor(value, formatString, (int?)null);

    public static FormatResult FormatWithColor(ScalarValue value, string formatString, int targetWidthCharacters)
        => FormatWithColor(value, formatString, (int?)targetWidthCharacters);

    public static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        WorkbookIndexedColorPalette indexedColors)
        => FormatWithColor(value, formatString, (int?)null, indexedColors);

    public static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        int targetWidthCharacters,
        WorkbookIndexedColorPalette indexedColors)
        => FormatWithColor(value, formatString, (int?)targetWidthCharacters, indexedColors);

    private static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        int? targetWidthCharacters,
        WorkbookIndexedColorPalette? indexedColors = null)
    {
        if (string.IsNullOrEmpty(formatString) || IsGeneralFormat(formatString))
            return new FormatResult(FormatGeneral(value));

        // Pure text format
        if (formatString == "@")
        {
            return value switch
            {
                TextValue t   => new FormatResult(t.Value),
                NumberValue n => new FormatResult(FormatGeneral(value)),
                _             => new FormatResult(FormatGeneral(value))
            };
        }

        var sections = SplitSections(formatString);

        return value switch
        {
            NumberValue n   => FormatNumber(n.Value, sections, targetWidthCharacters, indexedColors),
            DateTimeValue d => FormatDateTimeWithColor(d.Value, sections, targetWidthCharacters, indexedColors),
            TextValue t     => FormatTextWithColor(t.Value, sections, indexedColors),
            BoolValue b     => new FormatResult(b.Value ? "TRUE" : "FALSE"),
            ErrorValue e    => new FormatResult(e.Code),
            BlankValue      => new FormatResult(""),
            _               => new FormatResult("")
        };
    }

    // ── General format ────────────────────────────────────────────────────────

    // ── Section splitting ─────────────────────────────────────────────────────

    // ── Number formatting ─────────────────────────────────────────────────────

    private static FormatResult FormatNumber(
        double value,
        string[] sections,
        int? targetWidthCharacters,
        WorkbookIndexedColorPalette? indexedColors)
    {
        var parsedSections = sections.Select(section => ParseSection(section, indexedColors)).ToArray();
        bool hasConditions = parsedSections.Any(section => section.Condition is not null);

        ParsedSection section;
        double displayValue = value;

        if (hasConditions)
        {
            int selectedIndex = Array.FindIndex(parsedSections, section =>
                section.Condition is not null && section.Condition.Matches(value));
            if (selectedIndex < 0)
            {
                selectedIndex = Array.FindIndex(parsedSections, section => section.Condition is null);
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
            : ApplyNumericFormat(displayValue, section.Format);
        text = ApplyAccountingTargetWidth(text, section.Format, targetWidthCharacters);
        return new FormatResult(text, section.ColorHex);
    }

    private static string ApplyNumericFormat(
        double value,
        string format,
        bool preserveAccountingZeroDashAlignment = false)
    {
        if (string.IsNullOrEmpty(format) || IsGeneralFormat(format))
            return FormatNumberGeneral(value);

        if (TryResolveSpecialDateTimeLocaleToken(format, out var specialDateTimeToken))
        {
            try
            {
                var dt = DateTime.FromOADate(value);
                return FormatSpecialDateTimeLocaleValue(dt, specialDateTimeToken);
            }
            catch { return value.ToString(CultureInfo.InvariantCulture); }
        }
        format = PreserveLocaleCurrencyTokens(format, out var numberFormat, out var dateTimeFormat);

        // Elapsed-time brackets: [h], [m], [s] represent total elapsed hours/minutes/seconds
        // and must be handled before the generic bracket-stripping pass.
        var elapsedMatch = NumericElapsedTokenRegex.Match(format);
        if (elapsedMatch.Success)
        {
            return FormatElapsedTime(value, RemoveSpacingAndFillDirectives(format), elapsedMatch);
        }

        // Remove any remaining bracket content (conditions, locale, etc.)
        format = NumericBracketDirectiveRegex.Replace(format, "");
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
                return pctValue.ToString(string.IsNullOrEmpty(numFmt) ? "0" : numFmt, numberFormat);
            }
            catch { return pctValue.ToString("0", numberFormat) + "%"; }
        }

        // Date / time format
        if (IsDateTimeFormat(format))
        {
            try
            {
                var dt = DateTime.FromOADate(value);
                return FormatDateTimeValue(dt, format, dateTimeFormat);
            }
            catch { return value.ToString(CultureInfo.InvariantCulture); }
        }

        if (IsSimpleFractionFormat(format))
            return FormatSimpleFraction(value, format);

        if (IsScientificFormat(format))
            return FormatScientific(value, format, numberFormat);

        // Accounting / text literals — strip quoted strings to expose the numeric pattern
        var stripped = NumericQuotedTextRegex.Replace(format, "");
        var prefix = "";
        var suffix = "";

        // Extract prefix/suffix literal text (not part of numeric pattern)
        if (format != stripped || HasActiveQuestionPlaceholder(format))
        {
            (prefix, format, suffix) = ExtractNumericAffixes(format);

            if (format.All(c => c is '?' || char.IsWhiteSpace(c)) &&
                !ShouldRenderQuestionOnlyFormat(prefix, suffix))
                return prefix + suffix;

            if (string.IsNullOrEmpty(format))
                return prefix + suffix;
        }

        // Pass the cleaned format to .NET — it understands #,##0.00, 0.00, 0, # etc.
        if (HasActiveQuestionPlaceholder(format))
            return prefix + FormatQuestionPlaceholderNumber(value, format, numberFormat) + suffix;

        string numStr;
        try   { numStr = value.ToString(format, numberFormat); }
        catch { numStr = value.ToString(numberFormat); }

        return prefix + numStr + suffix;
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

        if (scaleCommas == 0)
            return (format, value);

        return (sb.ToString(), value / Math.Pow(1000, scaleCommas));
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

