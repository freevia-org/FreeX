using System.Globalization;

namespace Freexcel.Core.Calc;

public static partial class NumberFormatter
{
    private static bool HasActiveQuestionPlaceholder(string format)
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

            if (!inQuote && c == '?')
                return true;
        }

        return false;
    }

    private static bool ShouldRenderQuestionOnlyFormat(string prefix, string suffix) =>
        string.IsNullOrWhiteSpace(prefix) && string.IsNullOrWhiteSpace(suffix);

    private static string FormatQuestionPlaceholderNumber(
        double value,
        string format,
        NumberFormatInfo numberFormat)
    {
        var zeroFormat = ReplaceActiveQuestionPlaceholders(format, '0');
        var hashFormat = ReplaceActiveQuestionPlaceholders(format, '#');
        string zeroText;
        string hashText;
        try
        {
            zeroText = value.ToString(zeroFormat, numberFormat);
            hashText = value.ToString(hashFormat, numberFormat);
        }
        catch
        {
            return value.ToString(numberFormat);
        }

        var decimalSeparator = numberFormat.NumberDecimalSeparator;
        var zeroParts = SplitFormattedNumber(zeroText, decimalSeparator);
        var hashParts = SplitFormattedNumber(hashText, decimalSeparator);
        var formatParts = SplitFormatNumber(format);

        var integer = ApplyQuestionIntegerSpacing(zeroParts.Integer, hashParts.Integer, formatParts.Integer);
        if (!formatParts.HasDecimal)
            return integer;

        var decimals = ApplyQuestionDecimalSpacing(zeroParts.Decimal, hashParts.Decimal, formatParts.Decimal);
        return integer + decimalSeparator + decimals;
    }

    private static string ReplaceActiveQuestionPlaceholders(string format, char replacement)
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

            if (!inQuote && c == '\\' && i + 1 < format.Length)
            {
                result.Append(c);
                result.Append(format[++i]);
                continue;
            }

            result.Append(!inQuote && c == '?' ? replacement : c);
        }

        return result.ToString();
    }

    private static (string Integer, string Decimal) SplitFormattedNumber(string text, string decimalSeparator)
    {
        var index = text.IndexOf(decimalSeparator, StringComparison.Ordinal);
        return index < 0
            ? (text, "")
            : (text[..index], text[(index + decimalSeparator.Length)..]);
    }

    private static (string Integer, string Decimal, bool HasDecimal) SplitFormatNumber(string format)
    {
        var index = format.IndexOf('.');
        return index < 0
            ? (format, "", false)
            : (format[..index], format[(index + 1)..], true);
    }

    private static string ApplyQuestionIntegerSpacing(string zeroInteger, string hashInteger, string integerFormat)
    {
        if (zeroInteger.Length <= hashInteger.Length)
            return zeroInteger;

        var chars = zeroInteger.ToCharArray();
        var missingCount = zeroInteger.Length - hashInteger.Length;
        var formatIndex = integerFormat.Length - 1;
        for (var textIndex = zeroInteger.Length - 1; textIndex >= 0; textIndex--)
        {
            var formatChar = NextIntegerFormatChar(integerFormat, ref formatIndex);
            if (formatChar is null)
                break;

            if (textIndex < missingCount && formatChar == '?' && chars[textIndex] == '0')
                chars[textIndex] = ' ';
        }

        return new string(chars);
    }

    private static char? NextIntegerFormatChar(string integerFormat, ref int index)
    {
        while (index >= 0)
        {
            var c = integerFormat[index--];
            if (c is ',' or '\\')
                continue;

            return c;
        }

        return null;
    }

    private static string ApplyQuestionDecimalSpacing(string zeroDecimal, string hashDecimal, string decimalFormat)
    {
        if (zeroDecimal.Length == 0)
            return zeroDecimal;

        var chars = zeroDecimal.ToCharArray();
        var formatIndex = 0;
        for (var textIndex = 0; textIndex < chars.Length && formatIndex < decimalFormat.Length; textIndex++)
        {
            var formatChar = decimalFormat[formatIndex++];
            if (formatChar == '\\' && formatIndex < decimalFormat.Length)
                formatChar = decimalFormat[formatIndex++];

            if (formatChar == '?' && textIndex >= hashDecimal.Length && chars[textIndex] == '0')
                chars[textIndex] = ' ';
        }

        return new string(chars);
    }
}
