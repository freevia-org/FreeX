using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

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
        if (!TryOADateToDateTime(args[0], out var startRaw)) return ErrorValue.Num;
        if (!TryOADateToDateTime(args[1], out var endRaw)) return ErrorValue.Num;

        var (mask, maskErr) = ParseWeekendMask(args.Count > 2 ? args[2] : BlankValue.Instance);
        if (maskErr is not null) return maskErr;
        if (!TryCollectHolidays(args.Count > 3 ? args[3] : null, out var holidays, out var holidayError))
            return holidayError!;

        var startDt = startRaw.Date;
        var endDt = endRaw.Date;
        int sign = startDt <= endDt ? 1 : -1;
        var lo = startDt <= endDt ? startDt : endDt;
        var hi = startDt <= endDt ? endDt : startDt;

        int count = 0;
        for (var d = lo; d <= hi; d = d.AddDays(1))
        {
            if (mask![ExcelDowToMonIndex(d)]) continue;
            if (holidays.Contains(d.Date)) continue;
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
        if (!TryOADateToDateTime(args[0], out var current)) return ErrorValue.Num;
        double rawDays = ToNumber(args[1]);
        if (!double.IsFinite(rawDays)) return ErrorValue.Num;
        if (rawDays < int.MinValue + 1 || rawDays > int.MaxValue) return ErrorValue.Num;
        int days = (int)rawDays;

        var (mask, maskErr) = ParseWeekendMask(args.Count > 2 ? args[2] : BlankValue.Instance);
        if (maskErr is not null) return maskErr;
        if (!TryCollectHolidays(args.Count > 3 ? args[3] : null, out var holidays, out var holidayError))
            return holidayError!;

        int sign = days < 0 ? -1 : 1;
        int remaining = Math.Abs(days);
        while (remaining > 0)
        {
            current = current.AddDays(sign);
            if (mask![ExcelDowToMonIndex(current)]) continue;
            if (holidays.Contains(current.Date)) continue;
            remaining--;
        }
        return new NumberValue(DateToSerial(current));
    }
}
