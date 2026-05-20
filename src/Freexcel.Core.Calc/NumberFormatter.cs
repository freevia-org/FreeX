using System.Globalization;
using System.Text.RegularExpressions;
using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

public static class NumberFormatter
{
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
            DateTimeValue d => new FormatResult(FormatDateTime(d.Value, sections[0])),
            TextValue t     => new FormatResult(sections.Length > 3 && !string.IsNullOrEmpty(sections[3])
                                   ? ApplyTextSection(sections[3], t.Value) : t.Value),
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
            if (TryMapColor(token, out var tokenColor))
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

    // Extract optional [Color] or [ColorN] prefix; return (hexColor, remainingFormat)
    private static (string? Color, string Format) ExtractColor(string section)
    {
        var m = Regex.Match(section, @"^\[([A-Za-z]+|Color\d+)\]", RegexOptions.IgnoreCase);
        if (!m.Success) return (null, section);

        TryMapColor(m.Groups[1].Value, out var hex);
        return (hex, section[m.Length..]);
    }

    private static bool TryMapColor(string token, out string? color)
    {
        color = token.ToUpperInvariant() switch
        {
            "BLACK"   => "#000000",
            "WHITE"   => "#FFFFFF",
            "RED"     => "#FF0000",
            "GREEN"   => "#00B050",
            "BLUE"    => "#0070C0",
            "YELLOW"  => "#FFFF00",
            "CYAN"    => "#00FFFF",
            "MAGENTA" => "#FF00FF",
            _         => null
        };
        return color is not null;
    }

    private static string ApplyNumericFormat(double value, string format)
    {
        if (string.IsNullOrEmpty(format) || format == "General")
            return FormatNumberGeneral(value);

        // Elapsed-time brackets: [h], [m], [s] represent total elapsed hours/minutes/seconds
        // and must be handled before the generic bracket-stripping pass.
        var elapsedMatch = Regex.Match(format, @"\[([hH])\]|\[([mM])\]|\[([sS])\]");
        if (elapsedMatch.Success)
        {
            return FormatElapsedTime(value, format, elapsedMatch);
        }

        // Remove any remaining bracket content (conditions, locale, etc.)
        format = Regex.Replace(format, @"\[[^\]]*\]", "");
        format = PreserveAccountingFillSpace(format);
        format = RemoveSpacingAndFillDirectives(format);
        (format, value) = ApplyTrailingCommaScaling(format, value);

        // Percentage: multiply value by 100 before formatting
        if (format.Contains('%'))
        {
            double pctValue = value * 100;
            string pctFmt = format; // keep the % so .NET formats it as percentage
            // .NET percentage format (P) multiplies by 100 and adds %; but format string
            // containing literal '%' means we multiply ourselves and use 'F' style.
            // Build a numeric format without the % and append it manually.
            string numFmt = format.Replace("%", "").Trim();
            try
            {
                return pctValue.ToString(string.IsNullOrEmpty(numFmt) ? "0" : numFmt,
                    CultureInfo.InvariantCulture) + "%";
            }
            catch { return pctValue.ToString("0", CultureInfo.InvariantCulture) + "%"; }
        }

        // Date / time format
        if (IsDateTimeFormat(format))
        {
            try
            {
                var dt = DateTime.FromOADate(value);
                return dt.ToString(ToNetDateFormat(format), CultureInfo.InvariantCulture);
            }
            catch { return value.ToString(CultureInfo.InvariantCulture); }
        }

        if (IsSimpleFractionFormat(format))
            return FormatSimpleFraction(value, format);

        if (IsScientificFormat(format))
            return FormatScientific(value, format);

        // Accounting / text literals — strip quoted strings to expose the numeric pattern
        var stripped = Regex.Replace(format, "\"[^\"]*\"", "");
        var prefix = "";
        var suffix = "";

        // Extract prefix/suffix literal text (not part of numeric pattern)
        if (format != stripped)
        {
            // Rebuild: replace each quoted segment with nothing for the format pattern,
            // then reassemble with literal text around the number.
            var parts = Regex.Split(format, "(\"[^\"]*\")");
            var numPartSb = new System.Text.StringBuilder();
            var prefixSb  = new System.Text.StringBuilder();
            var suffixSb  = new System.Text.StringBuilder();
            bool numStarted = false;
            bool numEnded   = false;

            foreach (var p in parts)
            {
                if (p.StartsWith('"') && p.EndsWith('"'))
                {
                    var lit = p[1..^1];
                    if (numStarted) { suffixSb.Append(lit); numEnded = true; }
                    else prefixSb.Append(lit);
                }
                else if (!string.IsNullOrEmpty(p))
                {
                    if (!numEnded) numPartSb.Append(p);
                    numStarted = true;
                }
            }
            prefix = prefixSb.ToString();
            suffix = suffixSb.ToString();
            format = numPartSb.ToString();

            if (string.IsNullOrEmpty(format))
                return prefix + suffix;
        }

        // Pass the cleaned format to .NET — it understands #,##0.00, 0.00, 0, # etc.
        string numStr;
        try   { numStr = value.ToString(format, CultureInfo.InvariantCulture); }
        catch { numStr = value.ToString(CultureInfo.InvariantCulture); }

        return prefix + numStr + suffix;
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
               stripped.Contains("??/??", StringComparison.Ordinal);
    }

