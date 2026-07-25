using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Weekend-mask date functions: NETWORKDAYS.INTL, WORKDAY.INTL.

    /// <summary>
    /// Parses a weekend argument: number code 1-17 OR 7-char "0"/"1" string.
    /// Returns Mon-Sun mask (mask[0]=Mon,…,mask[6]=Sun). True = weekend day.
    /// </summary>
    private static (bool[]? Mask, ErrorValue? Error) ParseWeekendMask(ScalarValue value)
    {
        var mask = new bool[7];
        if (value is BlankValue)
        {
            mask[5] = true; // Sat
            mask[6] = true; // Sun
            return (mask, null);
        }

        if (value is TextValue or DirectTextLiteralValue)
        {
            var pattern = ToText(value);
            if (pattern.Length != 7) return (null, ErrorValue.Value);
            if (pattern.Any(c => c is not '0' and not '1')) return (null, ErrorValue.Value);
            if (pattern.All(c => c == '1')) return (null, ErrorValue.Value); // all-weekend not allowed
            for (int i = 0; i < 7; i++) mask[i] = pattern[i] == '1';
            return (mask, null);
        }

        double rawCode = ToNumber(value);
        if (!double.IsFinite(rawCode)) return (null, ErrorValue.Value);
        int code = (int)rawCode;
        // Mon=0..Sun=6
        switch (code)
        {
            case 1: mask[5] = true; mask[6] = true; break;        // Sat, Sun
            case 2: mask[6] = true; mask[0] = true; break;        // Sun, Mon
            case 3: mask[0] = true; mask[1] = true; break;        // Mon, Tue
            case 4: mask[1] = true; mask[2] = true; break;        // Tue, Wed
            case 5: mask[2] = true; mask[3] = true; break;        // Wed, Thu
            case 6: mask[3] = true; mask[4] = true; break;        // Thu, Fri
            case 7: mask[4] = true; mask[5] = true; break;        // Fri, Sat
            case 11: mask[6] = true; break;                       // Sun
            case 12: mask[0] = true; break;                       // Mon
            case 13: mask[1] = true; break;                       // Tue
            case 14: mask[2] = true; break;                       // Wed
            case 15: mask[3] = true; break;                       // Thu
            case 16: mask[4] = true; break;                       // Fri
            case 17: mask[5] = true; break;                       // Sat
            default: return (null, ErrorValue.Num);
        }
        return (mask, null);
    }

    private static ScalarValue NetworkdaysIntl(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        var (mask, maskErr) = ParseWeekendMask(args.Count > 2 ? args[2] : BlankValue.Instance);
        if (maskErr is not null) return maskErr;
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        if (!TryCollectHolidays(args.Count > 3 ? args[3] : null, uses1904DateSystem, out var holidays, out var holidayError))
            return holidayError!;

        return MapBinaryMathArgs(args[0], args[1], (startDate, endDate) => NetworkdaysIntlScalar(startDate, endDate, mask!, holidays, uses1904DateSystem));
    }

    private static ScalarValue NetworkdaysIntlScalar(ScalarValue startDate, ScalarValue endDate, bool[] mask, HashSet<double> holidays, bool uses1904DateSystem)
    {
        if (!TrySerialToDateTime(startDate, uses1904DateSystem, out _)) return ErrorValue.Num;
        if (!TrySerialToDateTime(endDate, uses1904DateSystem, out _)) return ErrorValue.Num;
        // Walk in Excel-serial space rather than via DateTime.AddDays: DateTime collapses the
        // 1900 phantom leap day (serial 60, "1900-02-29") onto the same real date as serial 59
        // ("1900-02-28"), so a day-by-day DateTime walk across that boundary would silently
        // skip serial 60 entirely (a single AddDays(1) from serial 59 lands on serial 61).
        double startSerial = Math.Floor(ToNumber(startDate));
        double endSerial = Math.Floor(ToNumber(endDate));
        int sign = startSerial <= endSerial ? 1 : -1;
        double lo = Math.Min(startSerial, endSerial);
        double hi = Math.Max(startSerial, endSerial);

        int count = 0;
        for (double serial = lo; serial <= hi; serial++)
        {
            if (mask[ExcelDowToMonIndex(serial, uses1904DateSystem)]) continue;
            if (holidays.Contains(serial)) continue;
            count++;
        }
        return new NumberValue(sign * count);
    }

    private static ScalarValue WorkdayIntl(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        var (mask, maskErr) = ParseWeekendMask(args.Count > 2 ? args[2] : BlankValue.Instance);
        if (maskErr is not null) return maskErr;
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        if (!TryCollectHolidays(args.Count > 3 ? args[3] : null, uses1904DateSystem, out var holidays, out var holidayError))
            return holidayError!;

        return MapBinaryMathArgs(args[0], args[1], (startDate, daysValue) => WorkdayIntlScalar(startDate, daysValue, mask!, holidays, uses1904DateSystem));
    }

    private static ScalarValue WorkdayIntlScalar(ScalarValue startDate, ScalarValue daysValue, bool[] mask, HashSet<double> holidays, bool uses1904DateSystem)
    {
        if (!TrySerialToDateTime(startDate, uses1904DateSystem, out _)) return ErrorValue.Num;
        // WORKDAY.INTL always returns a whole-day serial — Excel discards any time-of-day
        // fraction carried by the start date before walking forward/back.
        //
        // Walk in Excel-serial space rather than via DateTime.AddDays: DateTime collapses the
        // 1900 phantom leap day (serial 60, "1900-02-29") onto the same real date as serial 59
        // ("1900-02-28"), so stepping across that boundary via DateTime would silently skip
        // serial 60 entirely (a single AddDays(1) from serial 59 lands on serial 61).
        double serial = Math.Floor(ToNumber(startDate));
        double rawDays = ToNumber(daysValue);
        if (!double.IsFinite(rawDays)) return ErrorValue.Num;
        if (rawDays < int.MinValue + 1 || rawDays > int.MaxValue) return ErrorValue.Num;
        int days = (int)rawDays;

        int sign = days < 0 ? -1 : 1;
        int remaining = Math.Abs(days);
        while (remaining > 0)
        {
            serial += sign;
            if (mask[ExcelDowToMonIndex(serial, uses1904DateSystem)]) continue;
            if (holidays.Contains(serial)) continue;
            remaining--;
        }
        // Excel returns #NUM! when the resulting workday falls outside the valid serial-date range.
        // The pre-refactor loop got this implicitly by calling SerialToDate() on every candidate day;
        // the serial-keyed holiday lookup no longer does, so validate the out-of-range result here.
        if (!TrySerialToDateTime(new NumberValue(serial), uses1904DateSystem, out _))
            return ErrorValue.Num;
        return new NumberValue(serial);
    }
}
