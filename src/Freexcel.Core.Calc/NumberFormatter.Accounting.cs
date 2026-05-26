namespace Freexcel.Core.Calc;

public static partial class NumberFormatter
{
    private static string ApplyAccountingTargetWidth(string text, string format, int? targetWidthCharacters)
    {
        if (targetWidthCharacters is not > 0 ||
            text.Length >= targetWidthCharacters.Value ||
            !HasAccountingLayoutDirective(format))
        {
            return text;
        }

        if (TryGetFillDirective(format, out var fillChar, out var fillAfterNumber))
        {
            var fill = new string(fillChar, targetWidthCharacters.Value - text.Length);
            if (fillAfterNumber)
                return text + fill;

            int directiveFillIndex = FindAccountingFillInsertionIndex(text);
            return directiveFillIndex < 0
                ? text.PadLeft(targetWidthCharacters.Value, fillChar)
                : text.Insert(directiveFillIndex, fill);
        }

        int fillIndex = FindAccountingFillInsertionIndex(text);
        if (fillIndex < 0)
        {
            var trailingSkipSpaces = CountTrailingSkipDirectives(format);
            return trailingSkipSpaces > 0
                ? text + new string(' ', trailingSkipSpaces)
                : text;
        }

        return text.Insert(fillIndex, new string(' ', targetWidthCharacters.Value - text.Length));
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

    private static bool TryGetFillDirective(string format, out char fillChar, out bool fillAfterNumber)
    {
        fillChar = ' ';
        fillAfterNumber = false;
        bool inQuote = false;
        int firstNumericPlaceholder = -1;

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

            if (!inQuote && firstNumericPlaceholder < 0 && IsNumericPlaceholder(c))
                firstNumericPlaceholder = i;

            if (!inQuote && c == '*' && i + 1 < format.Length)
            {
                fillChar = format[i + 1];
                fillAfterNumber = firstNumericPlaceholder >= 0;
                return true;
            }
        }

        return false;
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

            if (!inQuote && TryReadAccountingFillSymbol(format, i, out var symbol, out var fillIndex))
            {
                sb.Append(symbol);
                sb.Append(' ');
                i = fillIndex + 1;
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool IsCurrencySymbol(char c)
        => c is '$' or '\u00A3' or '\u20AC' or '\u00A5';

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
