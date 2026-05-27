using System.Globalization;

namespace Freexcel.Core.Calc;

internal static class ExcelDateTimeFormatConverter
{
    public static string PrepareFormat(string excelFormat, DateTime dateTime, DateTimeFormatInfo dateTimeFormat)
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

    public static string ToNetDateFormat(string excelFmt)
    {
        bool hasAmPm =
            excelFmt.IndexOf("AM/PM", StringComparison.OrdinalIgnoreCase) >= 0 ||
            excelFmt.IndexOf("A/P", StringComparison.OrdinalIgnoreCase) >= 0;
        string hourToken2 = hasAmPm ? "hh" : "HH";
        string hourToken1 = hasAmPm ? "h" : "H";

        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < excelFmt.Length)
        {
            char c = excelFmt[i];
            if (c == '"')
            {
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

            if (TryConsumeFractionalSeconds(excelFmt, i, sb, out int ni) ||
                TryConsume(excelFmt, i, "AM/PM", "tt", sb, out ni) ||
                TryConsume(excelFmt, i, "am/pm", "tt", sb, out ni) ||
                TryConsume(excelFmt, i, "A/P", "t", sb, out ni) ||
                TryConsume(excelFmt, i, "a/p", "t", sb, out ni) ||
                TryConsume(excelFmt, i, "yyyy", "yyyy", sb, out ni) ||
                TryConsume(excelFmt, i, "yy", "yy", sb, out ni) ||
                TryConsume(excelFmt, i, "mmmm", "MMMM", sb, out ni) ||
                TryConsume(excelFmt, i, "mmm", "MMM", sb, out ni) ||
                TryConsumeMonthOrMinute(excelFmt, i, "mm", sb, out ni) ||
                TryConsumeMonthOrMinute(excelFmt, i, "m", sb, out ni) ||
                TryConsume(excelFmt, i, "dddd", "dddd", sb, out ni) ||
                TryConsume(excelFmt, i, "ddd", "ddd", sb, out ni) ||
                TryConsume(excelFmt, i, "dd", "dd", sb, out ni) ||
                TryConsume(excelFmt, i, "d", "d", sb, out ni) ||
                TryConsume(excelFmt, i, "hh", hourToken2, sb, out ni) ||
                TryConsume(excelFmt, i, "h", hourToken1, sb, out ni) ||
                TryConsume(excelFmt, i, "ss", "ss", sb, out ni) ||
                TryConsume(excelFmt, i, "s", "s", sb, out ni))
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
}
