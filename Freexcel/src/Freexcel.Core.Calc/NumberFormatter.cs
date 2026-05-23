using System.Globalization;
using System.Text.RegularExpressions;
using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

public static partial class NumberFormatter
{
    // Returned alongside display text so the grid can apply conditional colors.
    public sealed record FormatResult(string Text, string? ColorHex = null);

    public static string Format(ScalarValue value, string formatString)
        => FormatWithColor(value, formatString).Text;

    public static FormatResult FormatWithColor(ScalarValue value, string formatString)
    {
        if (string.IsNullOrEmpty(formatString) || formatString == "General")
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
            NumberValue n   => FormatNumber(n.Value, sections),
            DateTimeValue d => FormatDateTimeWithColor(d.Value, sections),
            TextValue t     => FormatTextWithColor(t.Value, sections),
            BoolValue b     => new FormatResult(b.Value ? "TRUE" : "FALSE"),
            ErrorValue e    => new FormatResult(e.Code),
            BlankValue      => new FormatResult(""),
            _               => new FormatResult("")
        };
    }

    // ── General format ────────────────────────────────────────────────────────

    private static string FormatGeneral(ScalarValue value) => value switch
    {
        NumberValue n   => FormatNumberGeneral(n.Value),
        DateTimeValue d => FormatGeneralDateTime(d.Value),
        TextValue t     => t.Value,
        BoolValue b     => b.Value ? "TRUE" : "FALSE",
        ErrorValue e    => e.Code,
        BlankValue      => "",
        _               => ""
    };

    private static string FormatGeneralDateTime(double value)
    {
        try { return DateTime.FromOADate(value).ToString("d", CultureInfo.InvariantCulture); }
        catch { return FormatNumberGeneral(value); }
    }

    private static string FormatNumberGeneral(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return value.ToString(CultureInfo.InvariantCulture);
        if (value == Math.Truncate(value) && Math.Abs(value) < 1e15)
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        return value.ToString("G10", CultureInfo.InvariantCulture);
    }

    // ── Section splitting ─────────────────────────────────────────────────────

    // ── Number formatting ─────────────────────────────────────────────────────

    private static FormatResult FormatNumber(double value, string[] sections)
    {
        var parsedSections = sections.Select(ParseSection).ToArray();
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

        string text = ApplyNumericFormat(displayValue, section.Format);
        return new FormatResult(text, section.ColorHex);
    }

    private static string ApplyNumericFormat(double value, string format)
    {
        if (string.IsNullOrEmpty(format) || format == "General")
            return FormatNumberGeneral(value);

        if (TryResolveSpecialDateTimeLocaleToken(format, out var specialDateTimeFormat))
        {
            try
            {
                var dt = DateTime.FromOADate(value);
                return FormatDateTimeValue(dt, specialDateTimeFormat, CultureInfo.InvariantCulture.DateTimeFormat);
            }
            catch { return value.ToString(CultureInfo.InvariantCulture); }
        }
        format = PreserveLocaleCurrencyTokens(format, out var numberFormat, out var dateTimeFormat);

        // Elapsed-time brackets: [h], [m], [s] represent total elapsed hours/minutes/seconds
        // and must be handled before the generic bracket-stripping pass.
        var elapsedMatch = Regex.Match(format, @"\[([hH])\]|\[([mM])\]|\[([sS])\]");
        if (elapsedMatch.Success)
        {
            return FormatElapsedTime(value, RemoveSpacingAndFillDirectives(format), elapsedMatch);
        }

        // Remove any remaining bracket content (conditions, locale, etc.)
        format = Regex.Replace(format, @"\[[^\]]*\]", "");
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
        var stripped = Regex.Replace(format, "\"[^\"]*\"", "");
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

    private static string RemoveSpacingAndFillDirectives(string format)
    {
        var sb = new System.Text.StringBuilder(format.Length);
        bool inQuote = false;

        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                sb.Append(c);
                continue;
            }

            if (!inQuote && c == '\\' && i + 1 < format.Length)
            {
                sb.Append(c);
                sb.Append(format[++i]);
                continue;
            }

            if (!inQuote && c == '_')
            {
                if (i + 1 < format.Length) i++;
                continue;
            }

            if (!inQuote && c == '*')
            {
                if (i + 1 < format.Length) i++;
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string PreserveAccountingFillSpace(string format)
    {
        var sb = new System.Text.StringBuilder(format.Length);
        bool inQuote = false;

        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                sb.Append(c);
                continue;
            }

            if (!inQuote && IsCurrencySymbol(c) &&
                i + 2 < format.Length && format[i + 1] == '*' && format[i + 2] == ' ')
            {
                sb.Append(c);
                sb.Append(' ');
                i += 2;
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool IsCurrencySymbol(char c)
        => c is '$' or '\u00A3' or '\u20AC' or '\u00A5';

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

    // ── Date/time formatting ──────────────────────────────────────────────────

    private static FormatResult FormatTextWithColor(string text, string[] sections)
    {
        if (sections.Length <= 3 || string.IsNullOrEmpty(sections[3]))
        {
            var firstSection = ParseSection(sections[0]);
            return firstSection.Format.Contains('@', StringComparison.Ordinal)
                ? new FormatResult(ApplyTextSection(firstSection.Format, text), firstSection.ColorHex)
                : new FormatResult(text);
        }

        var parsed = ParseSection(sections[3]);
        return new FormatResult(ApplyTextSection(parsed.Format, text), parsed.ColorHex);
    }

    private static string ApplyTextSection(string section, string text)
    {
        // `@` is the text placeholder; surrounding quotes and escaped characters are literals.
        // Spacing/fill directives affect layout in Excel, not the displayed text payload.
        var result = new System.Text.StringBuilder();
        bool inQuote = false;
        for (int i = 0; i < section.Length; i++)
        {
            char c = section[i];
            if (c == '"') { inQuote = !inQuote; continue; }
            if (inQuote) { result.Append(c); continue; }

            if (c == '\\' && i + 1 < section.Length)
            {
                result.Append(section[++i]);
                continue;
            }

            if (c is '_' or '*' && i + 1 < section.Length)
            {
                i++;
                continue;
            }

            if (c == '@') result.Append(text);
            else result.Append(c);
        }
        return result.ToString();
    }
}

