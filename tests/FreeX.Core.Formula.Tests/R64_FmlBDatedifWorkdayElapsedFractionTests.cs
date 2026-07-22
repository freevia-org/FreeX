using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-64 fml-b bucket regression tests (Core.Formula: date/time phantom-leap + number format):
///
/// R64-formula-datetime-intl-6-1: DATEDIF's M/Y/YM/MD/YD units read start.Day/end.Day from a
/// DateTime that collapses serial 60 (the 1900 phantom leap day) onto 1900-02-28, so they used
/// day=28 where Excel uses day=29. Fixed by substituting day 29 (via IsExcelFakeLeapDay) into the
/// day-of-month used by MonthDiff/YearDiff/DateDifYD/DateDifMD.
///
/// R64-formula-datetime-intl-6-2: WORKDAY(.INTL)/NETWORKDAYS(.INTL) walked/counted via real
/// DateTime.AddDays/subtraction, so a span crossing the serial 59/60/61 boundary miscounted by
/// one (DateTime collapses serial 60 onto the same real date as serial 59, so a single AddDays(1)
/// step from serial 59 lands straight on serial 61). Fixed by walking/counting in Excel-serial
/// space instead.
///
/// R64-app-number-format-6-1: FormatElapsedTime never zero-padded a doubled elapsed bracket letter
/// ([hh]/[mm]/[ss]) to its repeat-count width, so e.g. "05" rendered as "5". Fixed by padding the
/// lead unit to the bracket token's letter-repeat width.
///
/// R64-app-number-format-6-2 / 6-3: FormatSimpleFraction's whole-number part (1) never applied the
/// format's ',' thousands grouping, and (2) merged the '#' (suppress-if-zero) and '0'
/// (always-show) whole placeholders into one bool, so "0 ?/?" on 0.5 dropped the "0" digit. Fixed
/// by applying grouping to the whole part and distinguishing '0' vs '#' when the whole part is 0.
/// </summary>
public sealed class R64_FmlBDatedifWorkdayElapsedFractionTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    // --- R64-formula-datetime-intl-6-1: DATEDIF phantom leap day ---

    [Fact]
    public void Datedif_Ym_PhantomLeapDayStart_UsesDay29NotDay28()
    {
        // DATEDIF(60,DATE(1900,4,28),"YM"): start day-of-month must be treated as 29 (the
        // phantom 1900-02-29), so end.Day(28) < startDay(29) decrements the month diff by one:
        // (Apr-Feb)=2, minus 1 = 1. The pre-fix DateTime-collapsed start.Day(28) made
        // 28 < 28 false, so no decrement happened and the bug returned 2.
        _eval.Evaluate("=DATEDIF(60,DATE(1900,4,28),\"YM\")", MakeSheet())
            .Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Datedif_Md_PhantomLeapDayStart_UsesDay29NotDay28()
    {
        // DATEDIF(60,DATE(1900,3,15),"MD"): end.Day(15) < startDay(29) so it wraps:
        // 15 + daysInFeb1900(29) - 29 = 15. The pre-fix collapsed start.Day(28) gave
        // 15 + 29 - 28 = 16.
        _eval.Evaluate("=DATEDIF(60,DATE(1900,3,15),\"MD\")", MakeSheet())
            .Should().Be(new NumberValue(15));
    }

    [Fact]
    public void Datedif_NormalNonPhantomDates_UnaffectedByFix()
    {
        // Sibling no-regression case: ordinary (non-1900-phantom) dates must still compute
        // exactly as before for YM and MD.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(new DateTime(2023, 5, 10).ToOADate())),
            (1, 2, new NumberValue(new DateTime(2023, 8, 3).ToOADate())));

        _eval.Evaluate("=DATEDIF(A1,B1,\"YM\")", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=DATEDIF(A1,B1,\"MD\")", sheet).Should().Be(new NumberValue(24));
    }

    // --- R64-formula-datetime-intl-6-2: WORKDAY.INTL/NETWORKDAYS.INTL serial-60 boundary ---

    [Fact]
    public void WorkdayIntl_SpanCrossingPhantomLeapDayBoundary_ReturnsSerial61NotSerial62()
    {
        // Serials 58,59,60,61 are Mon,Tue,Wed,Thu in Excel's 1900 system; with no weekend mask
        // ("0000000") every one of the 3 steps from serial 58 must count, landing on serial 61.
        // The pre-fix DateTime-walk skipped serial 60 entirely and landed on 62.
        _eval.Evaluate("=WORKDAY.INTL(58,3,\"0000000\")", MakeSheet())
            .Should().Be(new NumberValue(61));
    }

    [Fact]
    public void NetworkdaysIntl_SpanCrossingPhantomLeapDayBoundary_ReturnsFourNotThree()
    {
        // Serials 58..61 inclusive = 4 distinct days; with no weekend mask all 4 count.
        // The pre-fix DateTime-subtraction undercounted this span as 3.
        _eval.Evaluate("=NETWORKDAYS.INTL(58,61,\"0000000\")", MakeSheet())
            .Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Workday_And_Networkdays_PlainVariants_SpanCrossingBoundary_AlsoFixed()
    {
        // Sibling coverage: the plain (non-.INTL) WORKDAY/NETWORKDAYS share the same
        // DateTime-walk bug for the default Sat/Sun weekend mask, since none of serials
        // 58..61 (Mon..Thu) fall on a weekend.
        _eval.Evaluate("=WORKDAY(58,3)", MakeSheet()).Should().Be(new NumberValue(61));
        _eval.Evaluate("=NETWORKDAYS(58,61)", MakeSheet()).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void WorkdayIntl_NetworkdaysIntl_NonBoundaryModernDateSpan_Unchanged()
    {
        // Sibling no-regression case: a modern-date (2024) span nowhere near the 1900 phantom
        // leap day must be unaffected by switching to serial-space walking/counting.
        double expected = new DateTime(2024, 1, 13).ToOADate();
        ((NumberValue)_eval.Evaluate("=WORKDAY.INTL(DATE(2024,1,8),5,\"0000000\")", MakeSheet())).Value
            .Should().BeApproximately(expected, 1);

        _eval.Evaluate("=NETWORKDAYS.INTL(DATE(2024,1,1),DATE(2024,1,10),\"0000000\")", MakeSheet())
            .Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Workday_Networkdays_PlainVariants_NonBoundaryModernDateSpan_Unchanged()
    {
        // Sibling no-regression case for the plain (default weekend mask) variants, matching
        // the already-covered R30 scenario.
        double expected = new DateTime(2024, 1, 15).ToOADate();
        ((NumberValue)_eval.Evaluate("=WORKDAY(DATE(2024,1,8),5)", MakeSheet())).Value
            .Should().BeApproximately(expected, 1);
    }

    // --- R64-app-number-format-6-1: doubled elapsed bracket letter zero-padding ---

    [Fact]
    public void ElapsedTimeFormat_DoubledHourBracket_ZeroPadsLeadUnit()
    {
        // TIME(5,3,0) with "[hh]:mm:ss" must render the lead hour as "05", not "5".
        _eval.Evaluate("=TEXT(TIME(5,3,0),\"[hh]:mm:ss\")", MakeSheet())
            .Should().Be(new TextValue("05:03:00"));
    }

    [Fact]
    public void ElapsedTimeFormat_SingleHourBracket_StaysUnpadded()
    {
        // Sibling no-regression case: a single-letter "[h]" must remain unpadded.
        _eval.Evaluate("=TEXT(TIME(5,3,0),\"[h]:mm:ss\")", MakeSheet())
            .Should().Be(new TextValue("5:03:00"));
    }

    [Fact]
    public void ElapsedTimeFormat_TwoDigitLeadHour_UnaffectedByPaddingFix()
    {
        // Sibling no-regression case: a lead value that already has >= 2 digits (36 hours ==
        // 1.5 days) must render unchanged with "[hh]:mm:ss".
        _eval.Evaluate("=TEXT(1.5,\"[hh]:mm:ss\")", MakeSheet())
            .Should().Be(new TextValue("36:00:00"));
    }

    // --- R64-app-number-format-6-2 / 6-3: fraction whole-part grouping + 0-vs-# ---

    [Fact]
    public void SimpleFraction_ThousandsGrouping_AppliesToWholePart()
    {
        NumberFormatter.Format(new NumberValue(12345.5), "#,##0 ?/?")
            .Should().Be("12,345 1/2");
    }

    [Fact]
    public void SimpleFraction_ZeroWholePlaceholder_ShowsZeroDigit()
    {
        NumberFormatter.Format(new NumberValue(0.5), "0 ?/?")
            .Should().Be("0 1/2");
    }

    [Fact]
    public void SimpleFraction_HashWholePlaceholder_StillSuppressesZeroDigit()
    {
        // Sibling no-regression case: '#' (suppress-if-zero) must keep suppressing the zero
        // whole digit, leaving only the separator space before the fraction.
        NumberFormatter.Format(new NumberValue(0.5), "# ?/?")
            .Should().Be(" 1/2");
    }

    [Fact]
    public void SimpleFraction_NormalFraction_UnaffectedByGroupingAndZeroFix()
    {
        // Sibling no-regression case: an ordinary non-grouped, non-zero-whole fraction must be
        // unaffected by either fix.
        NumberFormatter.Format(new NumberValue(3.25), "# ?/?")
            .Should().Be("3 1/4");
    }
}
