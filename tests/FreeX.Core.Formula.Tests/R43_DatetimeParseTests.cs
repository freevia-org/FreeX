using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// Round-43 findings R43-formula-datetime-parse-2-1..4: DATEVALUE/TIMEVALUE free-form text
// parsing diverged from real Excel in four ways:
//  - DATEVALUE always resolved ambiguous slash dates with US month/day order regardless of the
//    current locale's actual short-date order (2-1).
//  - TIMEVALUE returned #VALUE! for a date-only string instead of Excel's 0 (2-2).
//  - TIMEVALUE rejected an elapsed time >= 24 hours instead of taking the time-of-day fraction
//    mod 1 day (2-3).
//  - DATEVALUE returned #NUM! instead of Excel's documented #VALUE! for a syntactically valid
//    date_text before the 1900 epoch floor (2-4).
public sealed class R43_DatetimeParseTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet Sheet() => new(SheetId.New(), "S");

    // ── R43-formula-datetime-parse-2-1: DATEVALUE locale-dependent ambiguous date order ──────

    [Fact]
    public void Datevalue_EnGbLocale_ResolvesAmbiguousDateWithDayFirstOrder()
    {
        using var culture = new TestCultureScope("en-GB");

        // en-GB's short-date order is day/month/year, so "03/04/2024" is 3-Apr-2024, not
        // 4-Mar-2024 (the US month/day order the old Invariant-culture parse always produced).
        var expected = new DateTime(2024, 4, 3).ToOADate();
        _eval.Evaluate("=DATEVALUE(\"03/04/2024\")", Sheet())
            .Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void Datevalue_EnUsLocale_StillResolvesAmbiguousDateWithMonthFirstOrder()
    {
        // Sibling no-regression case: en-US's short-date order is month/day/year, so the same
        // ambiguous text resolves the other way - this must keep working under the new
        // CurrentCulture-based parse just as it did under the old Invariant-culture parse.
        using var culture = new TestCultureScope("en-US");

        var expected = new DateTime(2024, 3, 4).ToOADate();
        _eval.Evaluate("=DATEVALUE(\"03/04/2024\")", Sheet())
            .Should().Be(new NumberValue(expected));
    }

    // ── R43-formula-datetime-parse-2-2: TIMEVALUE on a date-only string ──────────────────────

    [Fact]
    public void Timevalue_DateOnlyString_ReturnsZero()
    {
        // TIMEVALUE ignores any date portion and returns only the time-of-day fraction, which
        // is 0 (midnight) when the text has no time component at all.
        ((NumberValue)_eval.Evaluate("=TIMEVALUE(\"8/22/2011\")", Sheet())).Value
            .Should().Be(0);
    }

    [Fact]
    public void Timevalue_PlainNonDateNonTimeText_StillReturnsValueError()
    {
        // Sibling no-regression case: text with neither a date component nor a time component
        // must still be rejected as #VALUE!, not silently coerced to 0.
        _eval.Evaluate("=TIMEVALUE(\"hello world\")", Sheet())
            .Should().Be(ErrorValue.Value);
    }

    // ── R43-formula-datetime-parse-2-3: TIMEVALUE on an elapsed time >= 24 hours ─────────────

    [Fact]
    public void Timevalue_ElapsedTimeOver24Hours_ReturnsFractionModOneDay()
    {
        // "36:00:00" is 1 day 12:00:00 elapsed; Excel strips the day component and returns the
        // 12-hour fraction (0.5), not #VALUE!.
        ((NumberValue)_eval.Evaluate("=TIMEVALUE(\"36:00:00\")", Sheet())).Value
            .Should().BeApproximately(0.5, 1e-10);

        // "25:30:00" is 1 day 01:30:00 elapsed -> 1:30:00 fraction = 0.0625.
        ((NumberValue)_eval.Evaluate("=TIMEVALUE(\"25:30:00\")", Sheet())).Value
            .Should().BeApproximately(0.0625, 1e-10);
    }

    [Fact]
    public void Timevalue_ElapsedTimeUnder24Hours_StillWorks()
    {
        // Sibling no-regression case: an elapsed time already under 24 hours must keep
        // resolving the same way it did before the mod-1-day fix.
        ((NumberValue)_eval.Evaluate("=TIMEVALUE(\"01:30:00\")", Sheet())).Value
            .Should().BeApproximately(0.0625, 1e-10);
    }

    // ── R43-formula-datetime-parse-2-4: DATEVALUE before the 1900 epoch floor ────────────────

    [Fact]
    public void Datevalue_TextDateBeforeEpoch_ReturnsValueErrorNotNumError()
    {
        // Microsoft's documented DATEVALUE behavior: a date_text outside the representable
        // range of the current date base returns #VALUE!, not #NUM!.
        _eval.Evaluate("=DATEVALUE(\"12/31/1899\")", Sheet())
            .Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Datevalue_TextDateAtEpoch_StillReturnsSerialOne()
    {
        // Sibling no-regression case: the earliest representable Excel date (1900-01-01,
        // serial 1) must still resolve to a number, not an error.
        _eval.Evaluate("=DATEVALUE(\"1/1/1900\")", Sheet())
            .Should().Be(new NumberValue(1));
    }
}
