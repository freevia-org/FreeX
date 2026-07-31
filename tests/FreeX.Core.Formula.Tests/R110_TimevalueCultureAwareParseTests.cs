using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// Round-110 finding: TimevalueScalar's general DateTime.TryParse fallback hardcoded
// CultureInfo.InvariantCulture instead of the current-culture-aware parse (via
// CreateExcelTwoDigitYearCulture) that its sibling DatevalueScalar already uses. Real Excel
// resolves TIMEVALUE's date+time text per the system's regional short-date settings, same as
// DATEVALUE: under a day/month/year locale (e.g. en-GB) or a '.'-separated locale (e.g. de-DE),
// a date+time string that the hardcoded Invariant parse would reject (day-of-month > 12, or a
// '.' separator) must still resolve to the correct time-of-day fraction.
public sealed class R110_TimevalueCultureAwareParseTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet Sheet() => new(SheetId.New(), "S");

    [Fact]
    public void Timevalue_EnGbLocale_DayFirstDateWithDayOfMonthOver12_ResolvesTimeFraction()
    {
        // "14/3/2024 15:30" has day-of-month 14, which the old hardcoded-Invariant M/d/yyyy
        // parse cannot resolve at all (14 is not a valid month) -> it used to return #VALUE!.
        // Under en-GB's day/month/year short-date order this is unambiguously 14-Mar-2024
        // 15:30, and TIMEVALUE must return just the time-of-day fraction (15:30 -> 0.645833...).
        using var culture = new TestCultureScope("en-GB");

        var result = _eval.Evaluate("=TIMEVALUE(\"14/3/2024 15:30\")", Sheet());

        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(15.5 / 24.0, 1e-9);
    }

    [Fact]
    public void Timevalue_DeDeLocale_DotSeparatedDateWithTime_ResolvesTimeFraction()
    {
        // de-DE's short-date separator is '.', which the hardcoded-Invariant parse (expecting
        // '/' or '-') cannot recognize -> it used to return #VALUE!. Under de-DE this resolves
        // to 14-Mar-2024 15:30, and TIMEVALUE must return the 15:30 time-of-day fraction.
        using var culture = new TestCultureScope("de-DE");

        var result = _eval.Evaluate("=TIMEVALUE(\"14.3.2024 15:30\")", Sheet());

        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(15.5 / 24.0, 1e-9);
    }

    [Fact]
    public void Timevalue_EnUsLocale_SlashDateWithTime_StillResolvesTimeFraction()
    {
        // Sibling no-regression case: en-US's month/day/year short-date order must keep
        // resolving a slash-separated date+time string the same way it always did, now routed
        // through the current-culture-aware parse instead of the old hardcoded Invariant one.
        using var culture = new TestCultureScope("en-US");

        var result = _eval.Evaluate("=TIMEVALUE(\"3/14/2024 15:30\")", Sheet());

        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(15.5 / 24.0, 1e-9);
    }
}
