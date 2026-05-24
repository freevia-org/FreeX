using System.Globalization;
using System.Text.RegularExpressions;

using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // Date and time functions plus shared Excel date-system helpers.

    private static ScalarValue Date(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double rawMonth = ToNumber(args[1]);
        double rawDay = ToNumber(args[2]);
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => DateScalar(value, rawMonth, rawDay));
        return DateScalar(args[0], rawMonth, rawDay);
    }

    private static ScalarValue DateScalar(ScalarValue yearValue, double rawMonth, double rawDay)
    {
        double rawYear = ToNumber(yearValue);
        if (!double.IsFinite(rawYear) || !double.IsFinite(rawMonth) || !double.IsFinite(rawDay))
            return ErrorValue.Num;
        if (rawYear > int.MaxValue || rawMonth > int.MaxValue || rawDay > int.MaxValue ||
            rawYear < int.MinValue || rawMonth < int.MinValue || rawDay < int.MinValue)
            return ErrorValue.Num;
        int year  = (int)rawYear;
        int month = (int)rawMonth;
        int day   = (int)rawDay;
        if (year >= 0 && year < 1900)
            year += 1900;
        if (year < 0 || year > 9999) return ErrorValue.Num;
        if (year == 1900 && month < 1) return ErrorValue.Num;
        try
        {
            var dt = new DateTime(year, 1, 1)
                .AddMonths(month - 1)
                .AddDays(day - 1);
            double serial = DateToSerial(dt);
            if (serial < 0) return ErrorValue.Num;
            if (year == 1900 && month >= 3 && dt < new DateTime(1900, 3, 1))
                return new NumberValue(serial + 1);
            if (year == 1900 && month == 3 && day == 0)
                return new NumberValue(60);
            if (dt == new DateTime(1900, 3, 1) && month < 3)
                return new NumberValue(60);
            return new NumberValue(serial);
        }
        catch { return ErrorValue.Num; }
    }

    // OADate range supported by DateTime.FromOADate: -657435.0 to 2958465.0
    private static bool TryOADateToDateTime(ScalarValue v, out DateTime dt)
    {
        dt = default;
        var num = ToNumber(v);
        if (!double.IsFinite(num) || num < 0 || num > 2958465.0)
            return false;
        dt = SerialToDate(num);
        return true;
    }

    private static bool TryNonNegativeOADateToDateTime(ScalarValue v, out DateTime dt)
    {
        dt = default;
        var num = ToNumber(v);
        if (!double.IsFinite(num) || num < 0 || num > 2958465.0)
            return false;
        dt = SerialToDate(num);
        return true;
    }

    private static bool TryNonNegativeSerialToTimeParts(ScalarValue v, out int hour, out int minute, out int second)
    {
        hour = minute = second = 0;
        var num = ToNumber(v);
        if (!double.IsFinite(num) || num < 0 || num > 2958465.0)
            return false;

        var fraction = num - Math.Floor(num);
        var totalSeconds = (int)Math.Floor(fraction * 86400.0 + 1e-9) % 86400;
        hour = totalSeconds / 3600;
        minute = totalSeconds % 3600 / 60;
        second = totalSeconds % 60;
        return true;
    }

    private static ScalarValue Year(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, YearScalar);
        return YearScalar(args[0]);
    }

    private static ScalarValue YearScalar(ScalarValue value) =>
        IsExcelFakeLeapDay(value) || IsExcelZeroDate(value)
            ? new NumberValue(1900)
            : TryOADateToDateTime(value, out var dt) ? new NumberValue(dt.Year) : ErrorValue.Num;

    private static ScalarValue Month(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, MonthScalar);
        return MonthScalar(args[0]);
    }

    private static ScalarValue MonthScalar(ScalarValue value) =>
        IsExcelFakeLeapDay(value) ? new NumberValue(2)
        : IsExcelZeroDate(value) ? new NumberValue(1)
        : TryOADateToDateTime(value, out var dt) ? new NumberValue(dt.Month) : ErrorValue.Num;

    private static ScalarValue Day(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, DayScalar);
        return DayScalar(args[0]);
    }

    private static ScalarValue DayScalar(ScalarValue value) =>
        IsExcelFakeLeapDay(value) ? new NumberValue(29)
        : IsExcelZeroDate(value) ? new NumberValue(0)
        : TryOADateToDateTime(value, out var dt) ? new NumberValue(dt.Day) : ErrorValue.Num;

    private static ScalarValue Hour(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, HourScalar);
        return HourScalar(args[0]);
    }

    private static ScalarValue HourScalar(ScalarValue value) =>
        TryNonNegativeSerialToTimeParts(value, out var hour, out _, out _) ? new NumberValue(hour) : ErrorValue.Num;

    private static ScalarValue Minute(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, MinuteScalar);
        return MinuteScalar(args[0]);
    }

    private static ScalarValue MinuteScalar(ScalarValue value) =>
        TryNonNegativeSerialToTimeParts(value, out _, out var minute, out _) ? new NumberValue(minute) : ErrorValue.Num;

    private static ScalarValue Second(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, SecondScalar);
        return SecondScalar(args[0]);
    }

    private static ScalarValue SecondScalar(ScalarValue value) =>
        TryNonNegativeSerialToTimeParts(value, out _, out _, out var second) ? new NumberValue(second) : ErrorValue.Num;

    private static ScalarValue Weekday(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args.Count > 1 && args[1] is ErrorValue returnTypeError) return returnTypeError;
        if (args.Count > 1 && args[1] is RangeValue returnTypeRange)
            return MapUnaryTextRange(returnTypeRange, value =>
            {
                double rawType = ToNumber(value);
                return double.IsFinite(rawType) ? WeekdayScalar(args[0], (int)rawType) : ErrorValue.Num;
            });
        double rawReturnType = args.Count > 1 && args[1] is not BlankValue ? ToNumber(args[1]) : 1;
        if (!double.IsFinite(rawReturnType)) return ErrorValue.Num;
        int returnType = (int)rawReturnType;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => WeekdayScalar(value, returnType));
        return WeekdayScalar(args[0], returnType);
    }

    private static ScalarValue WeekdayScalar(ScalarValue value, int returnType)
    {
        double rawSerial = ToNumber(value);
        if (!double.IsFinite(rawSerial) || rawSerial < 0 || rawSerial >= 2958466.0) return ErrorValue.Num;
        int daySerial = (int)Math.Floor(rawSerial);
        int dow = ((daySerial - 1) % 7 + 7) % 7; // 0=Sunday...6=Saturday in Excel's 1900 date system
        return returnType switch
        {
            1 => new NumberValue(dow + 1),                     // Sun=1..Sat=7
            2 or 11 => new NumberValue(dow == 0 ? 7 : dow),    // Mon=1..Sun=7
            3 => new NumberValue(dow == 0 ? 6 : dow - 1),      // Mon=0..Sun=6
            >= 12 and <= 17 => new NumberValue(((dow - (returnType - 10) + 7) % 7) + 1),
            _ => ErrorValue.Num
        };
    }

    private static ScalarValue Edate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is RangeValue monthsRange)
            return MapUnaryTextRange(monthsRange, value =>
            {
                double raw = ToNumber(value);
                if (!double.IsFinite(raw) || raw > int.MaxValue || raw < int.MinValue) return ErrorValue.Num;
                return EdateScalar(args[0], (int)raw);
            });
        double rawMonths = ToNumber(args[1]);
        if (!double.IsFinite(rawMonths) || rawMonths > int.MaxValue || rawMonths < int.MinValue) return ErrorValue.Num;
        int months = (int)rawMonths;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => EdateScalar(value, months));
        return EdateScalar(args[0], months);
    }

    private static ScalarValue EdateScalar(ScalarValue value, int months)
    {
        if (!TryOADateToDateTime(value, out var dt)) return ErrorValue.Num;
        try
        {
            var result = dt.AddMonths(months);
            return new NumberValue(DateToSerial(result));
        }
        catch { return ErrorValue.Num; }
    }

    private static ScalarValue Datedif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (!TryOADateToDateTime(args[0], out var startRaw)) return ErrorValue.Num;
        if (!TryOADateToDateTime(args[1], out var endRaw)) return ErrorValue.Num;
        // DATEDIF operates on whole dates — discard any time portion so that
        // e.g. DATEDIF(2024-01-01 23:00, 2024-01-02 01:00, "D") returns 1 (Excel)
        // rather than 0 (TimeSpan.Days would otherwise round toward zero).
        var start = startRaw.Date;
        var end = endRaw.Date;
        if (end < start) return ErrorValue.Num;
        var unit  = ToText(args[2]).ToUpperInvariant();

        return unit switch
        {
            "D"  => new NumberValue(DateToSerial(end) - DateToSerial(start)),
            "M"  => new NumberValue(MonthDiff(start, end)),
            "Y"  => new NumberValue(YearDiff(start, end)),
            "YM" => new NumberValue((int)MonthDiff(start, end) % 12),
            "YD" => DateDifYD(start, end),
            "MD" => DateDifMD(start, end),
            _    => ErrorValue.Value
        };
    }

    private static double MonthDiff(DateTime start, DateTime end)
    {
        int months = (end.Year - start.Year) * 12 + (end.Month - start.Month);
        if (end.Day < start.Day) months--;
        return months;
    }

    private static double YearDiff(DateTime start, DateTime end)
    {
        int years = end.Year - start.Year;
        if (end.Month < start.Month || (end.Month == start.Month && end.Day < start.Day))
            years--;
        return years;
    }

    private static ScalarValue DateDifYD(DateTime start, DateTime end)
    {
        try
        {
            var anchor = new DateTime(end.Year, start.Month, start.Day);
            var adjustedStart = anchor > end ? anchor.AddYears(-1) : anchor;
            return new NumberValue(DateToSerial(end) - DateToSerial(adjustedStart));
        }
        catch (ArgumentOutOfRangeException) { return ErrorValue.Num; }
    }

    private static ScalarValue DateDifMD(DateTime start, DateTime end)
    {
        try
        {
            if (end.Day >= start.Day)
                return new NumberValue(end.Day - start.Day);
            int prevYear  = end.Month == 1 ? end.Year - 1 : end.Year;
            int prevMonth = end.Month == 1 ? 12 : end.Month - 1;
            return new NumberValue(end.Day + DaysInExcelMonth(prevYear, prevMonth) - start.Day);
        }
        catch (ArgumentOutOfRangeException) { return ErrorValue.Num; }
    }

    private static int DaysInExcelMonth(int year, int month) =>
        year == 1900 && month == 2 ? 29 : DateTime.DaysInMonth(year, month);

    private static ScalarValue TimeFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double rawM = ToNumber(args[1]), rawS = ToNumber(args[2]);
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => TimeScalar(value, rawM, rawS));
        return TimeScalar(args[0], rawM, rawS);
    }

    private static ScalarValue TimeScalar(ScalarValue hourValue, double rawM, double rawS)
    {
        double rawH = ToNumber(hourValue);
        if (!double.IsFinite(rawH) || !double.IsFinite(rawM) || !double.IsFinite(rawS)) return ErrorValue.Num;
        if (rawH < 0 || rawM < 0 || rawS < 0) return ErrorValue.Num;
        if (rawH > 32767 || rawM > 32767 || rawS > 32767) return ErrorValue.Num;
        int h = (int)rawH, m = (int)rawM, s = (int)rawS;
        double frac = (h * 3600 + m * 60 + s) / 86400.0;
        return new NumberValue(frac - Math.Floor(frac));
    }

    private static ScalarValue Timevalue(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, TimevalueScalar);
        return TimevalueScalar(args[0]);
    }

    private static ScalarValue TimevalueScalar(ScalarValue value)
    {
        var text = ToText(value);
        if (TimeSpan.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var ts) && ts.Days == 0)
            return new NumberValue(ts.TotalDays);
        if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return new NumberValue(dt.TimeOfDay.TotalDays);
        return ErrorValue.Value;
    }

    private static ScalarValue Datevalue(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, DatevalueScalar);
        return DatevalueScalar(args[0]);
    }

    private static ScalarValue DatevalueScalar(ScalarValue value)
    {
        var text = ToText(value);
        if (TryParseExcelFakeLeapDayValueText(text, CultureInfo.InvariantCulture, out _)) return new NumberValue(60);
        if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return new NumberValue(Math.Floor(DateToSerial(dt)));
        return ErrorValue.Value;
    }

    private static bool TryParseExcelFakeLeapDayValueText(string text, CultureInfo culture, out double serial)
    {
        serial = 0;
        var trimmed = text.Trim();
        var match = Regex.Match(trimmed, @"^(?:2/29/1900|02/29/1900|1900-02-29)(?:\s+(.+))?$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;

        serial = 60;
        if (match.Groups[1].Success)
        {
            if (!DateTime.TryParse(match.Groups[1].Value, culture, DateTimeStyles.None, out var time))
                return false;
            serial += time.TimeOfDay.TotalDays;
        }

        return true;
    }

    private static ScalarValue Eomonth(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is RangeValue monthsRange)
            return MapUnaryTextRange(monthsRange, value =>
            {
                double raw = ToNumber(value);
                if (!double.IsFinite(raw) || raw > int.MaxValue - 1 || raw < int.MinValue) return ErrorValue.Num;
                return EomonthScalar(args[0], (int)raw);
            });
        double rawMonths = ToNumber(args[1]);
        if (!double.IsFinite(rawMonths) || rawMonths > int.MaxValue - 1 || rawMonths < int.MinValue) return ErrorValue.Num;
        int months = (int)rawMonths;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => EomonthScalar(value, months));
        return EomonthScalar(args[0], months);
    }

    private static ScalarValue EomonthScalar(ScalarValue value, int months)
    {
        if (!TryOADateToDateTime(value, out var dt)) return ErrorValue.Num;
        try
        {
            var target = dt.AddMonths(months + 1);
            var eomonth = new DateTime(target.Year, target.Month, 1).AddDays(-1);
            return new NumberValue(DateToSerial(eomonth));
        }
        catch { return ErrorValue.Num; }
    }

    private static ScalarValue Weeknum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        double rawReturnType = args.Count > 1 && args[1] is not BlankValue ? ToNumber(args[1]) : 1;
        if (!double.IsFinite(rawReturnType)) return ErrorValue.Num;
        int returnType = (int)rawReturnType;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => WeeknumScalar(value, returnType));
        return WeeknumScalar(args[0], returnType);
    }

    private static ScalarValue WeeknumScalar(ScalarValue value, int returnType)
    {
        if (!TryOADateToDateTime(value, out var dt)) return ErrorValue.Num;
        if (Math.Floor(ToNumber(value)) == 0)
            return new NumberValue(0);
        if (returnType == 21)
            return new NumberValue(ExcelIsoWeeknum(dt));

        int firstDay = returnType switch
        {
            1 or 17 => 6,
            2 or 11 => 0,
            12 => 1,
            13 => 2,
            14 => 3,
            15 => 4,
            16 => 5,
            _ => -1
        };
        if (firstDay < 0) return ErrorValue.Num;
        var jan1 = new DateTime(dt.Year, 1, 1);
        int jan1Dow = (ExcelDowToMonIndex(jan1) - firstDay + 7) % 7;
        int dayOfYear = (dt - jan1).Days;
        return new NumberValue((dayOfYear + jan1Dow) / 7 + 1);
    }

    private static ScalarValue Isoweeknum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, IsoweeknumScalar);
        return IsoweeknumScalar(args[0]);
    }

    private static ScalarValue IsoweeknumScalar(ScalarValue value)
    {
        if (!TryOADateToDateTime(value, out var dt)) return ErrorValue.Num;
        return new NumberValue(ExcelIsoWeeknum(dt));
    }

    private static ScalarValue Workday(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (args[1] is RangeValue daysRange)
        {
            if (!TryCollectHolidays(args.Count > 2 ? args[2] : null, out var rangeHolidays, out var rangeHolidayError))
                return rangeHolidayError!;
            return MapUnaryTextRange(daysRange, value =>
            {
                double raw = ToNumber(value);
                if (!double.IsFinite(raw) || raw < int.MinValue + 1 || raw > int.MaxValue) return ErrorValue.Num;
                return WorkdayScalar(args[0], (int)raw, rangeHolidays);
            });
        }
        double rawDays = ToNumber(args[1]);
        if (!double.IsFinite(rawDays)) return ErrorValue.Num;
        if (rawDays < int.MinValue + 1 || rawDays > int.MaxValue) return ErrorValue.Num;
        int days = (int)rawDays;
        if (!TryCollectHolidays(args.Count > 2 ? args[2] : null, out var holidays, out var holidayError))
            return holidayError!;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => WorkdayScalar(value, days, holidays));
        return WorkdayScalar(args[0], days, holidays);
    }

    private static ScalarValue WorkdayScalar(ScalarValue startDate, int days, HashSet<DateTime> holidays)
    {
        if (!TryOADateToDateTime(startDate, out var current)) return ErrorValue.Num;
        int sign = days < 0 ? -1 : 1;
        int remaining = Math.Abs(days);
        // Skip full weeks when there are no holidays — 5 workdays = 7 calendar days
        if (remaining > 5 && holidays.Count == 0)
        {
            int fullWeeks = (remaining - 1) / 5; // keep ≥5 left so day-of-week boundary is handled correctly
            current = current.AddDays((long)sign * fullWeeks * 7);
            remaining -= fullWeeks * 5;
        }
        while (remaining > 0)
        {
            current = current.AddDays(sign);
            if (ExcelDowToMonIndex(current) < 5 &&
                !holidays.Contains(current.Date))
                remaining--;
        }
        return new NumberValue(DateToSerial(current));
    }

    private static ScalarValue Networkdays(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (!TryCollectHolidays(args.Count > 2 ? args[2] : null, out var holidays, out var holidayError))
            return holidayError!;
        if (args[0] is RangeValue startRange) return MapUnaryTextRange(startRange, value => NetworkdaysScalar(value, args[1], holidays));
        if (args[1] is RangeValue endRange) return MapUnaryTextRange(endRange, value => NetworkdaysScalar(args[0], value, holidays));
        return NetworkdaysScalar(args[0], args[1], holidays);
    }

    private static ScalarValue NetworkdaysScalar(ScalarValue startDate, ScalarValue endDate, HashSet<DateTime> holidays)
    {
        if (!TryOADateToDateTime(startDate, out var startRaw)) return ErrorValue.Num;
        if (!TryOADateToDateTime(endDate, out var endRaw)) return ErrorValue.Num;
        var startDt = startRaw.Date;
        var endDt   = endRaw.Date;
        int sign = startDt <= endDt ? 1 : -1;
        var lo = startDt <= endDt ? startDt : endDt;
        var hi = startDt <= endDt ? endDt   : startDt;
        int count = CountExcelWeekdaysInclusive(lo, hi);
        foreach (var h in holidays)
            if (h >= lo && h <= hi && ExcelDowToMonIndex(h) < 5)
                count--;
        return new NumberValue(sign * count);
    }

    private static int CountWeekdaysInclusive(DateTime lo, DateTime hi)
    {
        int totalDays = (int)(hi - lo).TotalDays + 1;
        int fullWeeks = totalDays / 7;
        int count = fullWeeks * 5;
        int startDow = (int)lo.DayOfWeek; // 0=Sun, 1=Mon, …, 6=Sat
        for (int i = 0; i < totalDays % 7; i++)
        {
            int dow = (startDow + i) % 7;
            if (dow != 0 && dow != 6) count++;
        }
        return count;
    }

    private static int CountExcelWeekdaysInclusive(DateTime lo, DateTime hi)
    {
        int totalDays = (int)(hi - lo).TotalDays + 1;
        int fullWeeks = totalDays / 7;
        int count = fullWeeks * 5;
        int startDow = ExcelDowToMonIndex(lo);
        for (int i = 0; i < totalDays % 7; i++)
        {
            int dow = (startDow + i) % 7;
            if (dow < 5) count++;
        }
        return count;
    }

    private static ScalarValue Days(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[0] is RangeValue endRange) return MapUnaryTextRange(endRange, value => DaysScalar(value, args[1]));
        if (args[1] is RangeValue startRange) return MapUnaryTextRange(startRange, value => DaysScalar(args[0], value));
        return DaysScalar(args[0], args[1]);
    }

    private static ScalarValue DaysScalar(ScalarValue endDate, ScalarValue startDate)
    {
        if (!TryOADateToDateTime(endDate, out var endDt))   return ErrorValue.Num;
        if (!TryOADateToDateTime(startDate, out var startDt)) return ErrorValue.Num;
        return new NumberValue(DateToSerial(endDt) - DateToSerial(startDt));
    }

    private static ScalarValue Days360(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        bool european = args.Count > 2 && args[2] is not BlankValue && ToNumber(args[2]) != 0;
        if (args[0] is RangeValue startRange) return MapUnaryTextRange(startRange, value => Days360Scalar(value, args[1], european));
        if (args[1] is RangeValue endRange) return MapUnaryTextRange(endRange, value => Days360Scalar(args[0], value, european));
        return Days360Scalar(args[0], args[1], european);
    }

    private static ScalarValue Days360Scalar(ScalarValue startDate, ScalarValue endDate, bool european)
    {
        if (!TryOADateToDateTime(startDate, out var startRaw)) return ErrorValue.Num;
        if (!TryOADateToDateTime(endDate, out var endRaw)) return ErrorValue.Num;
        var startDt = startRaw.Date;
        var endDt   = endRaw.Date;
        double days = european ? Days30E360(startDt, endDt) : Days30US360(startDt, endDt);
        return new NumberValue(Math.Truncate(days));
    }

    private static ScalarValue Yearfrac(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        double rawBasis = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 0;
        if (!double.IsFinite(rawBasis)) return ErrorValue.Num;
        int basis = (int)rawBasis;
        if (basis < 0 || basis > 4) return ErrorValue.Num;
        if (args[0] is RangeValue startRange) return MapUnaryTextRange(startRange, value => YearfracScalar(value, args[1], basis));
        if (args[1] is RangeValue endRange) return MapUnaryTextRange(endRange, value => YearfracScalar(args[0], value, basis));
        return YearfracScalar(args[0], args[1], basis);
    }

    private static ScalarValue YearfracScalar(ScalarValue startDate, ScalarValue endDate, int basis)
    {
        if (!TryOADateToDateTime(startDate, out var startRaw)) return ErrorValue.Num;
        if (!TryOADateToDateTime(endDate, out var endRaw)) return ErrorValue.Num;
        var startDt = startRaw.Date;
        var endDt   = endRaw.Date;
        double totalDays = DateToSerial(endDt) - DateToSerial(startDt);
        double result = basis switch
        {
            1 => totalDays / ActualActualDenominator(startDt, endDt),
            2 => totalDays / 360.0,
            3 => totalDays / 365.0,
            4 => Days30E360(startDt, endDt) / 360.0,
            _ => Days30US360(startDt, endDt) / 360.0
        };
        return new NumberValue(result);
    }

    private static double ActualActualDenominator(DateTime start, DateTime end)
    {
        // Normalize order so the denominator is well-defined when callers pass
        // a reversed range (Excel allows YEARFRAC(start > end) and returns a
        // negative value — without this swap the loop is empty and we'd
        // divide by zero, yielding ±infinity instead of a finite result).
        if (start > end) (start, end) = (end, start);
        if (start.Year == end.Year)
            return DateTime.IsLeapYear(start.Year) ? 366.0 : 365.0;
        double total = 0;
        for (int y = start.Year; y <= end.Year; y++)
            total += DateTime.IsLeapYear(y) ? 366.0 : 365.0;
        return total / (end.Year - start.Year + 1);
    }

    private static double Days30US360(DateTime d1, DateTime d2)
    {
        int y1 = d1.Year, m1 = d1.Month, dd1 = d1.Day;
        int y2 = d2.Year, m2 = d2.Month, dd2 = d2.Day;
        if (dd1 == 31) dd1 = 30;
        if (dd2 == 31 && dd1 == 30) dd2 = 30;
        return 360.0 * (y2 - y1) + 30.0 * (m2 - m1) + (dd2 - dd1);
    }

    private static double Days30E360(DateTime d1, DateTime d2)
    {
        int y1 = d1.Year, m1 = d1.Month, dd1 = d1.Day;
        int y2 = d2.Year, m2 = d2.Month, dd2 = d2.Day;
        if (dd1 == 31) dd1 = 30;
        if (dd2 == 31) dd2 = 30;
        return 360.0 * (y2 - y1) + 30.0 * (m2 - m1) + (dd2 - dd1);
    }

    private static int DowToMonIndex(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday    => 0,
        DayOfWeek.Tuesday   => 1,
        DayOfWeek.Wednesday => 2,
        DayOfWeek.Thursday  => 3,
        DayOfWeek.Friday    => 4,
        DayOfWeek.Saturday  => 5,
        _                   => 6 // Sunday
    };

    private static int ExcelDowToMonIndex(DateTime date)
    {
        int serial = (int)Math.Floor(DateToSerial(date));
        return ((serial + 5) % 7 + 7) % 7;
    }

    private static int ExcelIsoWeeknum(DateTime date)
    {
        int serial = (int)Math.Floor(DateToSerial(date));
        int dowMon0 = ExcelDowToMonIndex(serial);
        int thursdaySerial = serial + (3 - dowMon0);
        int weekYear = SerialToDate(thursdaySerial).Year;
        int jan4Serial = (int)Math.Floor(DateToSerial(new DateTime(weekYear, 1, 4)));
        int week1MondaySerial = jan4Serial - ExcelDowToMonIndex(jan4Serial);
        return (serial - week1MondaySerial) / 7 + 1;
    }

    private static bool TryCollectHolidays(ScalarValue? arg, out HashSet<DateTime> holidays, out ErrorValue? error)
    {
        holidays = new HashSet<DateTime>();
        error = null;
        if (arg is RangeValue rv)
        {
            foreach (var v in rv.Flatten())
            {
                if (v is ErrorValue rangeError)
                {
                    error = rangeError;
                    return false;
                }
                if (TryCellNumber(v, out double serial))
                {
                    if (!TryOADateToDateTime(new NumberValue(serial), out var holiday))
                    {
                        error = ErrorValue.Num;
                        return false;
                    }
                    holidays.Add(holiday.Date);
                }
            }
        }
        else if (arg is not null && TryCellNumber(arg, out double s))
        {
            if (!TryOADateToDateTime(new NumberValue(s), out var holiday))
            {
                error = ErrorValue.Num;
                return false;
            }
            holidays.Add(holiday.Date);
        }
        return true;
    }
}
