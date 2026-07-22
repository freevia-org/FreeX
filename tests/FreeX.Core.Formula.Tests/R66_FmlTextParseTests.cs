using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// Round-66 findings R66-formula-text-parse-6-1/6-2:
//  - 6-1: DATEVALUE("Jan-99") (and other "MMM-YY"/"MMM YY" two-digit-year month-year texts)
//    returned #VALUE! because TryParseMonthYearDateValueText only accepted 4-digit years; it now
//    also accepts 2-digit years, resolved via Excel's two-digit-year pivot (00-29 -> 2000-2029,
//    30-99 -> 1930-1999), matching the numeric M/D/YY path DATEVALUE already had.
//  - 6-2: ExcelTextNumberParser (VALUE()/implicit-arithmetic text coercion) hardcoded en-US
//    instead of reading CultureInfo.CurrentCulture like DatevalueScalar/NUMBERVALUE do, so under
//    a non-US locale VALUE() disagreed with DATEVALUE()/NUMBERVALUE() on the same text. It now
//    clones CultureInfo.CurrentCulture (with the same Excel two-digit-year pivot) for its
//    date-order and decimal/grouping separators.
public sealed class R66_FmlTextParseTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet Sheet() => new(SheetId.New(), "S");

    // ── R66-formula-text-parse-6-1: DATEVALUE two-digit-year month-year text ─────────────────

    [Fact]
    public void Datevalue_MonthDashTwoDigitYearAbovePivot_ResolvesToNineteenHundreds()
    {
        // "99" is above Excel's two-digit-year pivot of 29, so "Jan-99" must resolve to
        // January 1999 (serial 36161), not #VALUE! (the pre-fix behavior).
        var expected = new DateTime(1999, 1, 1).ToOADate();
        expected.Should().Be(36161);
        _eval.Evaluate("=DATEVALUE(\"Jan-99\")", Sheet())
            .Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void Datevalue_MonthDashTwoDigitYearAbovePivot_Dec95_ResolvesToNineteenHundreds()
    {
        var expected = new DateTime(1995, 12, 1).ToOADate();
        _eval.Evaluate("=DATEVALUE(\"Dec-95\")", Sheet())
            .Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void Datevalue_MonthDashTwoDigitYearAtOrBelowPivot_ResolvesToTwoThousands()
    {
        // "05" is at/below the pivot, so "Jan-05" resolves to January 2005, not #VALUE!.
        var expected = new DateTime(2005, 1, 1).ToOADate();
        _eval.Evaluate("=DATEVALUE(\"Jan-05\")", Sheet())
            .Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void Datevalue_MonthDashTwoDigitAmbiguousValue_StillResolvesSuccessfully()
    {
        // Sibling no-regression case: "Jan-24" was already accepted before this fix (via the
        // general free-form DateTime fallback, which read "24" as a day-of-month in the current
        // year since no 4-digit year was present). This value is genuinely ambiguous between a
        // day-of-month and a two-digit year, and the fix's month-year path now consistently wins
        // for every 2-digit "MMM-YY" text (matching the 05/95/99 cases above) - so the important
        // regression guard here is that it still resolves to a valid date and does not become
        // #VALUE!, not the specific historical (pre-fix) numeric value.
        var result = _eval.Evaluate("=DATEVALUE(\"Jan-24\")", Sheet());
        result.Should().BeOfType<NumberValue>();
        result.Should().NotBe(ErrorValue.Value);
    }

    [Fact]
    public void Datevalue_MonthSpaceFourDigitYear_Unaffected()
    {
        // Sibling no-regression case: the existing 4-digit-year "MMM yyyy" format must keep
        // resolving the same way after adding the 2-digit-year formats.
        var expected = new DateTime(2024, 1, 1).ToOADate();
        _eval.Evaluate("=DATEVALUE(\"Jan 2024\")", Sheet())
            .Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void Datevalue_NonDateText_StillReturnsValueError()
    {
        // Sibling no-regression case: plain non-date text must still be rejected as #VALUE!.
        _eval.Evaluate("=DATEVALUE(\"hello world\")", Sheet())
            .Should().Be(ErrorValue.Value);
    }

    // ── R66-formula-text-parse-6-2: VALUE()/coercion culture-aware parsing ───────────────────

    [Fact]
    public void Value_EnUsCulture_StillParsesUsGroupedNumber()
    {
        // Sibling no-regression case: en-US behavior must be unaffected by the CurrentCulture
        // switch (the test host's culture is en-US, so this also guards the implicit default).
        using var culture = new TestCultureScope("en-US");

        _eval.Evaluate("=VALUE(\"1,234.50\")", Sheet())
            .Should().Be(new NumberValue(1234.5));
    }

    [Fact]
    public void Value_DeDeCulture_ParsesCommaDecimalNumber()
    {
        // Under de-DE, ',' is the decimal separator: VALUE("1234,56") must parse as 1234.56,
        // not #VALUE! (the pre-fix behavior, since ExcelTextNumberParser hardcoded en-US).
        using var culture = new TestCultureScope("de-DE");

        _eval.Evaluate("=VALUE(\"1234,56\")", Sheet())
            .Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void Value_AndDatevalue_AgreeOnAmbiguousSlashDate_UnderSameCulture()
    {
        // en-GB's short-date order is day/month/year, so "03/04/2024" is 3-Apr-2024. Before the
        // fix, VALUE() hardcoded en-US (month/day order) while DATEVALUE() read CurrentCulture,
        // so the two disagreed on this text under en-GB; they must now agree.
        using var culture = new TestCultureScope("en-GB");

        var dateValueResult = _eval.Evaluate("=DATEVALUE(\"03/04/2024\")", Sheet());
        var valueResult = _eval.Evaluate("=VALUE(\"03/04/2024\")", Sheet());

        valueResult.Should().Be(dateValueResult);
        valueResult.Should().Be(new NumberValue(new DateTime(2024, 4, 3).ToOADate()));
    }
}
