namespace FreeX.Core.Formula;

public static partial class NumberFormatter
{
    private static string ApplyAccountingTargetWidth(string text, string format, int? targetWidthCharacters)
    {
        if (!HasAccountingLayoutDirective(format))
        {
            return text;
        }

        if (targetWidthCharacters is > 0)
        {
            return text.Length >= targetWidthCharacters.Value
                ? text
                : ApplyFillDirective(text, format, targetWidthCharacters.Value - text.Length);
        }

        // No column-width context (e.g. TEXT()/formula evaluation with no target cell).
        // Date/time formats intentionally strip these layout directives entirely with no
        // column to align to (see CustomNumberSubset_Cleans*SpacingFillAndEscapes tests).
        // For numeric formats, the accounting "<symbol>* " fill idiom is already rendered
        // as a single literal space upstream by PreserveAccountingFillSpace, and the
        // "[$symbol-locale]* " idiom likewise by PreserveLocaleCurrencyTokens, so nothing
        // further is needed for either case here. Any other underscore/asterisk fill
        // directive (a bare "_)" or "*-" with no accounting symbol) must still render at
        // least one character instead of being silently dropped.
        if (IsDateTimeFormat(format))
        {
            return text;
        }

        return HasAccountingFillDirective(format) || HasLocaleCurrencyFillDirective(format)
            ? text
            : ApplyFillDirective(text, format, fillWidth: 1);
    }

    // Detects the "[$symbol-locale]* " idiom (a bracketed locale-currency token
    // immediately followed by an asterisk fill and a space) -- PreserveLocaleCurrencyTokens
    // resolves this into a quoted symbol plus a literal space before PreserveAccountingFillSpace
    // ever runs, so by the time this method would otherwise re-detect a raw "*" here it is
    // already gone; check the original bracket-adjacent pattern directly instead.
    private static bool HasLocaleCurrencyFillDirective(string format)
    {
        bool inQuote = false;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (inQuote)
                continue;

            if (c == '[' && i + 2 < format.Length && format[i + 1] == '$')
            {
                int close = format.IndexOf(']', i + 2);
                if (close > i && close + 2 < format.Length &&
                    format[close + 1] == '*' && format[close + 2] == ' ')
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string ApplyFillDirective(string text, string format, int fillWidth)
    {
        if (TryGetFillDirective(format, out var fillChar, out var fillPlacement, out var fillDirectiveIndex))
        {
            // The "_x" skip directives around the number reserve the width of x (a space here):
            // Excel's cell text carries them ("_(* #,##0_)" -> " ... 5 "), so they are taken out
            // of the fill budget and re-added at the edges.
            var edgePadLeading = CountLeadingSkipDirectives(format);
            var edgePadTrailing = CountTrailingSkipDirectives(format);
            if (edgePadLeading + edgePadTrailing > 0 && fillWidth > edgePadLeading + edgePadTrailing)
            {
                fillWidth -= edgePadLeading + edgePadTrailing;
                return new string(' ', edgePadLeading) +
                       ApplyFillDirectiveCore(text, format, fillWidth, fillChar, fillPlacement, fillDirectiveIndex) +
                       new string(' ', edgePadTrailing);
            }

            return ApplyFillDirectiveCore(text, format, fillWidth, fillChar, fillPlacement, fillDirectiveIndex);
        }

        // No asterisk anywhere in the format (TryGetFillDirective above already handles
        // every genuine "<symbol>* " stretch idiom), so any remaining "_)"/"_x" here is a
        // fixed single-character reserve, not a stretch-to-width directive. Never insert a
        // gap mid-string (e.g. right after a "$" prefix) for this case -- only append the
        // reserved trailing space(s), matching the no-symbol-prefix "0_)" idiom.
        var trailingSkipSpaces = CountTrailingSkipDirectives(format);
        return trailingSkipSpaces > 0
            ? text + new string(' ', trailingSkipSpaces)
            : text;
    }

    private static int CountLeadingSkipDirectives(string format)
    {
        var count = 0;
        for (var i = 0; i + 1 < format.Length; i++)
        {
            if (format[i] == '_')
            {
                count++;
                i++;
                continue;
            }

            if (format[i] == '[')
            {
                var close = format.IndexOf(']', i + 1);
                if (close < 0)
                    break;
                i = close;
                continue;
            }

            break;
        }

        return count;
    }

    private static string ApplyFillDirectiveCore(
        string text,
        string format,
        int fillWidth,
        char fillChar,
        FillDirectivePlacement fillPlacement,
        int fillDirectiveIndex)
    {
        {
            var fill = new string(fillChar, fillWidth);
            if (fillPlacement == FillDirectivePlacement.AfterNumber)
            {
                int insertionIndex = FindFillAfterNumberInsertionIndex(text, format, fillDirectiveIndex);
                return text.Insert(insertionIndex, fill);
            }

            int directiveFillIndex = FindAccountingFillInsertionIndex(text);
            // A literal sign written before the asterisk ("-* #,##0.00") stays at the left edge and
            // the fill goes after it, not before it (verified against Excel).
            var signPrefix = ExtractRenderedLiteralPrefix(format, fillDirectiveIndex);
            if (signPrefix is "-" or "+")
            {
                var at = text.IndexOf(signPrefix, StringComparison.Ordinal);
                if (at >= 0 && text[..at].Trim().Length == 0)
                    directiveFillIndex = at + signPrefix.Length;
            }

            return directiveFillIndex < 0
                ? text.PadLeft(text.Length + fillWidth, fillChar)
                : text.Insert(directiveFillIndex, fill);
        }
    }

    private static int CountTrailingSkipDirectives(string format)
    {
        bool inQuote = false;
        var lastNumericPlaceholder = -1;
        var skipDirectivesAfterValue = 0;

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
                i++;
                continue;
            }

            if (!inQuote && IsNumericPlaceholder(c))
            {
                lastNumericPlaceholder = i;
                skipDirectivesAfterValue = 0;
                continue;
            }

            if (!inQuote && lastNumericPlaceholder >= 0 && c == '_' && i + 1 < format.Length)
            {
                skipDirectivesAfterValue++;
                i++;
            }
        }

        return skipDirectivesAfterValue;
    }

