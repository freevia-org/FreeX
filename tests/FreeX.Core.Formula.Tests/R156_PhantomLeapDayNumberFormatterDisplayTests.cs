using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// freex-number-precision F1: Excel's phantom 1900-02-29 (serial 60) has no real .NET DateTime
/// representation, so ExcelDateSystem.SerialToDate(60) collapses onto the same DateTime as
/// serial 59 ("1900-02-28") -- see Backlog_Serial60Tests, which documents that collision as
/// deliberately unchanged for day-count arithmetic. NumberFormatter fed that collided DateTime
/// straight to FormatDateTimeValue with no correction, so every date-shaped display of serial 60
/// (TEXT(), a cell formatted as a date, DATEVALUE round-tripped through TEXT()) rendered
/// "1900-02-28" instead of Excel's "1900-02-29" -- disagreeing with YEAR()/MONTH()/DAY(), which
/// already special-case this via BuiltInFunctions.Financial.cs's IsExcelFakeLeapDay.
/// </summary>
public sealed class R156_PhantomLeapDayNumberFormatterDisplayTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet Sheet() => new(SheetId.New(), "S");

    // ── The exact user gesture: TEXT(60, ...) must show Feb 29, not Feb 28 ──────────────────

    [Fact]
    public void Text_Serial60_YyyyMmDd_ShowsPhantomFeb29NotFeb28()
    {
        _eval.Evaluate("=TEXT(60,\"yyyy-mm-dd\")", Sheet()).Should().Be(new TextValue("1900-02-29"));
    }

    [Fact]
    public void Text_Serial60_DayOnly_ShowsTwentyNine()
    {
        _eval.Evaluate("=TEXT(60,\"dd\")", Sheet()).Should().Be(new TextValue("29"));
    }

    [Fact]
    public void Text_Serial60_AgreesWithYearMonthDayOnSameValue()
    {
        // Before the fix, this concatenation ("1900-2-29" via YEAR/MONTH/DAY) visibly disagreed
        // with TEXT()'s "1900-02-28" for the exact same underlying value.
        var yearMonthDay = _eval.Evaluate("=YEAR(60)&\"-\"&MONTH(60)&\"-\"&DAY(60)", Sheet());
        var text = _eval.Evaluate("=TEXT(60,\"yyyy-mm-dd\")", Sheet());

        yearMonthDay.Should().Be(new TextValue("1900-2-29"));
        text.Should().Be(new TextValue("1900-02-29"));
    }

    [Fact]
    public void Text_DatevalueRoundTrip_Serial60_MatchesWhatTheUserTyped()
    {
        var roundTripped = _eval.Evaluate(
            "=TEXT(DATEVALUE(\"2/29/1900\"),\"mm/dd/yyyy\")", Sheet());

        roundTripped.Should().Be(new TextValue("02/29/1900"));
    }

    // ── Direct NumberFormatter.Format coverage (matches Excel COM ground truth captured in
    //    TestData/ExcelNumberFormatMatrix.csv for DateSerial=60) ────────────────────────────

    [Theory]
    [InlineData("m/d/yyyy", "2/29/1900")]
    [InlineData("d-mmm-yy", "29-Feb-00")]
    [InlineData("m/d/yyyy h:mm", "2/29/1900 0:00")]
    [InlineData("mmmmm d yyyy", "F 29 1900")]
    public void Format_Serial60_MatchesExcelCapturedGroundTruth(string formatCode, string expected)
    {
        NumberFormatter.Format(new DateTimeValue(60.0), formatCode).Should().Be(expected);
    }

    // ── Multi-section / bracket-leading formats: a DateTimeValue(60) with a ';'-sectioned or
    //    '['-leading format bypasses the single-section fast path (TryFormatSimpleDateTime) and
    //    is dispatched through FormatDateTimeWithColor -> FormatDateTime in
    //    NumberFormatter.DateTime.cs instead -- a second call site that collapses onto the same
    //    DateTime and needed the identical correction. ──────────────────────────────────────

    [Fact]
    public void Format_Serial60_MultiSectionFormat_ShowsPhantomFeb29NotFeb28()
    {
        NumberFormatter.Format(new DateTimeValue(60.0), "m/d/yyyy;;;").Should().Be("2/29/1900");
    }

    [Fact]
    public void Format_Serial60_BracketColorLeadingFormat_ShowsPhantomFeb29NotFeb28()
    {
        NumberFormatter.Format(new DateTimeValue(60.0), "[Blue]d-mmm-yy").Should().Be("29-Feb-00");
    }

    // ── Sibling / no-regression coverage ─────────────────────────────────────────────────────

    [Fact]
    public void Format_Serial59_StillShowsFeb28_UnaffectedByFix()
    {
        // Sibling: the genuine, non-phantom neighbor day must keep rendering exactly as before.
        NumberFormatter.Format(new DateTimeValue(59.0), "yyyy-mm-dd").Should().Be("1900-02-28");
    }

    [Fact]
    public void Format_Serial59_MultiSectionFormat_StillShowsFeb28_UnaffectedByFix()
    {
        // Sibling: same non-phantom day through the multi-section/FormatDateTime call site.
        NumberFormatter.Format(new DateTimeValue(59.0), "m/d/yyyy;;;").Should().Be("2/28/1900");
    }

    [Fact]
    public void Format_Serial61_StillShowsMar1_UnaffectedByFix()
    {
        // Sibling: the day immediately after the phantom leap day must keep rendering unchanged.
        NumberFormatter.Format(new DateTimeValue(61.0), "yyyy-mm-dd").Should().Be("1900-03-01");
    }

    [Fact]
    public void Format_Serial60_WeekdayNameToken_AlreadyCorrect_StaysWednesday()
    {
        // Sibling: dddd/ddd (weekday name) already rendered correctly off the collided DateTime
        // (WeekdayScalar independently confirms serial 60 is Wednesday) -- the fix must not touch
        // that token, only the bare day-of-month digits.
        NumberFormatter.Format(new DateTimeValue(60.0), "dddd, mmmm d, yyyy")
            .Should().Be("Wednesday, February 29, 1900");
    }

    [Fact]
    public void Format_Serial60_1904DateSystem_UnaffectedByFix()
    {
        // Sibling: the 1904 date system has no phantom leap day at all -- its epoch is
        // 1904-01-01, so serial 60 is the ordinary date 1904-03-01 (Jan has 31 days, Feb 1904
        // is a real leap year with 29, 31+29=60). The correction must not fire under that date
        // system and this ordinary date must render unchanged.
        NumberFormatter.Format(new DateTimeValue(60.0), "yyyy-mm-dd", uses1904DateSystem: true)
            .Should().Be("1904-03-01");
    }

    [Fact]
    public void SerialToDate_Serials59And60_StillCollideOnSameDateTime_ArithmeticUnchanged()
    {
        // Sibling: ExcelDateSystem.SerialToDate itself (used by day-count arithmetic such as
        // SerialDayDifference) must stay exactly as Backlog_Serial60Tests pins it -- this fix
        // only changes what NumberFormatter displays, not the underlying serial<->DateTime
        // conversion.
        ExcelDateSystem.SerialToDate(59).Should().Be(new DateTime(1900, 2, 28));
        ExcelDateSystem.SerialToDate(60).Should().Be(new DateTime(1900, 2, 28));
    }
}
