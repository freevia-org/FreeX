using System.Globalization;
using System.Text.RegularExpressions;

namespace Freexcel.Core.Formula;

internal static class ExcelTextNumberParser
{
    private static readonly CultureInfo UsCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly Regex FakeLeapDayTextRegex = new(
        @"^(?:2/29/1900|02/29/1900|1900-02-29)(?:\s+(.+))?$",
        RegexOptions.IgnoreCase);
    private static readonly Regex MonthNameRegex = new(
        @"\b(?:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)",
        RegexOptions.IgnoreCase);
    private static readonly Regex AmPmRegex = new(
        @"\b(?:am|pm)\b",
        RegexOptions.IgnoreCase);

    public static bool TryParse(string text, out double number)
    {
        var trimmed = text.Trim();

        if (trimmed.EndsWith('%') &&
            double.TryParse(trimmed[..^1].Trim(), NumberStyles.Any, UsCulture, out var pct))
        {
            number = pct / 100.0;
            return true;
        }

        if (double.TryParse(trimmed, NumberStyles.Any, UsCulture, out number))
            return true;

        if (TryParseExcelFakeLeapDayText(trimmed, out number))
            return true;

        if (DateTime.TryParse(trimmed, UsCulture, DateTimeStyles.None, out var dt))
        {
            number = IsTimeOnlyText(trimmed)
                ? dt.TimeOfDay.TotalDays
                : ExcelDateSystem.DateToSerial(dt);
            return true;
        }

        number = 0;
        return false;
    }

    private static bool TryParseExcelFakeLeapDayText(string text, out double serial)
    {
        serial = 0;
        var match = FakeLeapDayTextRegex.Match(text);
        if (!match.Success) return false;

        serial = 60;
        if (match.Groups[1].Success)
        {
            if (!DateTime.TryParse(match.Groups[1].Value, UsCulture, DateTimeStyles.None, out var time))
                return false;
            serial += time.TimeOfDay.TotalDays;
        }

        return true;
    }

    private static bool IsTimeOnlyText(string text)
    {
        if (text.Contains('/') || text.Contains('-')) return false;
        if (MonthNameRegex.IsMatch(text))
            return false;

        return text.Contains(':')
            || AmPmRegex.IsMatch(text);
    }

}