    private static string FormatSimpleFraction(double value, string format)
    {
        var (prefix, numericFormat, suffix) = ExtractNumericAffixes(format);
        var stripped = Regex.Replace(numericFormat, "\"[^\"]*\"", "");
        var denominatorPattern = Regex.Match(stripped, @"/(\?+)");
        int maxDenominator = denominatorPattern.Success && denominatorPattern.Groups[1].Value.Length >= 2 ? 99 : 9;

        double absValue = Math.Abs(value);
        int whole = stripped.Contains('#') || stripped.Contains('0') ? (int)Math.Floor(absValue) : 0;
        double fractional = absValue - whole;

        var (numerator, denominator) = ApproximateFraction(fractional, maxDenominator);
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

    private static string FormatScientific(double value, string format)
    {
        var (prefix, numericFormat, suffix) = ExtractNumericAffixes(format);
        var stripped = Regex.Replace(numericFormat, "\"[^\"]*\"", "");
        var mantissa = stripped.Split(['E', 'e'], 2)[0];
        int decimals = mantissa.Contains('.')
            ? mantissa[(mantissa.IndexOf('.') + 1)..].Count(c => c == '0' || c == '#')
            : 0;

        string result = value.ToString("E" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
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
        string unquoted = Regex.Replace(format, "\"([^\"]*)\"", "$1");
        int start = -1;
        int end = -1;

        for (int i = 0; i < unquoted.Length; i++)
        {
            if (IsNumericPlaceholder(unquoted[i]))
            {
                start = i;
                break;
            }
        }

        for (int i = unquoted.Length - 1; i >= 0; i--)
        {
            if (IsNumericPlaceholder(unquoted[i]))
            {
                end = i;
                break;
            }
        }

        if (start < 0 || end < start)
            return (unquoted, "", "");

        return (unquoted[..start], unquoted[start..(end + 1)], unquoted[(end + 1)..]);
    }

    private static bool IsNumericPlaceholder(char c)
        => c is '0' or '#' or '?';

    // ── Date/time formatting ──────────────────────────────────────────────────

    private static string FormatDateTime(double oaDate, string format)
    {
        var (_, cleanFmt) = ExtractColor(format);
        cleanFmt = Regex.Replace(cleanFmt, @"\[[^\]]*\]", "");
        try
        {
            var dt = DateTime.FromOADate(oaDate);
            if (IsDateTimeFormat(cleanFmt))
                return dt.ToString(ToNetDateFormat(cleanFmt), CultureInfo.InvariantCulture);
            return dt.ToString(cleanFmt, CultureInfo.InvariantCulture);
        }
        catch { return oaDate.ToString(CultureInfo.InvariantCulture); }
    }

    // Detect date/time format: has date/time tokens and no digit-only tokens
    private static bool IsDateTimeFormat(string format)
    {
        // Strip quoted strings before checking
        var stripped = Regex.Replace(format, "\"[^\"]*\"", "");
        bool hasDateToken = stripped.IndexOfAny(['y', 'Y', 'd', 'D', 'h', 'H', 's', 'S', 'm', 'M']) >= 0;
        bool hasNumberToken = stripped.IndexOfAny(['0', '#']) >= 0;
        return hasDateToken && !hasNumberToken;
    }

    // Map Excel date format tokens to .NET format string equivalents.
    private static string ToNetDateFormat(string excelFmt)
    {
        // When AM/PM is present the hour token uses 12-hour lowercase (h/hh);
        // without AM/PM use 24-hour uppercase (H/HH).
        bool hasAmPm = excelFmt.IndexOf("AM/PM", StringComparison.OrdinalIgnoreCase) >= 0;
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
            // Longest-match for each token group
            if (TryConsume(excelFmt, i, "AM/PM", "tt", sb, out int ni) ||
                TryConsume(excelFmt, i, "am/pm", "tt", sb, out ni) ||
                TryConsume(excelFmt, i, "yyyy", "yyyy", sb, out ni) ||
                TryConsume(excelFmt, i, "yy",   "yy",   sb, out ni) ||
                TryConsume(excelFmt, i, "mmmm", "MMMM", sb, out ni) ||
                TryConsume(excelFmt, i, "mmm",  "MMM",  sb, out ni) ||
                TryConsume(excelFmt, i, "mm",   "MM",   sb, out ni) ||
                TryConsume(excelFmt, i, "m",    "M",    sb, out ni) ||
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

    // ── Elapsed time format [h]:mm:ss, [m]:ss, [s] ───────────────────────────

    private static string FormatElapsedTime(double value, string format, Match elapsedMatch)
    {
        // value is an OADate fraction; each unit = 1 day = 86400 seconds.
        double totalSecondsD = Math.Abs(value) * 86400.0;
        long totalSeconds = (long)totalSecondsD;
        long totalMinutes = totalSeconds / 60;
        long totalHours   = totalSeconds / 3600;
        int remMinutes = (int)(totalMinutes % 60);
        int remSeconds = (int)(totalSeconds % 60);

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
            else
            {
                sb.Append(format[i++]);
            }
        }
        return sb.ToString();
    }

    // ── Text section ──────────────────────────────────────────────────────────

    private static string ApplyTextSection(string section, string text)
    {
        // `@` is the text placeholder; surrounding quotes are literals
        var result = new System.Text.StringBuilder();
        bool inQuote = false;
        foreach (char c in section)
        {
            if (c == '"') { inQuote = !inQuote; continue; }
            if (inQuote) { result.Append(c); continue; }
            if (c == '@') result.Append(text);
            else result.Append(c);
        }
        return result.ToString();
    }
}
