using System.Globalization;
using System.Text.RegularExpressions;
using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

public static class NumberFormatter
{
    private readonly record struct LocaleFormatSeparators(
        string DecimalSeparator,
        string GroupSeparator,
        string DateSeparator,
        int[]? NumberGroupSizes = null);

    private static readonly IReadOnlyDictionary<string, LocaleFormatSeparators> LocaleFormatCatalog =
        new Dictionary<string, LocaleFormatSeparators>(StringComparer.OrdinalIgnoreCase)
        {
            ["402"] = new(",", "\u00A0", "."),
            ["401"] = new(".", ",", "/"),
            ["409"] = new(".", ",", "/"),
            ["405"] = new(",", " ", "."),
            ["406"] = new(",", ".", "-"),
            ["407"] = new(",", ".", "."),
            ["408"] = new(",", ".", "/"),
            ["404"] = new(".", ",", "/"),
            ["40D"] = new(".", ",", "/"),
            ["40A"] = new(",", ".", "/"),
            ["40B"] = new(",", " ", "."),
            ["40C"] = new(",", " ", "/"),
            ["40E"] = new(",", " ", "."),
            ["410"] = new(",", ".", "/"),
            ["411"] = new(".", ",", "/"),
            ["412"] = new(".", ",", "-"),
            ["413"] = new(",", ".", "-"),
            ["414"] = new(",", " ", "."),
            ["415"] = new(",", " ", "."),
            ["416"] = new(",", ".", "/"),
            ["418"] = new(",", ".", "."),
            ["419"] = new(",", " ", "."),
            ["41A"] = new(",", ".", "."),
            ["41B"] = new(",", "\u00A0", "."),
            ["41D"] = new(",", " ", "-"),
            ["41E"] = new(".", ",", "/"),
            ["41F"] = new(",", ".", "."),
            ["420"] = new(".", ",", "/"),
            ["421"] = new(",", ".", "/"),
            ["422"] = new(",", " ", "."),
            ["424"] = new(",", ".", "."),
            ["425"] = new(",", "\u00A0", "."),
            ["426"] = new(",", "\u00A0", "."),
            ["427"] = new(",", "\u00A0", "-"),
            ["429"] = new("/", ",", "/"),
            ["42A"] = new(",", ".", "/"),
            ["42B"] = new(".", ",", "."),
            ["42C"] = new(",", ".", "."),
            ["434"] = new(".", "\u00A0", "/"),
            ["435"] = new(".", ",", "/"),
            ["436"] = new(",", "\u00A0", "-"),
            ["437"] = new(",", "\u00A0", "."),
            ["439"] = new(".", ",", "-", [3, 2]),
            ["43F"] = new(",", "\u00A0", "."),
            ["440"] = new(",", "\u00A0", "/"),
            ["441"] = new(".", ",", "/"),
            ["443"] = new(",", "\u00A0", "/"),
            ["43E"] = new(".", ",", "/"),
            ["450"] = new(".", ",", "."),
            ["453"] = new(".", ",", "/"),
            ["454"] = new(",", ".", "/"),
            ["455"] = new(".", ",", "/"),
            ["45B"] = new(".", ",", "-"),
            ["45E"] = new(".", ",", "/"),
            ["461"] = new(".", ",", "/"),
            ["463"] = new(",", ".", "/"),
            ["468"] = new(".", ",", "/"),
            ["46A"] = new(".", ",", "/"),
            ["470"] = new(".", ",", "/"),
            ["492"] = new(".", ",", "/"),
            ["804"] = new(".", ",", "/"),
            ["809"] = new(".", ",", "/"),
            ["80A"] = new(".", ",", "/"),
            ["807"] = new(".", "'", "."),
            ["813"] = new(",", ".", "/"),
            ["816"] = new(",", " ", "/"),
            ["100A"] = new(".", ",", "/"),
            ["C04"] = new(".", ",", "/"),
            ["C01"] = new(".", ",", "/"),
            ["C09"] = new(".", ",", "/"),
            ["C0C"] = new(",", "\u00A0", "-"),
            ["C0A"] = new(",", ".", "/"),
            ["1009"] = new(".", ",", "-"),
            ["100C"] = new(".", "'", "."),
            ["1409"] = new(".", ",", "/"),
            ["140A"] = new(",", "\u00A0", "/"),
            ["1801"] = new(".", ",", "-"),
            ["1809"] = new(".", ",", "/"),
            ["180A"] = new(".", ",", "/"),
            ["1C09"] = new(",", "\u00A0", "/"),
            ["1C0A"] = new(".", ",", "/"),
            ["200A"] = new(",", ".", "/"),
            ["241A"] = new(",", ".", "."),
            ["240A"] = new(",", ".", "/"),
            ["280A"] = new(".", ",", "/"),
            ["280C"] = new(",", "\u202F", "/"),
            ["2C0A"] = new(",", ".", "/"),
            ["300A"] = new(",", ".", "/"),
            ["340A"] = new(",", ".", "-"),
            ["380A"] = new(",", ".", "/"),
            ["3801"] = new(".", ",", "/"),
            ["380C"] = new(",", ".", "/"),
            ["3C0A"] = new(",", ".", "/"),
            ["400A"] = new(",", ".", "/"),
            ["4009"] = new(".", ",", "/", [3, 2]),
            ["445"] = new(".", ",", "-", [3, 2]),
            ["447"] = new(".", ",", "-", [3, 2]),
            ["449"] = new(".", ",", "-", [3, 2]),
            ["44A"] = new(".", ",", "-", [3, 2]),
            ["44E"] = new(".", ",", "-", [3, 2]),
            ["440A"] = new(".", ",", "/"),
            ["500A"] = new(".", ",", "/")
        };

