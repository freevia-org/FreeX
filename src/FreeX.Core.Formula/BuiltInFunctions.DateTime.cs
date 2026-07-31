using System.Globalization;
using System.Text.RegularExpressions;

using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Date and time functions plus shared Excel date-system helpers.
    private static readonly Regex DateTimeTextHasTimeSeparatorRegex = new(@"\d\s*:\s*\d");
    private static readonly Regex DateTimeTextHasAmPmRegex = new(@"\b(?:AM|PM)\b", RegexOptions.IgnoreCase);
    private static readonly Regex DateTimeTextHasDateSeparatorRegex = new(@"\d+\s*[-/]\s*\d+");
    private static readonly Regex DateTimeTextHasMonthNameRegex = new(
        @"\b(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\b",
        RegexOptions.IgnoreCase);
    private static readonly Regex DateTimeFakeLeapDayTextRegex = new(
        @"^(?:2/29/1900|02/29/1900|1900-02-29)(?:\s+(.+))?$",
        RegexOptions.IgnoreCase);

    // Excel's DATEVALUE resolves ambiguous slash/dash dates using the current locale's
    // short-date month/day order (e.g. en-GB "03/04/2024" -> 3-Apr, not 4-Mar), matching
    // CellEntryParser.TryParseCurrentCultureDate. The culture is cloned fresh on every call
    // (not cached) because CultureInfo.CurrentCulture can change at runtime. Excel's
    // two-digit-year pivot is also 29 (00-29 -> 2000-2029, 30-99 -> 1930-1999), unlike
    // .NET's default calendar cutoff of 2049, so that pivot is applied to the clone too.
    private static CultureInfo CreateExcelTwoDigitYearCulture()
    {
        var culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
        culture.DateTimeFormat.Calendar.TwoDigitYearMax = 2029;
        return culture;
    }

    // Same two-digit-year pivot as CreateExcelTwoDigitYearCulture, but on an Invariant-culture
    // clone: TryParseMonthYearDateValueText's "MMM"/"MMMM" formats deliberately only recognize
    // English month abbreviations/names (matching Excel's DATEVALUE, which accepts these month
    // names regardless of locale), so it must not otherwise pick up the current locale's date
    // conventions the way the general free-form parse above does.
    private static readonly CultureInfo MonthYearDateValueCulture = CreateInvariantExcelTwoDigitYearCulture();

    private static CultureInfo CreateInvariantExcelTwoDigitYearCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.Calendar.TwoDigitYearMax = 2029;
        return culture;
    }

    // A single recalculation pass typically evaluates many dependent NOW()/TODAY() cells in a
    // tight, uninterrupted burst; Excel guarantees every one of them observes the exact same
    // instant for that pass (e.g. =A1=B1, both =NOW(), is always TRUE within one pass). Reading
    // DateTime.Now/DateTime.Today fresh on every single call — as this file used to do — lets a
    // dependency chain large enough that walking it takes measurable wall-clock time return
    // visibly different timestamps for cells evaluated early vs. late in the same pass.
    //
    // A fully general fix would thread a genuine per-recalculation-pass snapshot down from
    // RecalcEngine through IEvalContext; RecalcEngine lives in a different project (FreeX.Core.Calc)
    // and is out of this fix's scope. As a bounded, self-contained mitigation, cache the captured
    // instant and keep reusing it as long as NOW()/TODAY() keep being invoked within a short idle
    // window — exactly the "busy burst of volatile evaluations within one pass" shape described
    // above — and only recapture once that burst goes quiet (a strong signal a new, unrelated
    // pass has started, e.g. the next F9 keypress). TODAY() derives from the same cached instant
    // as NOW() (rather than its own independent DateTime.Today) so the two stay mutually
    // consistent within a pass too.
    private static DateTime? _cachedPassNow;
    private static long _cachedPassNowTicks;
    private const long PassNowIdleWindowMs = 200;

    private static DateTime CapturePassScopedNow()
    {
        long ticks = Environment.TickCount64;
        var cached = _cachedPassNow;
        if (cached is not null && ticks - _cachedPassNowTicks <= PassNowIdleWindowMs)
        {
            _cachedPassNowTicks = ticks; // slide the window so a long-but-busy pass stays consistent
            return cached.Value;
        }

        var fresh = DateTime.Now;
        _cachedPassNow = fresh;
        _cachedPassNowTicks = ticks;
        return fresh;
    }

    private static ScalarValue Now(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new DateTimeValue(DateToSerial(CapturePassScopedNow(), ctx.Uses1904DateSystem));

    private static ScalarValue Today(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new DateTimeValue(DateToSerial(CapturePassScopedNow().Date, ctx.Uses1904DateSystem));

    private static ScalarValue Date(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        return MapDateTimeTernaryArgs(args, (year, month, day) => DateScalar(year, ToNumber(month), ToNumber(day), uses1904DateSystem));
    }

    private static ScalarValue MapDateTimeTernaryArgs(
        IReadOnlyList<ScalarValue> args,
        Func<ScalarValue, ScalarValue, ScalarValue, ScalarValue> map)
    {
        RangeValue? range = null;
        foreach (var arg in args)
        {
            if (arg is not RangeValue argRange)
                continue;

            range = argRange;
            break;
        }

        if (range is null) return map(args[0], args[1], args[2]);

        for (int i = 0; i < 3; i++)
        {
            if (args[i] is RangeValue argRange &&
                (argRange.RowCount != range.RowCount || argRange.ColCount != range.ColCount))
                return ErrorValue.Value;
        }

        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
                cells[r, c] = map(DateTimeArgAt(args[0], r, c), DateTimeArgAt(args[1], r, c), DateTimeArgAt(args[2], r, c));
        return new RangeValue(cells);
    }

    private static ScalarValue DateTimeArgAt(ScalarValue value, int row, int col) =>
        value is RangeValue range ? range.Cells[row, col] : value;

    private static ScalarValue DateScalar(ScalarValue yearValue, double rawMonth, double rawDay, bool uses1904DateSystem)
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
        try
        {
            // Resolve month rollover against the *effective* year/month first (e.g. year=1901,
            // month=-10 rolls back to Feb 1900) before adding the day offset. The 1900
            // phantom-leap-day corrections below must key off this effective year/month, not
            // the raw `year`/`month` arguments, or a rollover that lands in Jan/Feb 1900 from a
            // different literal year (e.g. DATE(1901,-10,29)) misses the correction entirely.
            var monthAnchor = new DateTime(year, 1, 1).AddMonths(month - 1);
            int effectiveYear = monthAnchor.Year;
            int effectiveMonth = monthAnchor.Month;
            var dt = monthAnchor.AddDays(day - 1);
            double serial = DateToSerial(dt, uses1904DateSystem);
            if (serial < (uses1904DateSystem ? 0 : 1)) return ErrorValue.Num;
            if (!uses1904DateSystem && effectiveYear == 1900 && effectiveMonth >= 3 && dt < new DateTime(1900, 3, 1))
                return new NumberValue(serial + 1);
            if (!uses1904DateSystem && effectiveYear == 1900 && effectiveMonth == 3 && day == 0)
                return new NumberValue(60);
            // Requested effective month < 3 (Jan/Feb of 1900) but the constructed date rolled
            // forward on/after the phantom leap-day boundary (e.g. DATE(1900,2,30) real-rolls to
            // Mar 2, or DATE(1901,-10,29) rolls back to an effective Feb 1900 that then rolls
            // forward past it): the raw serial already counts one real day too many for the
            // boundary it crossed, so subtract 1. This must fire for ANY dt >= Mar 1, 1900 (not
            // just an exact match), so DATE(1900,2,30)=61 and DATE(1900,2,31)=62 as well as the
            // exact DATE(1900,2,29)=60 case.
            if (!uses1904DateSystem && effectiveYear == 1900 && effectiveMonth < 3 && dt >= new DateTime(1900, 3, 1))
                return new NumberValue(serial - 1);
            return new NumberValue(serial);
        }
        catch { return ErrorValue.Num; }
    }

    private static bool TrySerialToDateTime(ScalarValue v, bool uses1904DateSystem, out DateTime dt)
    {
        dt = default;
        var num = ToNumber(v);
        return ExcelDateSystem.TrySerialToDate(num, uses1904DateSystem, out dt);
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
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => YearScalar(value, uses1904DateSystem));
        return YearScalar(args[0], uses1904DateSystem);
    }

    private static ScalarValue YearScalar(ScalarValue value, bool uses1904DateSystem) =>
        !uses1904DateSystem && (IsExcelFakeLeapDay(value) || IsExcelZeroDate(value))
            ? new NumberValue(1900)
            : TrySerialToDateTime(value, uses1904DateSystem, out var dt) ? new NumberValue(dt.Year) : ErrorValue.Num;

    private static ScalarValue Month(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => MonthScalar(value, uses1904DateSystem));
        return MonthScalar(args[0], uses1904DateSystem);
    }

    private static ScalarValue MonthScalar(ScalarValue value, bool uses1904DateSystem) =>
        !uses1904DateSystem && IsExcelFakeLeapDay(value) ? new NumberValue(2)
        : !uses1904DateSystem && IsExcelZeroDate(value) ? new NumberValue(1)
        : TrySerialToDateTime(value, uses1904DateSystem, out var dt) ? new NumberValue(dt.Month) : ErrorValue.Num;

    private static ScalarValue Day(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => DayScalar(value, uses1904DateSystem));
        return DayScalar(args[0], uses1904DateSystem);
    }

    private static ScalarValue DayScalar(ScalarValue value, bool uses1904DateSystem) =>
        !uses1904DateSystem && IsExcelFakeLeapDay(value) ? new NumberValue(29)
        : !uses1904DateSystem && IsExcelZeroDate(value) ? new NumberValue(0)
        : TrySerialToDateTime(value, uses1904DateSystem, out var dt) ? new NumberValue(dt.Day) : ErrorValue.Num;

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
        var returnTypeArg = args.Count > 1 && args[1] is not BlankValue ? args[1] : new NumberValue(1);
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        return MapBinaryMathArgs(args[0], returnTypeArg, (value, returnType) => WeekdayScalarWithReturnType(value, returnType, uses1904DateSystem));
    }

    private static ScalarValue WeekdayScalarWithReturnType(ScalarValue value, ScalarValue returnTypeValue, bool uses1904DateSystem)
    {
        if (value is ErrorValue valueError) return valueError;
        if (returnTypeValue is ErrorValue returnTypeError) return returnTypeError;
        double rawReturnType = ToNumber(returnTypeValue);
        if (!double.IsFinite(rawReturnType)) return ErrorValue.Num;
        return WeekdayScalar(value, (int)rawReturnType, uses1904DateSystem);
    }

    private static ScalarValue WeekdayScalar(ScalarValue value, int returnType, bool uses1904DateSystem)
    {
        double rawSerial = ToNumber(value);
        if (!ExcelDateSystem.TrySerialToDate(rawSerial, uses1904DateSystem, out var date)) return ErrorValue.Num;
        int dow = uses1904DateSystem
            ? (int)date.DayOfWeek
            : (((int)Math.Floor(rawSerial) - 1) % 7 + 7) % 7; // 0=Sunday...6=Saturday in Excel's 1900 date system
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
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        return MapBinaryMathArgs(args[0], args[1], (value, monthsValue) => EdateScalar(value, monthsValue, uses1904DateSystem));
    }

    private static ScalarValue EdateScalar(ScalarValue value, ScalarValue monthsValue, bool uses1904DateSystem)
    {
        double rawMonths = ToNumber(monthsValue);
        if (!double.IsFinite(rawMonths) || rawMonths > int.MaxValue || rawMonths < int.MinValue) return ErrorValue.Num;
        return EdateScalar(value, (int)rawMonths, uses1904DateSystem);
    }

    private static ScalarValue EdateScalar(ScalarValue value, int months, bool uses1904DateSystem)
    {
        if (!uses1904DateSystem && IsExcelFakeLeapDay(value)) return EdateFromExcelFakeLeapDay(months);
        if (!TrySerialToDateTime(value, uses1904DateSystem, out var dt)) return ErrorValue.Num;
        try
        {
            var result = dt.AddMonths(months);
            var serial = DateToSerial(result, uses1904DateSystem);
            return serial < (uses1904DateSystem ? 0 : 1) ? ErrorValue.Num : new NumberValue(serial);
        }
        catch { return ErrorValue.Num; }
    }

    private static ScalarValue EdateFromExcelFakeLeapDay(int months)
    {
        if (!TryAddMonthsToExcelYearMonth(1900, 2, months, out var year, out var month))
            return ErrorValue.Num;

        int day = Math.Min(29, DaysInExcelMonth(year, month));
        return ExcelDateSerialFromParts(year, month, day);
    }

    private static ScalarValue Datedif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        return MapTernaryTextArgs(args[0], args[1], args[2], (start, end, unit) => DatedifScalar(start, end, unit, uses1904DateSystem));
    }

    private static ScalarValue DatedifScalar(ScalarValue startValue, ScalarValue endValue, ScalarValue unitValue, bool uses1904DateSystem)
    {
        if (startValue is ErrorValue startError) return startError;
        if (endValue is ErrorValue endError) return endError;
        if (unitValue is ErrorValue unitError) return unitError;
        if (!TrySerialToDateTime(startValue, uses1904DateSystem, out var startRaw)) return ErrorValue.Num;
        if (!TrySerialToDateTime(endValue, uses1904DateSystem, out var endRaw)) return ErrorValue.Num;
        // DATEDIF operates on whole dates — discard any time portion so that
        // e.g. DATEDIF(2024-01-01 23:00, 2024-01-02 01:00, "D") returns 1 (Excel)
        // rather than 0 (TimeSpan.Days would otherwise round toward zero).
        var start = startRaw.Date;
        var end = endRaw.Date;
        // Guard against a reversed start>end range using the raw floored serials, not the
        // collapsed DateTime values above: ExcelDateSystem.SerialToDate maps both the 1900
        // phantom leap day (serial 60, "1900-02-29") and serial 59 ("1900-02-28") onto the
        // same DateTime, so e.g. DATEDIF(60, 59, "D") (a reversed range) would otherwise read
        // end.Date == start.Date and slip past this check instead of correctly erroring.
        if (Math.Floor(ToNumber(endValue)) < Math.Floor(ToNumber(startValue))) return ErrorValue.Num;
        var unit  = ToText(unitValue).ToUpperInvariant();

        // start/end (above) collapse the 1900 phantom leap day (serial 60, "1900-02-29") onto
        // the same DateTime as serial 59 ("1900-02-28"), so start.Day/end.Day would read 28
        // instead of Excel's 29 for that exact date. Substitute the correct day-of-month
        // wherever the M/Y/YM/MD/YD arms below read it. (The "D" arm just below already
        // avoids this by diffing raw serials directly instead of DateTime day-of-month.)
        int startDay = !uses1904DateSystem && IsExcelFakeLeapDay(startValue) ? 29 : start.Day;
        int endDay   = !uses1904DateSystem && IsExcelFakeLeapDay(endValue) ? 29 : end.Day;

        return unit switch
        {
            // Compute directly in serial space rather than round-tripping through DateTime:
            // ExcelDateSystem.SerialToDate maps both the 1900 phantom-leap-day serial 60
            // ("1900-02-29") and serial 59 ("1900-02-28") onto the same DateTime, so a
            // DateTime-based difference silently collapses that boundary (e.g. DATEDIF(59,60,"D")
            // would come out 0 instead of 1). Floor to match DATEDIF's whole-date semantics
            // (time-of-day is discarded), then diff the raw serials directly.
            "D"  => new NumberValue(ExcelDateSystem.SerialDayDifference(
                        Math.Floor(ToNumber(startValue)), Math.Floor(ToNumber(endValue)))),
            "M"  => new NumberValue(MonthDiff(start, end, startDay, endDay)),
            "Y"  => new NumberValue(YearDiff(start, end, startDay, endDay)),
            "YM" => new NumberValue((int)MonthDiff(start, end, startDay, endDay) % 12),
            "YD" => DateDifYD(start, end, startDay, uses1904DateSystem),
            "MD" => DateDifMD(end, startDay, endDay),
            _    => ErrorValue.Num
        };
    }

    private static double MonthDiff(DateTime start, DateTime end, int startDay, int endDay)
    {
        int months = (end.Year - start.Year) * 12 + (end.Month - start.Month);
        if (endDay < startDay) months--;
        return months;
    }

    private static double YearDiff(DateTime start, DateTime end, int startDay, int endDay)
    {
        int years = end.Year - start.Year;
        if (end.Month < start.Month || (end.Month == start.Month && endDay < startDay))
            years--;
        return years;
    }

    private static ScalarValue DateDifYD(DateTime start, DateTime end, int startDay, bool uses1904DateSystem)
    {
        try
        {
            // Clamp startDay in case start is Feb 29 (leap, or the 1900 phantom leap day) and
            // the anchor year is non-leap.
            int anchorYear = end.Year;
            int clampedDay = Math.Min(startDay, DateTime.DaysInMonth(anchorYear, start.Month));
            var anchor = new DateTime(anchorYear, start.Month, clampedDay);
            if (anchor > end)
            {
                int prevYear = anchorYear - 1;
                clampedDay = Math.Min(startDay, DateTime.DaysInMonth(prevYear, start.Month));
                anchor = new DateTime(prevYear, start.Month, clampedDay);
            }
            return new NumberValue(DateToSerial(end, uses1904DateSystem) - DateToSerial(anchor, uses1904DateSystem));
        }
        catch (ArgumentOutOfRangeException) { return ErrorValue.Num; }
    }

    private static ScalarValue DateDifMD(DateTime end, int startDay, int endDay)
    {
        // Pure integer arithmetic — never constructs a DateTime, so it can never throw
        // ArgumentOutOfRangeException and startDay never needs clamping against any month's
        // length (that clamp was copy-pasted from DateDifYD, which genuinely needs it because
        // it builds a real DateTime anchor). Clamping here just silently corrupted ordinary
        // MD pairs whenever startDay is 29/30/31 and end's month is shorter.
        if (endDay >= startDay)
            return new NumberValue(endDay - startDay);
        int prevYear  = end.Month == 1 ? end.Year - 1 : end.Year;
        int prevMonth = end.Month == 1 ? 12 : end.Month - 1;
        return new NumberValue(endDay + DaysInExcelMonth(prevYear, prevMonth) - startDay);
    }

    private static int DaysInExcelMonth(int year, int month) =>
        year == 1900 && month == 2 ? 29 : DateTime.DaysInMonth(year, month);

    private static ScalarValue TimeFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapDateTimeTernaryArgs(args, (hour, minute, second) => TimeScalar(hour, ToNumber(minute), ToNumber(second)));
    }

    private static ScalarValue TimeScalar(ScalarValue hourValue, double rawM, double rawS)
    {
        double rawH = ToNumber(hourValue);
        if (!double.IsFinite(rawH) || !double.IsFinite(rawM) || !double.IsFinite(rawS)) return ErrorValue.Num;
        int h = (int)rawH, m = (int)rawM, s = (int)rawS;
        if (h < 0 || m < 0 || s < 0) return ErrorValue.Num;
        if (h > 32767 || m > 32767 || s > 32767) return ErrorValue.Num;
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
        var hasTimeComponent = TextHasTimeComponent(text);
        // TIMEVALUE ignores any date portion and returns just the time-of-day fraction, so a
        // date-only string (no time component) is still valid input - it yields 0 (midnight) -
        // as long as it at least looks like a date/time text; a bare non-date/time string is #VALUE!.
        if (!hasTimeComponent && !TextHasDateComponent(text)) return ErrorValue.Value;
        if (TryParseExcelFakeLeapDayValueText(text, CultureInfo.InvariantCulture, out var fakeLeapSerial))
            return new NumberValue(fakeLeapSerial - Math.Floor(fakeLeapSerial));
        // Parse a plain "H:MM[:SS[.f]]" elapsed-time literal directly rather than via
        // TimeSpan.TryParse: .NET's general TimeSpan parser reinterprets a 3-field "H:MM:SS"
        // string as "D:HH:MM" (days:hours:minutes) once H exceeds 23 (e.g. "36:00:00" parses
        // to 36 *days*, and "25:30:00" fails outright because the reinterpreted "30" hours
        // field is itself out of range) - neither matches Excel, which always reads the first
        // field as an unbounded elapsed-hours count and returns the fraction mod 1 day
        // (so "36:00:00" -> 0.5, "25:30:00" -> 0.0625).
        if (hasTimeComponent && TryParseElapsedHmsText(text, out var elapsedFraction))
            return new NumberValue(elapsedFraction);
        // Use the same current-culture-aware parse DATEVALUE uses (CreateExcelTwoDigitYearCulture),
        // not a hardcoded InvariantCulture: a date+time string with a day-of-month > 12 (e.g.
        // "14/3/2024 15:30" under a D/M/Y locale) or a '.'-separated date (e.g. de-DE) must resolve
        // per the system's regional short-date order, matching real Excel and DATEVALUE/CellEntryParser.
        if (DateTime.TryParse(text, CreateExcelTwoDigitYearCulture(),
                System.Globalization.DateTimeStyles.None, out var dt))
            return new NumberValue(dt.TimeOfDay.TotalDays);
        return ErrorValue.Value;
    }

    private static readonly Regex ElapsedHmsTextRegex = new(
        @"^\s*(\d+)\s*:\s*([0-5]?\d)(?:\s*:\s*([0-5]?\d(?:\.\d+)?))?\s*$");

    private static bool TryParseElapsedHmsText(string text, out double fraction)
    {
        fraction = 0;
        var match = ElapsedHmsTextRegex.Match(text);
        if (!match.Success) return false;
        if (!double.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours))
            return false;
        if (!double.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
            return false;
        double seconds = 0;
        if (match.Groups[3].Success &&
            !double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
            return false;

        var totalDays = (hours * 3600 + minutes * 60 + seconds) / 86400.0;
        fraction = totalDays - Math.Floor(totalDays);
        return true;
    }

    private static ScalarValue Datevalue(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => DatevalueScalar(value, uses1904DateSystem));
        return DatevalueScalar(args[0], uses1904DateSystem);
    }

    private static ScalarValue DatevalueScalar(ScalarValue value, bool uses1904DateSystem)
    {
        var text = ToText(value);
        if (!uses1904DateSystem && TryParseExcelFakeLeapDayValueText(text, CultureInfo.InvariantCulture, out _)) return new NumberValue(60);
        if (!TextHasDateComponent(text)) return ErrorValue.Value;
        if (TryParseMonthYearDateValueText(text, out var monthYearDate))
            return DateValueSerialOrNum(monthYearDate, uses1904DateSystem);
        if (DateTime.TryParse(text, CreateExcelTwoDigitYearCulture(),
                System.Globalization.DateTimeStyles.None, out var dt))
            return DateValueSerialOrNum(dt, uses1904DateSystem);
        return ErrorValue.Value;
    }

    private static ScalarValue DateValueSerialOrNum(DateTime date, bool uses1904DateSystem)
    {
        // Excel's documented behavior: a syntactically valid date_text outside the current
        // date base's representable range yields #VALUE!, not #NUM! (#NUM! is reserved for
        // out-of-range *numeric* arguments elsewhere in the date functions).
        var serial = Math.Floor(DateToSerial(date, uses1904DateSystem));
        return serial < (uses1904DateSystem ? 0 : 1) ? ErrorValue.Value : new NumberValue(serial);
    }

    private static bool TextHasTimeComponent(string text) =>
        DateTimeTextHasTimeSeparatorRegex.IsMatch(text) ||
        DateTimeTextHasAmPmRegex.IsMatch(text);

    private static bool TextHasDateComponent(string text) =>
        DateTimeTextHasDateSeparatorRegex.IsMatch(text) ||
        DateTimeTextHasMonthNameRegex.IsMatch(text);

    private static bool TryParseMonthYearDateValueText(string text, out DateTime dt) =>
        DateTime.TryParseExact(
            text.Trim(),
            [
                "MMMM yyyy", "MMM yyyy", "MMMM, yyyy", "MMM, yyyy", "MMMM-yyyy", "MMM-yyyy",
                // Two-digit-year variants: resolved via MonthYearDateValueCulture's Excel pivot
                // (00-29 -> 2000-2029, 30-99 -> 1930-1999), e.g. "Jan-99" -> Jan 1999.
                "MMMM yy", "MMM yy", "MMMM, yy", "MMM, yy", "MMMM-yy", "MMM-yy",
            ],
            MonthYearDateValueCulture,
            DateTimeStyles.None,
            out dt);

    /// <summary>
    /// Recognizes Excel's fictitious 1900 leap-day literal ("2/29/1900", "02/29/1900", or
    /// "1900-02-29", optionally followed by a time-of-day) in typed/pasted text and maps it to
    /// serial 60, the same way DATEVALUE/TIMEVALUE already do. .NET's <see cref="DateTime"/>
    /// cannot represent that date directly (1900 is not a real leap year), so this is the entry
    /// point non-formula callers (e.g. live cell entry in CellEntryParser) should use instead of
    /// falling through to a plain DateTime.TryParse, which always fails for this literal.
    /// </summary>
    public static bool TryParseExcelFakeLeapDayText(string text, out double serial) =>
        TryParseExcelFakeLeapDayValueText(text, CultureInfo.InvariantCulture, out serial);

    private static bool TryParseExcelFakeLeapDayValueText(string text, CultureInfo culture, out double serial)
    {
        serial = 0;
        var trimmed = text.Trim();
        var match = DateTimeFakeLeapDayTextRegex.Match(trimmed);
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
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        return MapBinaryMathArgs(args[0], args[1], (value, monthsValue) => EomonthScalar(value, monthsValue, uses1904DateSystem));
    }

    private static ScalarValue EomonthScalar(ScalarValue value, ScalarValue monthsValue, bool uses1904DateSystem)
    {
        double rawMonths = ToNumber(monthsValue);
        if (!double.IsFinite(rawMonths) || rawMonths > int.MaxValue - 1 || rawMonths < int.MinValue) return ErrorValue.Num;
        return EomonthScalar(value, (int)rawMonths, uses1904DateSystem);
    }

    private static ScalarValue EomonthScalar(ScalarValue value, int months, bool uses1904DateSystem)
    {
        if (!uses1904DateSystem && IsExcelFakeLeapDay(value)) return EomonthFromExcelFakeLeapDay(months);
        if (!TrySerialToDateTime(value, uses1904DateSystem, out var dt)) return ErrorValue.Num;
        try
        {
            var target = dt.AddMonths(months + 1);
            var eomonth = new DateTime(target.Year, target.Month, 1).AddDays(-1);
            var serial = DateToSerial(eomonth, uses1904DateSystem);
            return serial < (uses1904DateSystem ? 0 : 1) ? ErrorValue.Num : new NumberValue(serial);
        }
        catch { return ErrorValue.Num; }
    }

    private static ScalarValue EomonthFromExcelFakeLeapDay(int months)
    {
        if (!TryAddMonthsToExcelYearMonth(1900, 2, months, out var year, out var month))
            return ErrorValue.Num;

        return ExcelDateSerialFromParts(year, month, DaysInExcelMonth(year, month));
    }

    private static bool TryAddMonthsToExcelYearMonth(int year, int month, int offset, out int targetYear, out int targetMonth)
    {
        long zeroBasedMonth = (long)year * 12 + month - 1 + offset;
        targetYear = (int)Math.DivRem(zeroBasedMonth, 12, out var monthIndex);
        if (monthIndex < 0)
        {
            targetYear--;
            monthIndex += 12;
        }

        targetMonth = (int)monthIndex + 1;
        return targetYear is >= 1 and <= 9999;
    }

    private static ScalarValue ExcelDateSerialFromParts(int year, int month, int day, bool uses1904DateSystem = false)
    {
        if (!uses1904DateSystem && year == 1900 && month == 2 && day == 29) return new NumberValue(60);
        try
        {
            var serial = DateToSerial(new DateTime(year, month, day), uses1904DateSystem);
            return serial < (uses1904DateSystem ? 0 : 1) ? ErrorValue.Num : new NumberValue(serial);
        }
        catch { return ErrorValue.Num; }
    }

    private static ScalarValue Weeknum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        var returnTypeArg = args.Count > 1 ? args[1] : BlankValue.Instance;
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        return MapBinaryMathArgs(args[0], returnTypeArg, (value, returnType) => WeeknumScalar(value, returnType, uses1904DateSystem));
    }

    private static ScalarValue WeeknumScalar(ScalarValue value, ScalarValue returnTypeValue, bool uses1904DateSystem)
    {
        double rawReturnType = returnTypeValue is not BlankValue ? ToNumber(returnTypeValue) : 1;
        if (!double.IsFinite(rawReturnType)) return ErrorValue.Num;
        int returnType = (int)rawReturnType;
        return WeeknumScalar(value, returnType, uses1904DateSystem);
    }

    private static ScalarValue WeeknumScalar(ScalarValue value, int returnType, bool uses1904DateSystem)
    {
        if (!TrySerialToDateTime(value, uses1904DateSystem, out var dt)) return ErrorValue.Num;
        if (returnType == 21)
            return new NumberValue(ExcelIsoWeeknum(dt, uses1904DateSystem));
        double rawSerial = Math.Floor(ToNumber(value));
        if (!uses1904DateSystem && rawSerial == 0)
            return new NumberValue(0);

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
        int jan1Serial = (int)Math.Floor(DateToSerial(jan1, uses1904DateSystem));
        int jan1Dow = (ExcelDowToMonIndex(jan1, uses1904DateSystem) - firstDay + 7) % 7;
        // Day-of-year computed from raw serial arithmetic rather than DateTime subtraction:
        // ExcelDateSystem.SerialToDate collapses the 1900 phantom leap day (serial 60,
        // "1900-02-29") onto the same real DateTime as serial 59 ("1900-02-28"), so a
        // DateTime-based difference would put both serials in the same week.
        int dayOfYear = (int)rawSerial - jan1Serial;
        return new NumberValue((dayOfYear + jan1Dow) / 7 + 1);
    }

    private static ScalarValue Isoweeknum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => IsoweeknumScalar(value, uses1904DateSystem));
        return IsoweeknumScalar(args[0], uses1904DateSystem);
    }

    private static ScalarValue IsoweeknumScalar(ScalarValue value, bool uses1904DateSystem)
    {
        if (!TrySerialToDateTime(value, uses1904DateSystem, out var dt)) return ErrorValue.Num;
        return new NumberValue(ExcelIsoWeeknum(dt, uses1904DateSystem));
    }

    private static ScalarValue Workday(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        if (!TryCollectHolidays(args.Count > 2 ? args[2] : null, uses1904DateSystem, out var holidays, out var holidayError))
            return holidayError!;
        return MapBinaryMathArgs(args[0], args[1], (startDate, daysValue) => WorkdayScalar(startDate, daysValue, holidays, uses1904DateSystem));
    }

    private static ScalarValue WorkdayScalar(ScalarValue startDate, ScalarValue daysValue, HashSet<double> holidays, bool uses1904DateSystem)
    {
        double rawDays = ToNumber(daysValue);
        if (!double.IsFinite(rawDays)) return ErrorValue.Num;
        if (rawDays < int.MinValue + 1 || rawDays > int.MaxValue) return ErrorValue.Num;
        return WorkdayScalar(startDate, (int)rawDays, holidays, uses1904DateSystem);
    }

    private static ScalarValue WorkdayScalar(ScalarValue startDate, int days, HashSet<double> holidays, bool uses1904DateSystem)
    {
        if (!TrySerialToDateTime(startDate, uses1904DateSystem, out _)) return ErrorValue.Num;
        // WORKDAY always returns a whole-day serial — Excel discards any time-of-day
        // fraction carried by the start date before walking forward/back.
        //
        // Walk in Excel-serial space rather than via DateTime.AddDays: ExcelDateSystem
        // collapses the 1900 phantom leap day (serial 60, "1900-02-29") onto the same real
        // DateTime as serial 59 ("1900-02-28"), so re-deriving the serial from a DateTime
        // after stepping through that boundary would silently skip serial 60 (a single
        // AddDays(1) from serial 59 lands straight on serial 61).
        double serial = Math.Floor(ToNumber(startDate));
        int sign = days < 0 ? -1 : 1;
        int remaining = Math.Abs(days);
        // Skip full weeks when there are no holidays — 5 workdays = 7 calendar days
        if (remaining > 5 && holidays.Count == 0)
        {
            int fullWeeks = (remaining - 1) / 5; // keep ≥5 left so day-of-week boundary is handled correctly
            serial += (double)sign * fullWeeks * 7;
            remaining -= fullWeeks * 5;
        }
        while (remaining > 0)
        {
            serial += sign;
            if (ExcelDowToMonIndex(serial, uses1904DateSystem) < 5 && !holidays.Contains(serial))
                remaining--;
        }
        // Excel returns #NUM! when the resulting workday falls outside the valid serial-date range.
        // The pre-refactor loop got this implicitly by calling SerialToDate() on every candidate day
        // (which threw past the max serial); the serial-keyed holiday lookup no longer does, so the
        // out-of-range result must be validated explicitly here.
        if (!TrySerialToDateTime(new NumberValue(serial), uses1904DateSystem, out _))
            return ErrorValue.Num;
        return new NumberValue(serial);
    }

    private static ScalarValue Networkdays(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        if (!TryCollectHolidays(args.Count > 2 ? args[2] : null, uses1904DateSystem, out var holidays, out var holidayError))
            return holidayError!;
        return MapBinaryMathArgs(args[0], args[1], (startDate, endDate) => NetworkdaysScalar(startDate, endDate, holidays, uses1904DateSystem));
    }

    private static ScalarValue NetworkdaysScalar(ScalarValue startDate, ScalarValue endDate, HashSet<double> holidays, bool uses1904DateSystem)
    {
        if (!TrySerialToDateTime(startDate, uses1904DateSystem, out _)) return ErrorValue.Num;
        if (!TrySerialToDateTime(endDate, uses1904DateSystem, out _)) return ErrorValue.Num;
        // Count in Excel-serial space rather than via DateTime subtraction: DateTime collapses
        // the 1900 phantom leap day (serial 60, "1900-02-29") onto the same real date as serial
        // 59 ("1900-02-28"), so a span whose endpoints straddle that boundary would otherwise
        // be undercounted by one distinct day.
        double startSerial = Math.Floor(ToNumber(startDate));
        double endSerial   = Math.Floor(ToNumber(endDate));
        int sign = startSerial <= endSerial ? 1 : -1;
        double lo = Math.Min(startSerial, endSerial);
        double hi = Math.Max(startSerial, endSerial);
        int count = CountExcelWeekdaysInclusive(lo, hi, uses1904DateSystem);
        foreach (var hSerial in holidays)
        {
            if (hSerial >= lo && hSerial <= hi && ExcelDowToMonIndex(hSerial, uses1904DateSystem) < 5)
                count--;
        }
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

    private static int CountExcelWeekdaysInclusive(double loSerial, double hiSerial, bool uses1904DateSystem)
    {
        int totalDays = (int)(hiSerial - loSerial) + 1;
        int fullWeeks = totalDays / 7;
        int count = fullWeeks * 5;
        int startDow = ExcelDowToMonIndex(loSerial, uses1904DateSystem);
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
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        return MapBinaryMathArgs(args[0], args[1], (endDate, startDate) => DaysScalar(endDate, startDate, uses1904DateSystem));
    }

    private static ScalarValue DaysScalar(ScalarValue endDate, ScalarValue startDate, bool uses1904DateSystem)
    {
        // Validate both operands are representable dates, but compute the difference directly
        // in serial space rather than round-tripping through DateTime: ExcelDateSystem.SerialToDate
        // maps both the 1900 phantom-leap-day serial 60 ("1900-02-29") and serial 59
        // ("1900-02-28") onto the same DateTime, so a DateTime-based difference silently
        // collapses that boundary (e.g. DAYS(60,59) would come out 0 instead of 1).
        if (!TrySerialToDateTime(endDate, uses1904DateSystem, out _))   return ErrorValue.Num;
        if (!TrySerialToDateTime(startDate, uses1904DateSystem, out _)) return ErrorValue.Num;
        return new NumberValue(ExcelDateSystem.SerialDayDifference(ToNumber(startDate), ToNumber(endDate)));
    }

    private static ScalarValue Days360(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var methodArg = args.Count > 2 ? args[2] : BlankValue.Instance;
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        return MapTernaryTextArgs(args[0], args[1], methodArg, (start, end, method) => Days360Scalar(start, end, method, uses1904DateSystem));
    }

    private static ScalarValue Days360Scalar(ScalarValue startDate, ScalarValue endDate, ScalarValue methodValue, bool uses1904DateSystem)
    {
        bool european = methodValue is not BlankValue && ToNumber(methodValue) != 0;
        if (!TrySerialToDateTime(startDate, uses1904DateSystem, out var startRaw)) return ErrorValue.Num;
        if (!TrySerialToDateTime(endDate, uses1904DateSystem, out var endRaw)) return ErrorValue.Num;
        var startDt = startRaw.Date;
        var endDt   = endRaw.Date;
        // The 1900 phantom leap day (serial 60) collapses onto the same DateTime as serial 59
        // ("1900-02-28"), which would otherwise give it the wrong day-of-month component (28)
        // for 30/360 day counting; DAY(60) is already special-cased to 29 elsewhere in this
        // file (see DayScalar) -- match that here.
        bool startIsFakeLeapDay = !uses1904DateSystem && IsExcelFakeLeapDay(startDate);
        bool endIsFakeLeapDay   = !uses1904DateSystem && IsExcelFakeLeapDay(endDate);
        double days = european
            ? Days30E360(startDt, endDt, startIsFakeLeapDay, endIsFakeLeapDay)
            : Days30US360Days360(startDt, endDt, startIsFakeLeapDay, endIsFakeLeapDay);
        return new NumberValue(Math.Truncate(days));
    }

    private static ScalarValue Yearfrac(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var basisArg = args.Count > 2 ? args[2] : BlankValue.Instance;
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        return MapTernaryTextArgs(args[0], args[1], basisArg, (start, end, basis) => YearfracScalar(start, end, basis, uses1904DateSystem));
    }

    private static ScalarValue YearfracScalar(ScalarValue startDate, ScalarValue endDate, ScalarValue basisValue, bool uses1904DateSystem)
    {
        double rawBasis = basisValue is not BlankValue ? ToNumber(basisValue) : 0;
        if (!double.IsFinite(rawBasis)) return ErrorValue.Num;
        int basis = (int)rawBasis;
        if (basis < 0 || basis > 4) return ErrorValue.Num;
        return YearfracScalar(startDate, endDate, basis, uses1904DateSystem);
    }

    private static ScalarValue YearfracScalar(ScalarValue startDate, ScalarValue endDate, int basis, bool uses1904DateSystem)
    {
        if (!TrySerialToDateTime(startDate, uses1904DateSystem, out var startRaw)) return ErrorValue.Num;
        if (!TrySerialToDateTime(endDate, uses1904DateSystem, out var endRaw)) return ErrorValue.Num;
        var startDt = startRaw.Date;
        var endDt   = endRaw.Date;
        // The 1900 phantom leap day (serial 60) collapses onto the same DateTime as serial 59
        // ("1900-02-28"): track which endpoint (if either) is really serial 60 so the day-of-
        // month component used by basis 0/4 below can be corrected for it (DAY(60) is already
        // special-cased to 29 elsewhere in this file -- see DayScalar), and keep the raw
        // serials alongside the DateTimes so ordering/day-count can be done in serial space,
        // which -- unlike the collapsed DateTimes -- still tells 59 and 60 apart.
        bool startIsFakeLeapDay = !uses1904DateSystem && IsExcelFakeLeapDay(startDate);
        bool endIsFakeLeapDay   = !uses1904DateSystem && IsExcelFakeLeapDay(endDate);
        double startSerial = Math.Floor(ToNumber(startDate));
        double endSerial   = Math.Floor(ToNumber(endDate));
        // Excel's YEARFRAC always returns a non-negative fraction regardless of
        // which date is earlier — normalize order up front so every basis's
        // day-count math (which is not symmetric under swap) yields the same
        // magnitude as the argument order (start, end) would.
        if (startSerial > endSerial)
        {
            (startDt, endDt) = (endDt, startDt);
            (startIsFakeLeapDay, endIsFakeLeapDay) = (endIsFakeLeapDay, startIsFakeLeapDay);
            (startSerial, endSerial) = (endSerial, startSerial);
        }
        // Diff the raw serials directly rather than round-tripping through DateTime:
        // ExcelDateSystem.SerialToDate maps both serial 59 and 60 onto the identical
        // DateTime, so a DateTime-based diff silently collapses that boundary (e.g.
        // YEARFRAC(59,60,3) would come out 0 instead of the correct 1/365). Same technique
        // already used by DATEDIF's "D" unit and by DAYS.
        double totalDays = ExcelDateSystem.SerialDayDifference(startSerial, endSerial);
        double result = basis switch
        {
            1 => totalDays / ActualActualDenominator(startDt, endDt),
            2 => totalDays / 360.0,
            3 => totalDays / 365.0,
            4 => Days30E360(startDt, endDt, startIsFakeLeapDay, endIsFakeLeapDay) / 360.0,
            _ => Days30US360(startDt, endDt, startIsFakeLeapDay, endIsFakeLeapDay) / 360.0
        };
        return new NumberValue(result);
    }

    private static double ActualActualDenominator(DateTime start, DateTime end)
    {
        // Defensive normalization: YearfracScalar already swaps reversed ranges
        // before calling this helper, but keep this guard so the denominator
        // stays well-defined for any other caller — without it, a reversed
        // range would leave the averaging loop empty and divide by zero,
        // yielding ±infinity instead of a finite result.
        if (start > end) (start, end) = (end, start);

        // Excel basis 1 special case: for a span of at most one year, the
        // denominator is 366 only if Feb 29 falls within [start, end), OR both
        // endpoints are in the same leap year; otherwise 365.  The average-of-
        // spanned-years formula applies only when end > start.AddYears(1).
        if (end <= start.AddYears(1))
        {
            // Same-year fast path: leap year iff Feb 29 is in that year.
            if (start.Year == end.Year)
                return DateTime.IsLeapYear(start.Year) ? 366.0 : 365.0;
            // Cross-year span ≤1 year: check whether Feb 29 lies in [start, end).
            // Feb 29 in start.Year: only possible when start.Year is a leap year.
            if (DateTime.IsLeapYear(start.Year))
            {
                var feb29Start = new DateTime(start.Year, 2, 29);
                if (start <= feb29Start && feb29Start < end)
                    return 366.0;
            }
            // Feb 29 in end.Year: only possible when end.Year is a leap year.
            if (DateTime.IsLeapYear(end.Year))
            {
                var feb29End = new DateTime(end.Year, 2, 29);
                if (feb29End >= start && feb29End < end)
                    return 366.0;
            }
            return 365.0;
        }

        // True multi-year span: average the length of each spanned calendar year.
        double total = 0;
        for (int y = start.Year; y <= end.Year; y++)
            total += DateTime.IsLeapYear(y) ? 366.0 : 365.0;
        return total / (end.Year - start.Year + 1);
    }

    // DAYS360 US method: applies ONLY the day-31 reductions; does NOT fold in the
    // IsExcelNasdLastDayOfFebruary adjustment.  Excel's DAYS360(start,end,FALSE)
    // leaves February end-of-month dates alone — only the day-31 rule applies.
    // YEARFRAC basis-0 uses the full Days30US360 (with Feb-end adjustment) below.
    private static double Days30US360Days360(DateTime d1, DateTime d2, bool d1IsFakeLeapDay = false, bool d2IsFakeLeapDay = false)
    {
        int y1 = d1.Year, m1 = d1.Month, dd1 = d1IsFakeLeapDay ? 29 : d1.Day;
        int y2 = d2.Year, m2 = d2.Month, dd2 = d2IsFakeLeapDay ? 29 : d2.Day;
        if (dd1 == 31) dd1 = 30;
        if (dd2 == 31 && dd1 == 30) dd2 = 30;
        return 360.0 * (y2 - y1) + 30.0 * (m2 - m1) + (dd2 - dd1);
    }

    private static double Days30US360(DateTime d1, DateTime d2, bool d1IsFakeLeapDay = false, bool d2IsFakeLeapDay = false)
    {
        int y1 = d1.Year, m1 = d1.Month, dd1 = d1IsFakeLeapDay ? 29 : d1.Day;
        int y2 = d2.Year, m2 = d2.Month, dd2 = d2IsFakeLeapDay ? 29 : d2.Day;
        if (IsExcelNasdLastDayOfFebruary(d1)) dd1 = 30;
        if (IsExcelNasdLastDayOfFebruary(d2) && dd1 == 30) dd2 = 30;
        if (dd1 == 31) dd1 = 30;
        if (dd2 == 31 && dd1 == 30) dd2 = 30;
        return 360.0 * (y2 - y1) + 30.0 * (m2 - m1) + (dd2 - dd1);
    }

    private static bool IsExcelNasdLastDayOfFebruary(DateTime date) =>
        date.Year != 1900 &&
        date.Month == 2 &&
        date.Day == DateTime.DaysInMonth(date.Year, 2);

    private static double Days30E360(DateTime d1, DateTime d2, bool d1IsFakeLeapDay = false, bool d2IsFakeLeapDay = false)
    {
        int y1 = d1.Year, m1 = d1.Month, dd1 = d1IsFakeLeapDay ? 29 : d1.Day;
        int y2 = d2.Year, m2 = d2.Month, dd2 = d2IsFakeLeapDay ? 29 : d2.Day;
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

    private static int ExcelDowToMonIndex(DateTime date, bool uses1904DateSystem = false)
    {
        if (uses1904DateSystem)
            return DowToMonIndex(date.DayOfWeek);

        int serial = (int)Math.Floor(DateToSerial(date));
        return ExcelDowToMonIndex(serial);
    }

    private static int ExcelDowToMonIndex(int serial) => ((serial + 5) % 7 + 7) % 7;

    // Weekday from a raw Excel serial rather than a (possibly already-collapsed) DateTime.
    // For the 1900 date system this is pure modular arithmetic on the serial itself, so it
    // correctly distinguishes the phantom leap-day serial 60 ("1900-02-29") from serial 59
    // ("1900-02-28") even though both map to the same real DateTime — unlike going through
    // ExcelDowToMonIndex(DateTime, bool), which re-derives the serial from that DateTime and
    // so cannot tell 59 and 60 apart.
    private static int ExcelDowToMonIndex(double serial, bool uses1904DateSystem) =>
        uses1904DateSystem
            ? DowToMonIndex(SerialToDate(serial, true).DayOfWeek)
            : ExcelDowToMonIndex((int)Math.Floor(serial));

    private static int ExcelIsoWeeknum(DateTime date, bool uses1904DateSystem = false)
    {
        int serial = (int)Math.Floor(DateToSerial(date, uses1904DateSystem));
        int dowMon0 = uses1904DateSystem ? DowToMonIndex(date.DayOfWeek) : ExcelDowToMonIndex(serial);
        int thursdaySerial = serial + (3 - dowMon0);
        int weekYear = SerialToDate(thursdaySerial, uses1904DateSystem).Year;
        int jan4Serial = (int)Math.Floor(DateToSerial(new DateTime(weekYear, 1, 4), uses1904DateSystem));
        int week1MondaySerial = jan4Serial - (uses1904DateSystem
            ? DowToMonIndex(new DateTime(weekYear, 1, 4).DayOfWeek)
            : ExcelDowToMonIndex(jan4Serial));
        return (serial - week1MondaySerial) / 7 + 1;
    }

    // Holidays are keyed by the raw floored Excel serial, NOT by the resolved DateTime:
    // ExcelDateSystem.SerialToDate collapses the 1900 phantom leap day (serial 60,
    // "1900-02-29") onto the same real DateTime as serial 59 ("1900-02-28"), so storing
    // (and later looking up) by DateTime would make a holiday specified at either serial
    // indistinguishable from — and silently suppress — the other. Keying by serial instead
    // matches how the candidate-day loops in WorkdayScalar/NetworkdaysScalar/*Intl already
    // walk in serial space rather than DateTime space.
    private static bool TryCollectHolidays(ScalarValue? arg, bool uses1904DateSystem, out HashSet<double> holidays, out ErrorValue? error)
    {
        holidays = new HashSet<double>();
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
                    if (!TrySerialToDateTime(new NumberValue(serial), uses1904DateSystem, out _))
                    {
                        error = ErrorValue.Num;
                        return false;
                    }
                    holidays.Add(Math.Floor(serial));
                }
            }
        }
        else if (arg is not null && TryHolidayScalarNumber(arg, out double s))
        {
            if (!TrySerialToDateTime(new NumberValue(s), uses1904DateSystem, out _))
            {
                error = ErrorValue.Num;
                return false;
            }
            holidays.Add(Math.Floor(s));
        }
        return true;
    }

    // A single (non-range) holiday argument is coerced the same way the start/end/days
    // scalar arguments already are (see ToNumber): a text-literal or text-cell date such as
    // "1/2/2024" must parse to its serial number rather than being silently dropped. This
    // intentionally does NOT extend to text cells inside a holidays *range* above — matching
    // how ranges elsewhere (e.g. SUM) ignore text cells while a direct scalar argument coerces.
    private static bool TryHolidayScalarNumber(ScalarValue value, out double number)
    {
        if (TryCellNumber(value, out number)) return true;
        if (value is DirectTextLiteralValue or TextValue)
            return ExcelTextNumberParser.TryParse(ToText(value), out number);
        return false;
    }
}
