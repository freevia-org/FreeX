using System.Globalization;
using System.Text.RegularExpressions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.TextToColumns;

public static class TextToColumnsValueConverter
{
    private static readonly DatePartOrder DateOrderMDY = new(MonthIndex: 0, DayIndex: 1, YearIndex: 2);
    private static readonly DatePartOrder DateOrderDMY = new(MonthIndex: 1, DayIndex: 0, YearIndex: 2);
    private static readonly DatePartOrder DateOrderYMD = new(MonthIndex: 1, DayIndex: 2, YearIndex: 0);
    private static readonly DatePartOrder DateOrderMYD = new(MonthIndex: 0, DayIndex: 2, YearIndex: 1);
    private static readonly DatePartOrder DateOrderDYM = new(MonthIndex: 2, DayIndex: 0, YearIndex: 1);
    private static readonly DatePartOrder DateOrderYDM = new(MonthIndex: 2, DayIndex: 1, YearIndex: 0);

    public static ScalarValue ConvertValue(
        string text,
        TextToColumnsColumnFormat columnFormat,
        TextToColumnsAdvancedOptions? advancedOptions = null) =>
        columnFormat switch
        {
            TextToColumnsColumnFormat.Text => new TextValue(text),
            TextToColumnsColumnFormat.DateMDY when TryParseDate(text, DateOrderMDY, out var date) => new DateTimeValue(date.ToOADate()),
            TextToColumnsColumnFormat.DateDMY when TryParseDate(text, DateOrderDMY, out var date) => new DateTimeValue(date.ToOADate()),
            TextToColumnsColumnFormat.DateYMD when TryParseDate(text, DateOrderYMD, out var date) => new DateTimeValue(date.ToOADate()),
            TextToColumnsColumnFormat.DateMYD when TryParseDate(text, DateOrderMYD, out var date) => new DateTimeValue(date.ToOADate()),
            TextToColumnsColumnFormat.DateDYM when TryParseDate(text, DateOrderDYM, out var date) => new DateTimeValue(date.ToOADate()),
            TextToColumnsColumnFormat.DateYDM when TryParseDate(text, DateOrderYDM, out var date) => new DateTimeValue(date.ToOADate()),
            // General (the wizard's default, unmodified-per-column format) must match Excel and
            // FreeX's own typed-cell-entry path (CellEntryParser.ParseScalarValue): a number-first,
            // then date-like-text, then boolean, then text fallback chain. Number is tried before
            // date so an ordinary/grouped numeric literal is never misread as a date.
            TextToColumnsColumnFormat.General when TryParseNumber(text, advancedOptions, out var generalNumber) => new NumberValue(generalNumber),
            TextToColumnsColumnFormat.General when ExcelDateEntryParser.TryParseCurrentCulture(
                text,
                allowTimeOnly: false,
                out var generalDate) => new DateTimeValue(generalDate.ToOADate()),
            TextToColumnsColumnFormat.General when IsBooleanText(text, out var generalBool) => new BoolValue(generalBool),
            _ when TryParseNumber(text, advancedOptions, out var number) => new NumberValue(number),
            _ when IsBooleanText(text, out var value) => new BoolValue(value),
            _ => new TextValue(text)
        };

    private static bool IsBooleanText(string text, out bool value)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (trimmed.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryParseNumber(string text, TextToColumnsAdvancedOptions? advancedOptions, out double number)
    {
        if (advancedOptions is null)
        {
            return TryParseFiniteNumberWithValidGrouping(text, CultureInfo.CurrentCulture, out number) ||
                TryParseFiniteNumberWithValidGrouping(text, CultureInfo.InvariantCulture, out number);
        }

        // Excel's Text Import Wizard forbids identical Decimal/Thousands separators outright, because
        // stripping the thousands separator first would also erase the decimal marker and silently
        // truncate the value (e.g. "1,234" with both set to "," would parse as 1234 instead of 1.234).
        // Treat that as an invalid configuration rather than risk 1000x data corruption.
        if (!string.IsNullOrEmpty(advancedOptions.DecimalSeparator) &&
            !string.IsNullOrEmpty(advancedOptions.ThousandsSeparator) &&
            string.Equals(advancedOptions.DecimalSeparator, advancedOptions.ThousandsSeparator, StringComparison.Ordinal))
        {
            number = default;
            return false;
        }

        var normalized = text.Trim();
        if (advancedOptions.TrailingMinusNumbers && normalized.EndsWith("-", StringComparison.Ordinal))
            normalized = "-" + normalized[..^1];

        if (!string.IsNullOrEmpty(advancedOptions.ThousandsSeparator))
            normalized = normalized.Replace(advancedOptions.ThousandsSeparator, string.Empty, StringComparison.Ordinal);

        if (!string.IsNullOrEmpty(advancedOptions.DecimalSeparator) && advancedOptions.DecimalSeparator != ".")
            normalized = normalized.Replace(advancedOptions.DecimalSeparator, ".", StringComparison.Ordinal);

        return TryParseFiniteNumber(
            normalized,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out number);
    }

    private static bool TryParseFiniteNumber(
        string text,
        NumberStyles styles,
        IFormatProvider formatProvider,
        out double number)
    {
        if (double.TryParse(text, styles, formatProvider, out number) &&
            double.IsFinite(number))
        {
            return true;
        }

        number = default;
        return false;
    }

    // Parses with the given culture's group/decimal separators, but only permits
    // NumberStyles.AllowThousands when the text's group-separator placement is a
    // structurally valid grouping (each group left of the decimal exactly 3 digits,
    // leading group 1-3 digits). NumberStyles.AllowThousands alone does not validate
    // grouping shape/position — it simply strips the group-separator character wherever
    // it appears, so e.g. "1234,56" under en-US (group separator ",") would otherwise
    // silently parse as 123456 instead of failing or being read as a decimal value.
    private static bool TryParseFiniteNumberWithValidGrouping(string text, CultureInfo culture, out double number)
    {
        var groupSeparator = culture.NumberFormat.NumberGroupSeparator;
        if (!string.IsNullOrEmpty(groupSeparator) && text.Contains(groupSeparator, StringComparison.Ordinal))
        {
            if (!HasValidGrouping(text, culture))
            {
                number = default;
                return false;
            }

            return TryParseFiniteNumber(text, NumberStyles.Float | NumberStyles.AllowThousands, culture, out number);
        }

        return TryParseFiniteNumber(text, NumberStyles.Float, culture, out number);
    }

    private static bool HasValidGrouping(string text, CultureInfo culture)
    {
        var groupSeparator = Regex.Escape(culture.NumberFormat.NumberGroupSeparator);
        var decimalSeparator = Regex.Escape(culture.NumberFormat.NumberDecimalSeparator);
        var pattern = $@"^[+-]?\d{{1,3}}({groupSeparator}\d{{3}})*({decimalSeparator}\d*)?[+-]?$";
        return Regex.IsMatch(text.Trim(), pattern);
    }

    private static bool TryParseDate(string text, DatePartOrder partOrder, out DateTime date)
    {
        date = default;
        var parts = text
            .Split(['/', '-', '.'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 ||
            !int.TryParse(parts[partOrder.MonthIndex], out var month) ||
            !int.TryParse(parts[partOrder.DayIndex], out var day) ||
            !int.TryParse(parts[partOrder.YearIndex], out var year))
        {
            return false;
        }

        if (year is >= 0 and < 100)
            year += year < 30 ? 2000 : 1900;

        try
        {
            date = new DateTime(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private readonly record struct DatePartOrder(int MonthIndex, int DayIndex, int YearIndex);
}