    // Returned alongside display text so the grid can apply conditional colors.
    public sealed record FormatResult(string Text, string? ColorHex = null);

    private sealed record ParsedSection(string Format, string? ColorHex, FormatCondition? Condition);

    private sealed record FormatCondition(string Operator, double Value)
    {
        public bool Matches(double value) => Operator switch
        {
            ">"  => value > Value,
            ">=" => value >= Value,
            "<"  => value < Value,
            "<=" => value <= Value,
            "="  => value == Value,
            "<>" => value != Value,
            _    => false
        };
    }

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

    // Split format into sections separated by ';' that are not inside "" or []
    private static string[] SplitSections(string format)
    {
        var sections = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuote = false;
        bool inBracket = false;

        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"' && !inBracket)
            {
                inQuote = !inQuote;
                sb.Append(c);
            }
            else if (c == '[' && !inQuote)
            {
                inBracket = true;
                sb.Append(c);
            }
            else if (c == ']' && !inQuote)
            {
                inBracket = false;
                sb.Append(c);
            }
            else if (c == ';' && !inQuote && !inBracket)
            {
                sections.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        sections.Add(sb.ToString());
        return [.. sections];
    }

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

    private static (ParsedSection Section, double DisplayValue) SelectPositionalSection(
        double value,
        ParsedSection[] sections)
    {
        if (value > 0 || sections.Length == 1)
            return (sections[0], value);

        if (value < 0)
        {
            if (sections.Length >= 2 && sections[1].Format != "")
                return (sections[1], Math.Abs(value));

            return (sections[0], value);
        }

        if (sections.Length >= 3 && sections[2].Format != "")
            return (sections[2], value);

        return (sections[0], value);
    }

    private static ParsedSection ParseSection(string section)
    {
        string? color = null;
        FormatCondition? condition = null;
        int index = 0;

        while (index < section.Length && section[index] == '[')
        {
            int close = section.IndexOf(']', index + 1);
            if (close < 0)
                break;

            string token = section[(index + 1)..close];
            if (NumberFormatColorMapper.TryMapColor(token, out var tokenColor))
            {
                color = tokenColor;
                index = close + 1;
                continue;
            }

            if (TryParseCondition(token, out var tokenCondition))
            {
                condition = tokenCondition;
                index = close + 1;
                continue;
            }

            break;
        }

        return new ParsedSection(section[index..], color, condition);
    }

    private static bool TryParseCondition(string token, out FormatCondition? condition)
    {
        var match = Regex.Match(token, @"^(>=|<=|<>|>|<|=)\s*(-?(?:\d+(?:\.\d*)?|\.\d+))$");
        if (match.Success &&
            double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            condition = new FormatCondition(match.Groups[1].Value, value);
            return true;
        }

        condition = null;
        return false;
    }

    private static string ApplyNumericFormat(double value, string format)
    {
        if (string.IsNullOrEmpty(format) || format == "General")
            return FormatNumberGeneral(value);

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
        if (format != stripped)
        {
            (prefix, format, suffix) = ExtractNumericAffixes(format);

            if (format.All(c => c is '?' || char.IsWhiteSpace(c)))
                return prefix + suffix;

            if (string.IsNullOrEmpty(format))
                return prefix + suffix;
        }

        // Pass the cleaned format to .NET — it understands #,##0.00, 0.00, 0, # etc.
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

    private static string PreserveLocaleCurrencyTokens(
        string format,
        out NumberFormatInfo numberFormat,
        out DateTimeFormatInfo dateTimeFormat)
    {
        numberFormat = CultureInfo.InvariantCulture.NumberFormat;
        dateTimeFormat = CultureInfo.InvariantCulture.DateTimeFormat;
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

            if (!inQuote &&
                c == '[' &&
                i + 2 < format.Length &&
                format[i + 1] == '$')
            {
                int close = format.IndexOf(']', i + 2);
                if (close > i)
                {
                    string token = format[(i + 2)..close];
                    int localeSeparator = token.LastIndexOf('-');
                    var localeToken = localeSeparator >= 0 ? token[(localeSeparator + 1)..] : null;
                    if (localeToken is not null &&
                        TryCreateLocaleFormats(localeToken, out var localeNumberFormat, out var localeDateTimeFormat))
                    {
                        numberFormat = localeNumberFormat;
                        dateTimeFormat = localeDateTimeFormat;
                    }

                    if (localeSeparator == 0)
                    {
                        i = close;
                        continue;
                    }

                    string symbol = localeSeparator > 0 ? token[..localeSeparator] : token;
                    if (!string.IsNullOrEmpty(symbol))
                    {
                        sb.Append('"');
                        sb.Append(symbol.Replace("\"", "\"\"", StringComparison.Ordinal));
                        sb.Append('"');
                        if (close + 2 < format.Length &&
                            format[close + 1] == '*' &&
                            format[close + 2] == ' ')
                        {
                            sb.Append(' ');
                            i = close + 2;
                            continue;
                        }

                        i = close;
                        continue;
                    }
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool TryCreateLocaleFormats(
        string localeToken,
        out NumberFormatInfo numberFormat,
        out DateTimeFormatInfo dateTimeFormat)
    {
        numberFormat = CultureInfo.InvariantCulture.NumberFormat;
        dateTimeFormat = CultureInfo.InvariantCulture.DateTimeFormat;
        var normalized = localeToken.Trim().TrimStart('0').ToUpperInvariant();
        if (normalized.Length == 0)
            normalized = "0";

        if (!LocaleFormatCatalog.TryGetValue(normalized, out var separators))
            return false;

        numberFormat = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        numberFormat.NumberDecimalSeparator = separators.DecimalSeparator;
        numberFormat.NumberGroupSeparator = separators.GroupSeparator;
        numberFormat.PercentDecimalSeparator = separators.DecimalSeparator;
        numberFormat.PercentGroupSeparator = separators.GroupSeparator;
        if (separators.NumberGroupSizes is { Length: > 0 } groupSizes)
        {
            numberFormat.NumberGroupSizes = groupSizes;
            numberFormat.PercentGroupSizes = groupSizes;
        }
        dateTimeFormat = (DateTimeFormatInfo)CultureInfo.InvariantCulture.DateTimeFormat.Clone();
        dateTimeFormat.DateSeparator = separators.DateSeparator;
        return true;
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

    private static bool IsSimpleFractionFormat(string format)
    {
        var stripped = Regex.Replace(format, "\"[^\"]*\"", "");
        return stripped.Contains("?/?", StringComparison.Ordinal) ||
               stripped.Contains("?/??", StringComparison.Ordinal) ||
               stripped.Contains("??/??", StringComparison.Ordinal) ||
               Regex.IsMatch(stripped, @"\?+/\d+");
    }

    private static string FormatSimpleFraction(double value, string format)
    {
        var (prefix, numericFormat, suffix) = ExtractNumericAffixes(format);
        var stripped = Regex.Replace(numericFormat, "\"[^\"]*\"", "");
        int? fixedDenominator = null;
        var fixedDenominatorMatch = Regex.Match(suffix, @"^/(\d+)");
        if (fixedDenominatorMatch.Success &&
            int.TryParse(fixedDenominatorMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDenominator) &&
            parsedDenominator > 0)
        {
            fixedDenominator = parsedDenominator;
            suffix = suffix[fixedDenominatorMatch.Length..];
        }

        var denominatorPattern = Regex.Match(stripped, @"/(\?+)");
        int maxDenominator = denominatorPattern.Success && denominatorPattern.Groups[1].Value.Length >= 2 ? 99 : 9;

        double absValue = Math.Abs(value);
        int whole = stripped.Contains('#') || stripped.Contains('0') ? (int)Math.Floor(absValue) : 0;
        double fractional = absValue - whole;

        var (numerator, denominator) = fixedDenominator is { } denominatorValue
            ? ((int)Math.Round(fractional * denominatorValue), denominatorValue)
            : ApproximateFraction(fractional, maxDenominator);
        if (numerator == denominator)
        {
            whole++;
            numerator = 0;
        }

        var sign = value < 0 ? "-" : "";
        if (numerator == 0)
            return prefix + sign + (whole == 0 ? "0" : whole.ToString(CultureInfo.InvariantCulture)) + suffix;

        string fraction = numerator.ToString(CultureInfo.InvariantCulture) + "/" +
                          denominator.ToString(CultureInfo.InvariantCulture);
        string number = whole > 0
            ? sign + whole.ToString(CultureInfo.InvariantCulture) + " " + fraction
            : sign + fraction;
        return prefix + number + suffix;
    }

    private static (int Numerator, int Denominator) ApproximateFraction(double value, int maxDenominator)
    {
        int bestNumerator = 0;
        int bestDenominator = 1;
        double bestError = double.MaxValue;

        for (int denominator = 1; denominator <= maxDenominator; denominator++)
        {
            int numerator = (int)Math.Round(value * denominator);
            double error = Math.Abs(value - numerator / (double)denominator);
            if (error < bestError)
            {
                bestError = error;
                bestNumerator = numerator;
                bestDenominator = denominator;
            }
        }

        int gcd = GreatestCommonDivisor(bestNumerator, bestDenominator);
        return (bestNumerator / gcd, bestDenominator / gcd);
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b != 0)
        {
            int t = b;
            b = a % b;
            a = t;
        }
        return a == 0 ? 1 : a;
    }

    private static bool IsScientificFormat(string format)
    {
        var stripped = Regex.Replace(format, "\"[^\"]*\"", "");
        return Regex.IsMatch(stripped, @"E[+-]0+", RegexOptions.IgnoreCase);
    }

    private static string FormatScientific(double value, string format, IFormatProvider formatProvider)
    {
        var (prefix, numericFormat, suffix) = ExtractNumericAffixes(format);
        var stripped = Regex.Replace(numericFormat, "\"[^\"]*\"", "");
        var mantissa = stripped.Split(['E', 'e'], 2)[0];
        int decimals = mantissa.Contains('.')
            ? mantissa[(mantissa.IndexOf('.') + 1)..].Count(c => c == '0' || c == '#')
            : 0;

        string result = value.ToString("E" + decimals.ToString(CultureInfo.InvariantCulture), formatProvider);
        result = Regex.Replace(result, @"E([+-])(\d{2,})$", match =>
        {
            string sign = match.Groups[1].Value;
            string exponent = match.Groups[2].Value;
            var exponentFormat = Regex.Match(stripped, @"E([+-])(0+)", RegexOptions.IgnoreCase);
            int minDigits = exponentFormat.Groups[2].Value.Length;
            exponent = int.Parse(exponent, CultureInfo.InvariantCulture).ToString("D" + minDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            string displaySign = exponentFormat.Groups[1].Value == "-" && sign == "+" ? "" : sign;
            return "E" + displaySign + exponent;
        });
        return prefix + result + suffix;
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

    private static FormatResult FormatDateTimeWithColor(double oaDate, string[] sections)
    {
        var parsed = SelectDateTimeSection(oaDate, sections);
        return new FormatResult(FormatDateTime(oaDate, parsed.Format), parsed.ColorHex);
    }

    private static ParsedSection SelectDateTimeSection(double value, string[] sections)
    {
        var parsedSections = sections.Select(ParseSection).ToArray();
        if (!parsedSections.Any(section => section.Condition is not null))
            return parsedSections[0];

        var selectedIndex = Array.FindIndex(parsedSections, section =>
            section.Condition is not null && section.Condition.Matches(value));
        if (selectedIndex >= 0)
            return parsedSections[selectedIndex];

        selectedIndex = Array.FindIndex(parsedSections, section => section.Condition is null);
        return selectedIndex >= 0 ? parsedSections[selectedIndex] : parsedSections[0];
    }

    private static string FormatDateTime(double oaDate, string format)
    {
        var (_, cleanFmt) = NumberFormatColorMapper.ExtractColor(format);
        cleanFmt = PreserveLocaleCurrencyTokens(cleanFmt, out _, out var dateTimeFormat);
        cleanFmt = Regex.Replace(cleanFmt, @"\[[^\]]*\]", "");
        cleanFmt = RemoveSpacingAndFillDirectives(cleanFmt);
        try
        {
            var dt = DateTime.FromOADate(oaDate);
            if (IsDateTimeFormat(cleanFmt))
                return FormatDateTimeValue(dt, cleanFmt, dateTimeFormat);
            return dt.ToString(cleanFmt, dateTimeFormat);
        }
        catch { return oaDate.ToString(CultureInfo.InvariantCulture); }
    }

    private static string FormatDateTimeValue(
        DateTime dateTime,
        string excelFormat,
        DateTimeFormatInfo dateTimeFormat)
    {
        if (TryGetFractionalSecondPrecision(excelFormat, out int precision))
            dateTime = RoundToFractionalSecondPrecision(dateTime, precision);

        var preparedFormat = ReplaceMonthInitialTokens(excelFormat, dateTime, dateTimeFormat);
        return dateTime.ToString(ToNetDateFormat(preparedFormat), dateTimeFormat);
    }

    private static string ReplaceMonthInitialTokens(
        string excelFormat,
        DateTime dateTime,
        DateTimeFormatInfo dateTimeFormat)
    {
        var result = new System.Text.StringBuilder();
        string monthName = dateTimeFormat.MonthNames[dateTime.Month - 1];
        if (string.IsNullOrEmpty(monthName))
            monthName = dateTime.ToString("MMMM", dateTimeFormat);

        string initial = monthName[..1].Replace("'", "''", StringComparison.Ordinal);
        for (int i = 0; i < excelFormat.Length;)
        {
            if (excelFormat[i] == '"')
            {
                int end = excelFormat.IndexOf('"', i + 1);
                if (end < 0) end = excelFormat.Length - 1;
                result.Append(excelFormat[i..(end + 1)]);
                i = end + 1;
                continue;
            }

            if (excelFormat[i] == '\\' && i + 1 < excelFormat.Length)
            {
                result.Append(excelFormat, i, 2);
                i += 2;
                continue;
            }

            if (i + 5 <= excelFormat.Length &&
                string.Compare(excelFormat, i, "mmmmm", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result.Append('\'');
                result.Append(initial);
                result.Append('\'');
                i += 5;
                continue;
            }

            result.Append(excelFormat[i++]);
        }

        return result.ToString();
    }

    private static bool TryGetFractionalSecondPrecision(string format, out int precision)
    {
        var match = Regex.Match(format, @"(?<=[sS])\.(0+)");
        if (match.Success)
        {
            precision = match.Groups[1].Value.Length;
            return true;
        }

        precision = 0;
        return false;
    }

    private static DateTime RoundToFractionalSecondPrecision(DateTime dateTime, int precision)
    {
        if (precision >= 7)
            return dateTime;

        long scale = (long)Math.Pow(10, 7 - precision);
        long roundedTicks = ((dateTime.Ticks + (scale / 2)) / scale) * scale;
        return new DateTime(roundedTicks, dateTime.Kind);
    }

    // Detect date/time format: has date/time tokens and no digit-only tokens
    private static bool IsDateTimeFormat(string format)
    {
        // Strip quoted strings before checking
        var stripped = Regex.Replace(format, "\"[^\"]*\"", "");
        stripped = Regex.Replace(stripped, @"(?<=[sS])\.0+", "");
        bool hasDateToken = stripped.IndexOfAny(['y', 'Y', 'd', 'D', 'h', 'H', 's', 'S', 'm', 'M']) >= 0;
        bool hasNumberToken = stripped.IndexOfAny(['0', '#']) >= 0;
        return hasDateToken && !hasNumberToken;
    }

    // Map Excel date format tokens to .NET format string equivalents.
    private static string ToNetDateFormat(string excelFmt)
    {
        // When AM/PM is present the hour token uses 12-hour lowercase (h/hh);
        // without AM/PM use 24-hour uppercase (H/HH).
        bool hasAmPm =
            excelFmt.IndexOf("AM/PM", StringComparison.OrdinalIgnoreCase) >= 0 ||
            excelFmt.IndexOf("A/P", StringComparison.OrdinalIgnoreCase) >= 0;
        string hourToken2 = hasAmPm ? "hh" : "HH";
        string hourToken1 = hasAmPm ? "h"  : "H";

        // Process token by token to avoid double-replacement issues
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < excelFmt.Length)
        {
            char c = excelFmt[i];
            if (c == '"')
            {
                // Copy quoted literal unchanged
                int end = excelFmt.IndexOf('"', i + 1);
                if (end < 0) end = excelFmt.Length - 1;
                sb.Append(excelFmt[i..(end + 1)]);
                i = end + 1;
                continue;
            }
            if (c == '\\' && i + 1 < excelFmt.Length)
            {
                sb.Append('\'');
                if (excelFmt[i + 1] == '\'')
                    sb.Append("''");
                else
                    sb.Append(excelFmt[i + 1]);
                sb.Append('\'');
                i += 2;
                continue;
            }
            // Longest-match for each token group
            if (TryConsumeFractionalSeconds(excelFmt, i, sb, out int ni) ||
                TryConsume(excelFmt, i, "AM/PM", "tt", sb, out ni) ||
                TryConsume(excelFmt, i, "am/pm", "tt", sb, out ni) ||
                TryConsume(excelFmt, i, "A/P", "t", sb, out ni) ||
                TryConsume(excelFmt, i, "a/p", "t", sb, out ni) ||
                TryConsume(excelFmt, i, "yyyy", "yyyy", sb, out ni) ||
                TryConsume(excelFmt, i, "yy",   "yy",   sb, out ni) ||
                TryConsume(excelFmt, i, "mmmm", "MMMM", sb, out ni) ||
                TryConsume(excelFmt, i, "mmm",  "MMM",  sb, out ni) ||
                TryConsumeMonthOrMinute(excelFmt, i, "mm", sb, out ni) ||
                TryConsumeMonthOrMinute(excelFmt, i, "m", sb, out ni) ||
                TryConsume(excelFmt, i, "dddd", "dddd", sb, out ni) ||
                TryConsume(excelFmt, i, "ddd",  "ddd",  sb, out ni) ||
                TryConsume(excelFmt, i, "dd",   "dd",   sb, out ni) ||
                TryConsume(excelFmt, i, "d",    "d",    sb, out ni) ||
                TryConsume(excelFmt, i, "hh",   hourToken2, sb, out ni) ||
                TryConsume(excelFmt, i, "h",    hourToken1, sb, out ni) ||
                TryConsume(excelFmt, i, "ss",   "ss",   sb, out ni) ||
                TryConsume(excelFmt, i, "s",    "s",    sb, out ni))
            {
                i = ni;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    private static bool TryConsumeFractionalSeconds(
        string src,
        int pos,
        System.Text.StringBuilder sb,
        out int newPos)
    {
        if (pos < 1 || src[pos] != '.' || !IsTimeToken(src[pos - 1]))
        {
            newPos = pos;
            return false;
        }

        int end = pos + 1;
        while (end < src.Length && src[end] == '0')
            end++;

        if (end == pos + 1)
        {
            newPos = pos;
            return false;
        }

        sb.Append('.');
        sb.Append('f', end - pos - 1);
        newPos = end;
        return true;
    }

    private static bool TryConsume(string src, int pos, string token, string replacement,
        System.Text.StringBuilder sb, out int newPos)
    {
        if (pos + token.Length <= src.Length &&
            string.Compare(src, pos, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0)
        {
            sb.Append(replacement);
            newPos = pos + token.Length;
            return true;
        }
        newPos = pos;
        return false;
    }

    private static bool TryConsumeMonthOrMinute(
        string src,
        int pos,
        string token,
        System.Text.StringBuilder sb,
        out int newPos)
    {
        if (pos + token.Length > src.Length ||
            string.Compare(src, pos, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) != 0)
        {
            newPos = pos;
            return false;
        }

        bool isMinute = IsAdjacentToTimeToken(src, pos, token.Length);
        sb.Append(isMinute
            ? token.Length == 2 ? "mm" : "m"
            : token.Length == 2 ? "MM" : "M");
        newPos = pos + token.Length;
        return true;
    }

    private static bool IsAdjacentToTimeToken(string format, int tokenStart, int tokenLength)
    {
        int before = PreviousFormatTokenIndex(format, tokenStart - 1);
        if (before >= 0 && IsTimeToken(format[before]))
            return true;

        int after = NextFormatTokenIndex(format, tokenStart + tokenLength);
        return after >= 0 && IsTimeToken(format[after]);
    }

    private static int PreviousFormatTokenIndex(string format, int index)
    {
        for (int i = index; i >= 0; i--)
        {
            char c = format[i];
            if (char.IsWhiteSpace(c) || c is ':' or '/' or '-' or ',')
                continue;
            if (c == '"')
            {
                int open = format.LastIndexOf('"', i - 1);
                if (open >= 0)
                {
                    i = open;
                    continue;
                }
            }
            if (c == ']')
            {
                int open = format.LastIndexOf('[', i - 1);
                if (open >= 0)
                {
                    i = open;
                    continue;
                }
            }
            return i;
        }

        return -1;
    }

    private static int NextFormatTokenIndex(string format, int index)
    {
        for (int i = index; i < format.Length; i++)
        {
            char c = format[i];
            if (char.IsWhiteSpace(c) || c is ':' or '/' or '-' or ',')
                continue;
            if (c == '"')
            {
                int close = format.IndexOf('"', i + 1);
                if (close >= 0)
                {
                    i = close;
                    continue;
                }
            }
            if (c == '[')
            {
                int close = format.IndexOf(']', i + 1);
                if (close >= 0)
                {
                    i = close;
                    continue;
                }
            }
            return i;
        }

        return -1;
    }

    private static bool IsTimeToken(char c)
        => c is 'h' or 'H' or 's' or 'S';

    // ── Elapsed time format [h]:mm:ss, [m]:ss, [s] ───────────────────────────

    private static string FormatElapsedTime(double value, string format, Match elapsedMatch)
    {
        // value is an OADate fraction; each unit = 1 day = 86400 seconds.
        var fractionalMatch = Regex.Match(format, @"(?:s|\[[sS]\])\.(0+)");
        int fractionalDotIndex = fractionalMatch.Success
            ? fractionalMatch.Index + fractionalMatch.Value.IndexOf('.', StringComparison.Ordinal)
            : -1;
        int fractionalPrecision = fractionalMatch.Success ? fractionalMatch.Groups[1].Value.Length : 0;

        double totalSecondsD = Math.Abs(value) * 86400.0;
        if (fractionalPrecision > 0)
            totalSecondsD = Math.Round(totalSecondsD, fractionalPrecision, MidpointRounding.AwayFromZero);

        long totalSeconds = (long)totalSecondsD;
        long totalMinutes = totalSeconds / 60;
        long totalHours   = totalSeconds / 3600;
        int remMinutes = (int)(totalMinutes % 60);
        int remSeconds = (int)(totalSeconds % 60);
        int fractionalSecondUnits = fractionalPrecision > 0
            ? (int)Math.Round((totalSecondsD - totalSeconds) * Math.Pow(10, fractionalPrecision),
                MidpointRounding.AwayFromZero)
            : 0;

        // Which bracket is the "lead" elapsed unit?
        long leadValue;
        string leadToken;
        if (elapsedMatch.Groups[1].Success)       // [h] or [H]
        {
            leadValue = totalHours;
            leadToken = elapsedMatch.Value;        // e.g. "[h]"
        }
        else if (elapsedMatch.Groups[2].Success)  // [m] or [M]
        {
            leadValue = totalMinutes;
            leadToken = elapsedMatch.Value;
            remMinutes = (int)(totalSeconds % 60);  // remSeconds stands; remMinutes not used here
        }
        else                                      // [s] or [S]
        {
            leadValue = totalSeconds;
            leadToken = elapsedMatch.Value;
        }

        // Build output: replace the lead bracket with its numeric value,
        // then fill in mm and ss with the remainder components.
        var sb = new System.Text.StringBuilder();
        if (value < 0) sb.Append('-');
        int i = 0;
        while (i < format.Length)
        {
            // Skip the bracket token we already handled
            if (string.Compare(format, i, leadToken, 0, leadToken.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                sb.Append(leadValue);
                i += leadToken.Length;
            }
            // Skip any other bracket content (locale, color, etc.)
            else if (format[i] == '[')
            {
                int close = format.IndexOf(']', i + 1);
                i = close >= 0 ? close + 1 : format.Length;
            }
            else if (i + 1 < format.Length &&
                     format[i] == 'm' && format[i + 1] == 'm' &&
                     elapsedMatch.Groups[1].Success) // mm after [h]
            {
                sb.Append(remMinutes.ToString("D2"));
                i += 2;
            }
            else if (i + 1 < format.Length && format[i] == 's' && format[i + 1] == 's')
            {
                sb.Append(remSeconds.ToString("D2"));
                i += 2;
            }
            else if (i == fractionalDotIndex)
            {
                sb.Append('.');
                sb.Append(fractionalSecondUnits.ToString("D" + fractionalPrecision.ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture));
                i += fractionalPrecision + 1;
            }
            else if (format[i] == '\\' && i + 1 < format.Length)
            {
                sb.Append(format[i + 1]);
                i += 2;
            }
            else
            {
                sb.Append(format[i++]);
            }
        }
        return sb.ToString();
    }

    // ── Text section ──────────────────────────────────────────────────────────

    private static FormatResult FormatTextWithColor(string text, string[] sections)
    {
        if (sections.Length <= 3 || string.IsNullOrEmpty(sections[3]))
            return new FormatResult(text);

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

