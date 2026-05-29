using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class TextToColumnsValueConverter
{
    private static readonly DatePartOrder DateOrderMDY = new(MonthIndex: 0, DayIndex: 1, YearIndex: 2);
    private static readonly DatePartOrder DateOrderDMY = new(MonthIndex: 1, DayIndex: 0, YearIndex: 2);
    private static readonly DatePartOrder DateOrderYMD = new(MonthIndex: 1, DayIndex: 2, YearIndex: 0);

    public static ScalarValue ConvertValue(
        string text,
        TextToColumnsColumnFormat columnFormat,
        TextToColumnsAdvancedOptions? advancedOptions) =>
        columnFormat switch
        {
            TextToColumnsColumnFormat.Text => new TextValue(text),
            TextToColumnsColumnFormat.DateMDY when TryParseDate(text, DateOrderMDY, out var date) => new DateTimeValue(date.ToOADate()),
            TextToColumnsColumnFormat.DateDMY when TryParseDate(text, DateOrderDMY, out var date) => new DateTimeValue(date.ToOADate()),
            TextToColumnsColumnFormat.DateYMD when TryParseDate(text, DateOrderYMD, out var date) => new DateTimeValue(date.ToOADate()),
            _ when TryParseNumber(text, advancedOptions, out var number) => new NumberValue(number),
            _ => new TextValue(text)
        };

    private static bool TryParseNumber(string text, TextToColumnsAdvancedOptions? advancedOptions, out double number)
    {
        if (advancedOptions is null)
            return double.TryParse(text, out number);

        var normalized = text.Trim();
        if (advancedOptions.TrailingMinusNumbers && normalized.EndsWith("-", StringComparison.Ordinal))
            normalized = "-" + normalized[..^1];

        if (!string.IsNullOrEmpty(advancedOptions.ThousandsSeparator))
            normalized = normalized.Replace(advancedOptions.ThousandsSeparator, string.Empty, StringComparison.Ordinal);

        if (!string.IsNullOrEmpty(advancedOptions.DecimalSeparator) && advancedOptions.DecimalSeparator != ".")
            normalized = normalized.Replace(advancedOptions.DecimalSeparator, ".", StringComparison.Ordinal);

        return double.TryParse(
            normalized,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out number);
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
