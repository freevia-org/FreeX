using System.Globalization;

namespace Free.Shared.IO;

/// <summary>
/// Rejects malformed thousands-group shapes that the numeric parsers otherwise accept.
/// Numeric parsing, culture fallback, and finite-value policy remain the caller's responsibility.
/// </summary>
public static class NumericTextGroupingValidator
{
    public static bool HasValidGroupingShape(
        ReadOnlySpan<char> field,
        IFormatProvider formatProvider)
    {
        var numberFormat = NumberFormatInfo.GetInstance(formatProvider);
        if (!TryGetGroupedIntegerPart(field, numberFormat, out var integerPart, out var groupSeparator))
            return true;

        if (integerPart.Length > 0 && (integerPart[0] == '+' || integerPart[0] == '-'))
            integerPart = integerPart[1..];

        return HasValidPlainGroupedIntegerShape(integerPart, groupSeparator);
    }

    public static bool HasValidGroupingShape(
        ReadOnlySpan<char> field,
        NumberStyles styles,
        IFormatProvider formatProvider)
    {
        if ((styles & NumberStyles.AllowThousands) == 0)
            return true;

        var numberFormat = NumberFormatInfo.GetInstance(formatProvider);
        if (!TryGetGroupedIntegerPart(field, numberFormat, out var integerPart, out var groupSeparator))
            return true;

        integerPart = integerPart.Trim();
        if (integerPart.Length >= 2 && integerPart[0] == '(' && integerPart[^1] == ')')
            integerPart = integerPart[1..^1].Trim();
        if (integerPart.Length > 0 && (integerPart[0] == '+' || integerPart[0] == '-'))
            integerPart = integerPart[1..];

        if ((styles & NumberStyles.AllowCurrencySymbol) != 0)
        {
            var currencySymbol = numberFormat.CurrencySymbol;
            if (!string.IsNullOrEmpty(currencySymbol))
            {
                var symbolIndex = integerPart.IndexOf(currencySymbol, StringComparison.Ordinal);
                if (symbolIndex >= 0 && integerPart[..symbolIndex].Trim().Length == 0)
                    integerPart = integerPart[(symbolIndex + currencySymbol.Length)..].TrimStart();
            }
        }

        return HasValidPlainGroupedIntegerShape(integerPart, groupSeparator);
    }

    private static bool TryGetGroupedIntegerPart(
        ReadOnlySpan<char> field,
        NumberFormatInfo numberFormat,
        out ReadOnlySpan<char> integerPart,
        out string groupSeparator)
    {
        groupSeparator = numberFormat.NumberGroupSeparator;
        if (string.IsNullOrEmpty(groupSeparator) ||
            field.IndexOf(groupSeparator, StringComparison.Ordinal) < 0)
        {
            integerPart = default;
            return false;
        }

        var decimalSeparator = numberFormat.NumberDecimalSeparator;
        var decimalIndex = string.IsNullOrEmpty(decimalSeparator)
            ? -1
            : field.IndexOf(decimalSeparator, StringComparison.Ordinal);
        integerPart = decimalIndex >= 0 ? field[..decimalIndex] : field;
        return true;
    }

    private static bool HasValidPlainGroupedIntegerShape(
        ReadOnlySpan<char> integerPart,
        string groupSeparator)
    {
        var isFirstGroup = true;
        var currentGroupDigits = 0;
        var index = 0;
        while (index < integerPart.Length)
        {
            if (integerPart[index..].StartsWith(groupSeparator, StringComparison.Ordinal))
            {
                if (isFirstGroup ? currentGroupDigits is < 1 or > 3 : currentGroupDigits != 3)
                    return false;

                isFirstGroup = false;
                currentGroupDigits = 0;
                index += groupSeparator.Length;
                continue;
            }

            if (!char.IsDigit(integerPart[index]))
                return true;

            currentGroupDigits++;
            index++;
        }

        return isFirstGroup ? currentGroupDigits is >= 1 and <= 3 : currentGroupDigits == 3;
    }
}