    private static bool HasAccountingLayoutDirective(string format)
    {
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
                i++;
                continue;
            }

            if (!inQuote && (c == '_' || c == '*'))
                return true;
        }

        return false;
    }

    private enum FillDirectivePlacement
    {
        BeforeNumber,
        AfterNumber
    }

    private static bool TryGetFillDirective(
        string format,
        out char fillChar,
        out FillDirectivePlacement placement,
        out int fillDirectiveIndex)
    {
        fillChar = ' ';
        placement = FillDirectivePlacement.BeforeNumber;
        fillDirectiveIndex = -1;
        bool inQuote = false;
        int firstValuePlaceholder = -1;

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
                i++;
                continue;
            }

            if (!inQuote && firstValuePlaceholder < 0 && IsValuePlaceholderAt(format, i))
                firstValuePlaceholder = i;

            if (!inQuote && c == '*' && i + 1 < format.Length)
            {
                fillChar = format[i + 1];
                fillDirectiveIndex = i;
                placement = firstValuePlaceholder >= 0
                    ? FillDirectivePlacement.AfterNumber
                    : FillDirectivePlacement.BeforeNumber;
                return true;
            }
        }

        return false;
    }

    private static bool IsValuePlaceholderAt(string format, int index)
    {
        var c = format[index];
        if (IsNumericPlaceholder(c))
            return true;

        if (c is 'y' or 'Y' or 'd' or 'D' or 'h' or 'H' or 'm' or 'M' or 's' or 'S')
            return true;

        if (c == '[' && index + 2 < format.Length)
        {
            var unit = format[index + 1];
            return unit is 'h' or 'H' or 'm' or 'M' or 's' or 'S' &&
                format.IndexOf(']', index + 2) > index;
        }

        return false;
    }

    private static int FindFillAfterNumberInsertionIndex(string text, string format, int fillDirectiveIndex)
    {
        var suffix = ExtractRenderedLiteralSuffix(format, fillDirectiveIndex + 2);
        return suffix.Length > 0 && text.EndsWith(suffix, StringComparison.Ordinal)
            ? text.Length - suffix.Length
            : text.Length;
    }

    private static string ExtractRenderedLiteralSuffix(string format, int start)
    {
        var sb = new System.Text.StringBuilder();
        bool inQuote = false;

        for (int i = Math.Max(0, start); i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && c == '\\' && i + 1 < format.Length)
            {
                sb.Append(format[++i]);
                continue;
            }

            if (!inQuote && IsValuePlaceholderAt(format, i))
                return "";

            if (!inQuote && c == '_' && i + 1 < format.Length)
            {
                i++;
                continue;
            }

            if (!inQuote && c == '*' && i + 1 < format.Length)
            {
                i++;
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string ExtractRenderedLiteralPrefix(string format, int endExclusive)
    {
        var sb = new System.Text.StringBuilder();
        bool inQuote = false;
        for (int i = 0; i < endExclusive && i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && c == '\\' && i + 1 < format.Length)
            {
                sb.Append(format[++i]);
                continue;
            }

            if (!inQuote && c == '_' && i + 1 < format.Length)
            {
                i++;
                continue;
            }

            if (!inQuote && (IsValuePlaceholderAt(format, i) || c == '*'))
                return "";

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static int FindAccountingFillInsertionIndex(string text)
    {
        int firstValueChar = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsDigit(text[i]) || text[i] is '(' or '-' or '+')
            {
                firstValueChar = i;
                break;
            }
        }

        if (firstValueChar <= 0)
            return -1;

        int existingGap = text.LastIndexOf(' ', firstValueChar - 1, firstValueChar);
        return existingGap >= 0 ? existingGap + 1 : firstValueChar;
    }

    private static string RemoveSpacingAndFillDirectives(string format)
    {
        if (!HasSpacingOrFillDirective(format))
            return format;

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

    private static bool HasSpacingOrFillDirective(string format)
    {
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
                i++;
                continue;
            }

            if (!inQuote && c is '_' or '*')
                return true;
        }

        return false;
    }

    private static string PreserveAccountingFillSpace(string format)
    {
        if (!HasAccountingFillDirective(format))
            return format;

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

            if (!inQuote && TryReadAccountingFillSymbol(format, i, out var symbol, out var fillIndex))
            {
                sb.Append('"');
                sb.Append(symbol.Replace("\"", "\"\"", StringComparison.Ordinal));
                sb.Append(' ');
                sb.Append('"');
                i = fillIndex + 1;
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool HasAccountingFillDirective(string format)
    {
        bool inQuote = false;

        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && TryReadAccountingFillSymbol(format, i, out _, out _))
                return true;
        }

        return false;
    }

    private static bool IsCurrencySymbol(char c)
        => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.CurrencySymbol;

    private static bool TryReadAccountingFillSymbol(
        string format,
        int start,
        out string symbol,
        out int fillIndex)
    {
        symbol = "";
        fillIndex = -1;
        if (!IsAccountingSymbolChar(format[start]))
            return false;

        int cursor = start;
        while (cursor < format.Length && IsAccountingSymbolChar(format[cursor]))
            cursor++;

        if (cursor == start ||
            cursor + 1 >= format.Length ||
            format[cursor] != '*' ||
            format[cursor + 1] != ' ')
        {
            return false;
        }

        symbol = format[start..cursor];
        fillIndex = cursor;
        return true;
    }

    private static bool IsAccountingSymbolChar(char c)
        => char.IsLetter(c) || IsCurrencySymbol(c);

    private static bool IsAccountingDashPlaceholder(string prefix, string suffix) =>
        suffix.Length == 0 && prefix.TrimEnd().EndsWith("-", StringComparison.Ordinal);

    private static bool HasVisibleAffix(string prefix, string suffix) =>
        !string.IsNullOrWhiteSpace(prefix) || !string.IsNullOrWhiteSpace(suffix);

    private static bool HasAccountingZeroDashPlaceholder(string format) =>
        HasAccountingLayoutDirective(format) &&
        HasActiveQuestionPlaceholder(format) &&
        format.Contains("\"-\"", StringComparison.Ordinal);

    private static string RenderQuestionOnlyAlignment(string format)
    {
        var result = new System.Text.StringBuilder(format.Length);
        foreach (var c in format)
            result.Append(c == '?' ? ' ' : c);
        return result.ToString();
    }
}
