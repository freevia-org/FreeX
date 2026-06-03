using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    // DATE / TIME

    [Fact]
    public void Date_ConstructsSerial()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=DATE(2024,1,15)", sheet);
        result.Should().BeOfType<NumberValue>();
        var dt = DateTime.FromOADate(((NumberValue)result).Value);
        dt.Year.Should().Be(2024);
        dt.Month.Should().Be(1);
        dt.Day.Should().Be(15);
    }

    [Fact]
    public void Date_NormalizesOverflowMonth()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=DATE(2024,13,1)", sheet);
        result.Should().BeOfType<NumberValue>();
        var dt = DateTime.FromOADate(((NumberValue)result).Value);
        dt.Year.Should().Be(2025);
        dt.Month.Should().Be(1);
        dt.Day.Should().Be(1);
    }

    [Fact]
    public void Date_NormalizesDayZeroToPreviousMonth()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=DATE(2024,3,0)", sheet);
        result.Should().BeOfType<NumberValue>();
        var dt = DateTime.FromOADate(((NumberValue)result).Value);
        dt.Year.Should().Be(2024);
        dt.Month.Should().Be(2);
        dt.Day.Should().Be(29);
    }

    [Fact]
    public void Date_YearLessThan1900_Adds1900()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=DATE(24,1,1)", sheet);
        result.Should().BeOfType<NumberValue>();
        var dt = DateTime.FromOADate(((NumberValue)result).Value);
        dt.Year.Should().Be(1924);
        dt.Month.Should().Be(1);
        dt.Day.Should().Be(1);
    }

    [Fact]
    public void Date_NonFiniteYear_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=DATE(A1,1,1)", sheet).Should().Be(ErrorValue.Num);
    }

    // ── YEAR / MONTH / DAY ────────────────────────────────────────────────────

    [Fact]
    public void Year_ExtractsYear()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 6, 15).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=YEAR(A1)", sheet).Should().Be(new NumberValue(2024));
    }

    [Fact]
    public void Month_ExtractsMonth()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 6, 15).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=MONTH(A1)", sheet).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Day_ExtractsDay()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 6, 15).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=DAY(A1)", sheet).Should().Be(new NumberValue(15));
    }

    [Fact]
    public void Year_DirectTodayResult_ExtractsCurrentYear()
    {
        _eval.Evaluate("=YEAR(TODAY())", MakeSheet()).Should().Be(new NumberValue(DateTime.Today.Year));
    }

    [Fact]
    public void Int_DirectTodayResult_ReturnsDateSerial()
    {
        _eval.Evaluate("=INT(TODAY())", MakeSheet()).Should().Be(new NumberValue(Math.Floor(DateTime.Today.ToOADate())));
    }

    // ── HOUR / MINUTE / SECOND ────────────────────────────────────────────────

    [Fact]
    public void Hour_ExtractsHour()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 1, 1, 14, 30, 45).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=HOUR(A1)", sheet).Should().Be(new NumberValue(14));
    }

    [Fact]
    public void Minute_ExtractsMinute()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 1, 1, 14, 30, 45).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=MINUTE(A1)", sheet).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Second_ExtractsSecond()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 1, 1, 14, 30, 45).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=SECOND(A1)", sheet).Should().Be(new NumberValue(45));
    }

    [Fact]
    public void DateTimeExtractors_RangeArgument_SpillElementwise()
    {
        var dates = MakeSheet(
            (1, 1, new NumberValue(new DateTime(2026, 5, 24).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2027, 6, 25).ToOADate())));
        AssertColumn(_eval.Evaluate("=YEAR(A1:A2)", dates), new NumberValue(2026), new NumberValue(2027));
        AssertColumn(_eval.Evaluate("=MONTH(A1:A2)", dates), new NumberValue(5), new NumberValue(6));
        AssertColumn(_eval.Evaluate("=DAY(A1:A2)", dates), new NumberValue(24), new NumberValue(25));

        var times = MakeSheet(
            (1, 1, new NumberValue(new TimeSpan(1, 2, 3).TotalDays)),
            (2, 1, new NumberValue(new TimeSpan(4, 5, 6).TotalDays)));
        AssertColumn(_eval.Evaluate("=HOUR(A1:A2)", times), new NumberValue(1), new NumberValue(4));
        AssertColumn(_eval.Evaluate("=MINUTE(A1:A2)", times), new NumberValue(2), new NumberValue(5));
        AssertColumn(_eval.Evaluate("=SECOND(A1:A2)", times), new NumberValue(3), new NumberValue(6));
    }

    [Fact]
    public void DateTimeScalarRangeArguments_SpillElementwise()
    {
        var dates = MakeSheet(
            (1, 1, new NumberValue(new DateTime(2024, 1, 7).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2024, 1, 8).ToOADate())));

        AssertColumn(_eval.Evaluate("=WEEKDAY(A1:A2,2)", dates), new NumberValue(7), new NumberValue(1));
        AssertColumn(_eval.Evaluate("=WEEKNUM(A1:A2,2)", dates), new NumberValue(1), new NumberValue(2));
        AssertColumn(_eval.Evaluate("=ISOWEEKNUM(A1:A2)", dates), new NumberValue(1), new NumberValue(2));

        var monthEnds = MakeSheet(
            (1, 1, new NumberValue(new DateTime(2024, 1, 31).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2024, 2, 29).ToOADate())));

        AssertColumn(
            _eval.Evaluate("=EDATE(A1:A2,1)", monthEnds),
            new NumberValue(new DateTime(2024, 2, 29).ToOADate()),
            new NumberValue(new DateTime(2024, 3, 29).ToOADate()));
        AssertColumn(
            _eval.Evaluate("=EOMONTH(A1:A2,1)", monthEnds),
            new NumberValue(new DateTime(2024, 2, 29).ToOADate()),
            new NumberValue(new DateTime(2024, 3, 31).ToOADate()));

        var textDates = MakeSheet(
            (1, 1, new TextValue("2024-01-07")),
            (2, 1, new TextValue("not a date")));
        AssertColumn(
            _eval.Evaluate("=DATEVALUE(A1:A2)", textDates),
            new NumberValue(new DateTime(2024, 1, 7).ToOADate()),
            ErrorValue.Value);

        var textTimes = MakeSheet(
            (1, 1, new TextValue("01:02:03")),
            (2, 1, new TextValue("not a time")));
        AssertColumn(
            _eval.Evaluate("=TIMEVALUE(A1:A2)", textTimes),
            new NumberValue(new TimeSpan(1, 2, 3).TotalDays),
            ErrorValue.Value);
    }

    [Fact]
    public void DateAndTime_MultipleSameShapeRangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2024)), (2, 1, new NumberValue(2025)),
            (1, 2, new NumberValue(1)),    (2, 2, new NumberValue(2)),
            (1, 3, new NumberValue(15)),   (2, 3, new NumberValue(20)),
            (1, 4, new NumberValue(1)),    (2, 4, new NumberValue(4)),
            (1, 5, new NumberValue(2)),    (2, 5, new NumberValue(5)),
            (1, 6, new NumberValue(3)),    (2, 6, new NumberValue(6)));

        AssertColumn(
            _eval.Evaluate("=DATE(A1:A2,B1:B2,C1:C2)", sheet),
            new NumberValue(new DateTime(2024, 1, 15).ToOADate()),
            new NumberValue(new DateTime(2025, 2, 20).ToOADate()));
        AssertColumn(
            _eval.Evaluate("=TIME(D1:D2,E1:E2,F1:F2)", sheet),
            new NumberValue(new TimeSpan(1, 2, 3).TotalDays),
            new NumberValue(new TimeSpan(4, 5, 6).TotalDays));
    }

    [Fact]
    public void DateAndTime_MismatchedRangeArgumentShapes_ReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2024)), (2, 1, new NumberValue(2025)),
            (1, 2, new NumberValue(1)),    (1, 3, new NumberValue(2)),
            (1, 4, new NumberValue(15)),   (2, 4, new NumberValue(20)));

        _eval.Evaluate("=DATE(A1:A2,B1:C1,D1:D2)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=TIME(A1:A2,B1:C1,D1:D2)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void DateDifferenceRangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(new DateTime(2024, 1, 1).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2024, 1, 2).ToOADate())),
            (1, 2, new NumberValue(new DateTime(2024, 1, 5).ToOADate())),
            (1, 3, new NumberValue(new DateTime(2024, 1, 31).ToOADate())));

        AssertColumn(_eval.Evaluate("=WORKDAY(A1:A2,1)", sheet),
            new NumberValue(new DateTime(2024, 1, 2).ToOADate()),
            new NumberValue(new DateTime(2024, 1, 3).ToOADate()));
        AssertColumn(_eval.Evaluate("=NETWORKDAYS(A1:A2,B1)", sheet), new NumberValue(5), new NumberValue(4));
        AssertColumn(_eval.Evaluate("=DAYS(B1,A1:A2)", sheet), new NumberValue(4), new NumberValue(3));
        AssertColumn(_eval.Evaluate("=DAYS360(A1:A2,C1)", sheet), new NumberValue(30), new NumberValue(29));
        AssertColumn(_eval.Evaluate("=YEARFRAC(A1:A2,C1,0)", sheet), new NumberValue(30.0 / 360.0), new NumberValue(29.0 / 360.0));
    }

    [Fact]
    public void DateDifference_SameShapeRangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(new DateTime(2024, 1, 1).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2024, 1, 2).ToOADate())),
            (1, 2, new NumberValue(new DateTime(2024, 1, 5).ToOADate())),
            (2, 2, new NumberValue(new DateTime(2024, 1, 10).ToOADate())),
            (1, 3, new NumberValue(0)),
            (2, 3, new NumberValue(3)),
            (1, 4, new NumberValue(0)),
            (2, 4, new NumberValue(1)));

        AssertColumn(_eval.Evaluate("=DAYS(B1:B2,A1:A2)", sheet), new NumberValue(4), new NumberValue(8));
        AssertColumn(_eval.Evaluate("=DAYS360(A1:A2,B1:B2)", sheet), new NumberValue(4), new NumberValue(8));
        AssertColumn(_eval.Evaluate("=DAYS360(A1:A2,B1:B2,D1:D2)", sheet), new NumberValue(4), new NumberValue(8));
        AssertColumn(_eval.Evaluate("=YEARFRAC(A1:A2,B1:B2,0)", sheet), new NumberValue(4.0 / 360.0), new NumberValue(8.0 / 360.0));
        AssertColumn(_eval.Evaluate("=YEARFRAC(A1:A2,B1:B2,C1:C2)", sheet), new NumberValue(4.0 / 360.0), new NumberValue(8.0 / 365.0));
        AssertColumn(_eval.Evaluate("=NETWORKDAYS(A1:A2,B1:B2)", sheet), new NumberValue(5), new NumberValue(7));
    }

    [Fact]
    public void DateDifference_MismatchedRangeArgumentShapes_ReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(new DateTime(2024, 1, 1).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2024, 1, 2).ToOADate())),
            (1, 2, new NumberValue(new DateTime(2024, 1, 5).ToOADate())),
            (1, 3, new NumberValue(new DateTime(2024, 1, 10).ToOADate())),
            (1, 4, new NumberValue(0)),
            (1, 5, new NumberValue(3)));

        _eval.Evaluate("=DAYS(B1:C1,A1:A2)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DAYS360(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DAYS360(A1:A2,B1,D1:E1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=YEARFRAC(A1:A2,B1:C1,0)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=YEARFRAC(A1:A2,B1,D1:E1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=NETWORKDAYS(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void DateTimeSecondRangeArguments_SpillElementwise()
    {
        var offsets = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)));

        AssertColumn(
            _eval.Evaluate("=EDATE(DATE(2024,1,31),A1:A2)", offsets),
            new NumberValue(new DateTime(2024, 2, 29).ToOADate()),
            new NumberValue(new DateTime(2024, 3, 31).ToOADate()));
        AssertColumn(
            _eval.Evaluate("=EOMONTH(DATE(2024,1,31),A1:A2)", offsets),
            new NumberValue(new DateTime(2024, 2, 29).ToOADate()),
            new NumberValue(new DateTime(2024, 3, 31).ToOADate()));
        AssertColumn(
            _eval.Evaluate("=WORKDAY(DATE(2024,1,1),A1:A2)", offsets),
            new NumberValue(new DateTime(2024, 1, 2).ToOADate()),
            new NumberValue(new DateTime(2024, 1, 3).ToOADate()));
        AssertColumn(_eval.Evaluate("=WEEKDAY(DATE(2024,1,7),A1:A2)", offsets), new NumberValue(1), new NumberValue(7));
    }

    [Fact]
    public void DateOffset_SameShapeRangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(new DateTime(2024, 1, 31).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2024, 2, 29).ToOADate())),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(2)));

        AssertColumn(
            _eval.Evaluate("=EDATE(A1:A2,B1:B2)", sheet),
            new NumberValue(new DateTime(2024, 2, 29).ToOADate()),
            new NumberValue(new DateTime(2024, 4, 29).ToOADate()));
        AssertColumn(
            _eval.Evaluate("=EOMONTH(A1:A2,B1:B2)", sheet),
            new NumberValue(new DateTime(2024, 2, 29).ToOADate()),
            new NumberValue(new DateTime(2024, 4, 30).ToOADate()));
        AssertColumn(
            _eval.Evaluate("=WORKDAY(A1:A2,B1:B2)", sheet),
            new NumberValue(new DateTime(2024, 2, 1).ToOADate()),
            new NumberValue(new DateTime(2024, 3, 4).ToOADate()));
        AssertColumn(_eval.Evaluate("=WEEKDAY(A1:A2,B1:B2)", sheet), new NumberValue(4), new NumberValue(4));
    }

    [Fact]
    public void DateOffset_MismatchedRangeArgumentShapes_ReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(new DateTime(2024, 1, 31).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2024, 2, 29).ToOADate())),
            (1, 2, new NumberValue(1)),
            (1, 3, new NumberValue(2)));

        _eval.Evaluate("=EDATE(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=EOMONTH(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=WORKDAY(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=WEEKDAY(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Weekday_ReturnType1_SundayIs1()
    {
        // 2024-01-07 is a Sunday
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 1, 7).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=WEEKDAY(A1,1)", sheet).Should().Be(new NumberValue(1)); // Sunday=1
    }

    [Fact]
    public void Weekday_OmittedReturnType_DefaultsToType1()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 1, 7).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));

        _eval.Evaluate("=WEEKDAY(A1,)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Weekday_ReturnType2_MondayIs1()
    {
        // 2024-01-08 is a Monday
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 1, 8).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=WEEKDAY(A1,2)", sheet).Should().Be(new NumberValue(1)); // Monday=1
    }

    [Fact]
    public void Weekday_ReturnType11_MondayIs1()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 1, 14).ToOADate(); // Sunday
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=WEEKDAY(A1,11)", sheet).Should().Be(new NumberValue(7));
    }

    [Fact]
    public void Weekday_InvalidReturnType_ReturnsNumError()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 1, 14).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=WEEKDAY(A1,99)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Weekday_NonFiniteReturnType_ReturnsNumError()
    {
        var serial = new DateTime(2024, 1, 14).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(serial)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=WEEKDAY(A1,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Weekday_SerialOutsideExcelDateRange_ReturnsNumError()
    {
        _eval.Evaluate("=WEEKDAY(2958466)", MakeSheet()).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=WEEKDAY(10000000000)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Weekday_ReturnTypeError_PropagatesError()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 1, 14).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=WEEKDAY(A1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Edate_AddMonths()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 1, 15).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        var result = _eval.Evaluate("=EDATE(A1,3)", sheet);
        result.Should().BeOfType<NumberValue>();
        var dt = DateTime.FromOADate(((NumberValue)result).Value);
        dt.Month.Should().Be(4);
        dt.Day.Should().Be(15);
    }

    [Fact]
    public void Edate_SubtractMonths()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 6, 15).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        var result = _eval.Evaluate("=EDATE(A1,-2)", sheet);
        result.Should().BeOfType<NumberValue>();
        var dt = DateTime.FromOADate(((NumberValue)result).Value);
        dt.Month.Should().Be(4);
    }

    [Fact]
    public void Edate_NonFiniteMonths_ReturnsNumError()
    {
        var serial = new DateTime(2024, 1, 15).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(serial)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=EDATE(A1,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Datedif_Days()
    {
        var sheet = MakeSheet();
        var s1 = new DateTime(2024, 1, 1).ToOADate();
        var s2 = new DateTime(2024, 1, 11).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(s1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(s2));
        _eval.Evaluate("=DATEDIF(A1,B1,\"D\")", sheet).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Datedif_Years()
    {
        var sheet = MakeSheet();
        var s1 = new DateTime(2020, 3, 15).ToOADate();
        var s2 = new DateTime(2024, 3, 15).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(s1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(s2));
        _eval.Evaluate("=DATEDIF(A1,B1,\"Y\")", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Datedif_Months()
    {
        var sheet = MakeSheet();
        var s1 = new DateTime(2024, 1, 1).ToOADate();
        var s2 = new DateTime(2024, 4, 1).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(s1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(s2));
        _eval.Evaluate("=DATEDIF(A1,B1,\"M\")", sheet).Should().Be(new NumberValue(3));
    }

    // ── MOD ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Datedif_SameShapeRangeArguments_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(new DateTime(2024, 1, 1).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2020, 3, 15).ToOADate())),
            (1, 2, new NumberValue(new DateTime(2024, 1, 11).ToOADate())),
            (2, 2, new NumberValue(new DateTime(2024, 3, 15).ToOADate())),
            (1, 3, new TextValue("D")),
            (2, 3, new TextValue("Y")));

        AssertColumn(_eval.Evaluate("=DATEDIF(A1:A2,B1:B2,C1:C2)", sheet), new NumberValue(10), new NumberValue(4));
    }

    [Fact]
    public void Datedif_MismatchedRangeArgument_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(new DateTime(2024, 1, 1).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2020, 3, 15).ToOADate())),
            (1, 2, new NumberValue(new DateTime(2024, 1, 11).ToOADate())),
            (1, 3, new NumberValue(new DateTime(2024, 3, 15).ToOADate())),
            (1, 4, new TextValue("D")),
            (1, 5, new TextValue("Y")));

        _eval.Evaluate("=DATEDIF(A1:A2,B1:C1,\"D\")", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DATEDIF(A1:A2,B1,D1:E1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Datedif_UnitError_PropagatesError()
    {
        var sheet = MakeSheet();
        var s1 = new DateTime(2024, 1, 1).ToOADate();
        var s2 = new DateTime(2024, 4, 1).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(s1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(s2));
        _eval.Evaluate("=DATEDIF(A1,B1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Datedif_DaysIgnoresTimePortion()
    {
        // DATEDIF must operate on whole-day boundaries — without truncation
        // the TimeSpan-based subtraction would return 0 days here even though
        // the dates differ by 1 calendar day.
        var sheet = MakeSheet();
        var s1 = new DateTime(2024, 1, 1, 23, 0, 0).ToOADate();
        var s2 = new DateTime(2024, 1, 2, 1, 0, 0).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(s1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(s2));
        _eval.Evaluate("=DATEDIF(A1,B1,\"D\")", sheet).Should().Be(new NumberValue(1));
    }

    // TIMEVALUE / DATEVALUE / WORKDAY / YEARFRAC

    [Fact] public void Time_HMS_ReturnsFraction()
    {
        // TIME(12, 0, 0) = 0.5 (half a day)
        ((NumberValue)_eval.Evaluate("=TIME(12,0,0)", MakeSheet())).Value
            .Should().BeApproximately(0.5, 1e-10);
    }

    [Fact] public void Time_NegativeHour_ReturnsNumError() =>
        _eval.Evaluate("=TIME(-1,0,0)", MakeSheet()).Should().Be(ErrorValue.Num);

    [Fact] public void Time_ArgumentAboveExcelLimit_ReturnsNumError() =>
        _eval.Evaluate("=TIME(32768,0,0)", MakeSheet()).Should().Be(ErrorValue.Num);

    [Fact] public void Timevalue_String_ReturnsFraction()
    {
        ((NumberValue)_eval.Evaluate("=TIMEVALUE(\"12:00:00\")", MakeSheet())).Value
            .Should().BeApproximately(0.5, 1e-10);
    }

    [Fact] public void Datevalue_String_ReturnsSerial()
    {
        // 2024-01-01 OADate
        double expected = new DateTime(2024, 1, 1).ToOADate();
        ((NumberValue)_eval.Evaluate("=DATEVALUE(\"2024-01-01\")", MakeSheet())).Value
            .Should().BeApproximately(expected, 1);
    }

    [Fact] public void Eomonth_Jan_ReturnsLastDayJan()
    {
        // DATE(2024,1,15) + EOMONTH offset 0 → 2024-01-31
        double jan15 = new DateTime(2024, 1, 15).ToOADate();
        double jan31 = new DateTime(2024, 1, 31).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(jan15)));
        ((NumberValue)_eval.Evaluate("=EOMONTH(A1,0)", sheet)).Value
            .Should().BeApproximately(jan31, 1);
    }

    [Fact]
    public void Eomonth_NonFiniteMonths_ReturnsNumError()
    {
        double jan15 = new DateTime(2024, 1, 15).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(jan15)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=EOMONTH(A1,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Weeknum_Jan8_Returns2()
    {
        double jan8 = new DateTime(2024, 1, 8).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(jan8)));
        _eval.Evaluate("=WEEKNUM(A1)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Weeknum_ReturnType2_UsesMondayStart()
    {
        double jan7 = new DateTime(2024, 1, 7).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(jan7)));
        _eval.Evaluate("=WEEKNUM(A1,2)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Weeknum_InvalidReturnType_ReturnsNumError()
    {
        double jan8 = new DateTime(2024, 1, 8).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(jan8)));
        _eval.Evaluate("=WEEKNUM(A1,99)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Weeknum_NonFiniteReturnType_ReturnsNumError()
    {
        double jan8 = new DateTime(2024, 1, 8).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(jan8)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=WEEKNUM(A1,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Isoweeknum_Jan8_2024_Returns2()
    {
        double jan8 = new DateTime(2024, 1, 8).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(jan8)));
        _eval.Evaluate("=ISOWEEKNUM(A1)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact] public void Workday_5BusinessDays_SkipsWeekend()
    {
        // 2024-01-08 (Monday) + 5 workdays = 2024-01-15 (Monday)
        double mon = new DateTime(2024, 1, 8).ToOADate();
        double expected = new DateTime(2024, 1, 15).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(mon)));
        ((NumberValue)_eval.Evaluate("=WORKDAY(A1,5)", sheet)).Value
            .Should().BeApproximately(expected, 1);
    }

    [Fact]
    public void Workday_NonFiniteDays_ReturnsNumError()
    {
        double mon = new DateTime(2024, 1, 8).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(mon)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=WORKDAY(A1,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Workday_ResultOutsideExcelDateRange_ReturnsNumError()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=WORKDAY(2958465,1)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=WORKDAY.INTL(2958465,1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Workday_DateTimeHolidayRange_SkipsHoliday()
    {
        double mon = new DateTime(2024, 1, 8).ToOADate();
        double expected = new DateTime(2024, 1, 16).ToOADate();
        var holiday = DateTimeValue.FromDateTime(new DateTime(2024, 1, 15));
        var sheet = MakeSheet(
            (1, 1, new NumberValue(mon)),
            (1, 2, holiday));

        ((NumberValue)_eval.Evaluate("=WORKDAY(A1,5,B1:B1)", sheet)).Value
            .Should().BeApproximately(expected, 1);
    }

    [Fact]
    public void Workday_DateTimeScalarHoliday_SkipsHoliday()
    {
        double mon = new DateTime(2024, 1, 8).ToOADate();
        double expected = new DateTime(2024, 1, 16).ToOADate();
        var holiday = DateTimeValue.FromDateTime(new DateTime(2024, 1, 15));
        var sheet = MakeSheet(
            (1, 1, new NumberValue(mon)),
            (1, 2, holiday));

        ((NumberValue)_eval.Evaluate("=WORKDAY(A1,5,B1)", sheet)).Value
            .Should().BeApproximately(expected, 1);

        ((NumberValue)_eval.Evaluate("=WORKDAY(A1,5,DATE(2024,1,15))", sheet)).Value
            .Should().BeApproximately(expected, 1);
    }

    [Fact]
    public void Workday_HolidaysError_PropagatesError()
    {
        double mon = new DateTime(2024, 1, 8).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(mon)));
        _eval.Evaluate("=WORKDAY(A1,5,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void WorkdayNetworkdays_InvalidHolidaySerial_ReturnsNumError()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=WORKDAY(DATE(2024,1,8),5,-1)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=NETWORKDAYS(DATE(2024,1,8),DATE(2024,1,12),-1)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=WORKDAY.INTL(DATE(2024,1,8),5,1,-1)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=NETWORKDAYS.INTL(DATE(2024,1,8),DATE(2024,1,12),1,-1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void WorkdayNetworkdays_HolidayRangeError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, ErrorValue.NA));

        _eval.Evaluate("=WORKDAY(DATE(2024,1,8),5,A1:A1)", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=NETWORKDAYS(DATE(2024,1,8),DATE(2024,1,12),A1:A1)", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=WORKDAY.INTL(DATE(2024,1,8),5,1,A1:A1)", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=NETWORKDAYS.INTL(DATE(2024,1,8),DATE(2024,1,12),1,A1:A1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Networkdays_MonToFri_Returns5()
    {
        double mon = new DateTime(2024, 1, 8).ToOADate();
        double fri = new DateTime(2024, 1, 12).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(mon)), (1, 2, new NumberValue(fri)));
        _eval.Evaluate("=NETWORKDAYS(A1,B1)", sheet).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Networkdays_DateTimeHolidayRange_ExcludesHoliday()
    {
        double mon = new DateTime(2024, 1, 8).ToOADate();
        double fri = new DateTime(2024, 1, 12).ToOADate();
        var holiday = DateTimeValue.FromDateTime(new DateTime(2024, 1, 10));
        var sheet = MakeSheet(
            (1, 1, new NumberValue(mon)),
            (1, 2, new NumberValue(fri)),
            (1, 3, holiday));

        _eval.Evaluate("=NETWORKDAYS(A1,B1,C1:C1)", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Networkdays_DateTimeScalarHoliday_ExcludesHoliday()
    {
        double mon = new DateTime(2024, 1, 8).ToOADate();
        double fri = new DateTime(2024, 1, 12).ToOADate();
        var holiday = DateTimeValue.FromDateTime(new DateTime(2024, 1, 10));
        var sheet = MakeSheet(
            (1, 1, new NumberValue(mon)),
            (1, 2, new NumberValue(fri)),
            (1, 3, holiday));

        _eval.Evaluate("=NETWORKDAYS(A1,B1,C1)", sheet).Should().Be(new NumberValue(4));
        _eval.Evaluate("=NETWORKDAYS(A1,B1,DATE(2024,1,10))", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Networkdays_Early1900Holiday_UsesExcelSerialCalendar()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));

        _eval.Evaluate("=NETWORKDAYS(DATE(1900,1,1),DATE(1900,1,1),A1:A1)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Networkdays_HolidaysError_PropagatesError()
    {
        double mon = new DateTime(2024, 1, 8).ToOADate();
        double fri = new DateTime(2024, 1, 12).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(mon)), (1, 2, new NumberValue(fri)));
        _eval.Evaluate("=NETWORKDAYS(A1,B1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Days_EndMinusStart_ReturnsDifference()
    {
        double d1 = new DateTime(2024, 1, 1).ToOADate();
        double d2 = new DateTime(2024, 1, 11).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(d2)), (1, 2, new NumberValue(d1)));
        _eval.Evaluate("=DAYS(A1,B1)", sheet).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Days360_MethodError_PropagatesError()
    {
        double jan1 = new DateTime(2024, 1, 1).ToOADate();
        double jul1 = new DateTime(2024, 7, 1).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(jan1)), (1, 2, new NumberValue(jul1)));
        _eval.Evaluate("=DAYS360(A1,B1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Yearfrac_HalfYear_ReturnsApprox05()
    {
        double jan1 = new DateTime(2024, 1, 1).ToOADate();
        double jul1 = new DateTime(2024, 7, 1).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(jan1)), (1, 2, new NumberValue(jul1)));
        ((NumberValue)_eval.Evaluate("=YEARFRAC(A1,B1,3)", sheet)).Value
            .Should().BeApproximately(182.0 / 365.0, 0.01);
    }

    [Fact]
    public void Yearfrac_InvalidBasis_ReturnsNumError()
    {
        double jan1 = new DateTime(2024, 1, 1).ToOADate();
        double jul1 = new DateTime(2024, 7, 1).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(jan1)), (1, 2, new NumberValue(jul1)));
        _eval.Evaluate("=YEARFRAC(A1,B1,99)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Yearfrac_NonFiniteBasis_ReturnsNumError()
    {
        double jan1 = new DateTime(2024, 1, 1).ToOADate();
        double jul1 = new DateTime(2024, 7, 1).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(jan1)), (1, 2, new NumberValue(jul1)), (1, 3, new TextValue("1E309")));
        _eval.Evaluate("=YEARFRAC(A1,B1,C1)", sheet).Should().Be(ErrorValue.Num);
    }

    // ── Statistical ──────────────────────────────────────────────────────────────

    // YEARFRAC BASIS EDGE CASES

    [Fact]
    public void Yearfrac_BasisError_PropagatesError()
    {
        double jan1 = new DateTime(2024, 1, 1).ToOADate();
        double jul1 = new DateTime(2024, 7, 1).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(jan1)), (1, 2, new NumberValue(jul1)));
        _eval.Evaluate("=YEARFRAC(A1,B1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Yearfrac_Basis1_ReversedRange_ReturnsFiniteNegative()
    {
        // Previously the actual/actual denominator loop did not execute when
        // start.Year > end.Year, returning 0 and causing divide-by-zero.
        double start = new DateTime(2024, 1, 1).ToOADate();
        double end   = new DateTime(2022, 1, 1).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(start)), (1, 2, new NumberValue(end)));
        var result = _eval.Evaluate("=YEARFRAC(A1,B1,1)", sheet);
        result.Should().BeOfType<NumberValue>();
        var value = ((NumberValue)result).Value;
        double.IsFinite(value).Should().BeTrue();
        value.Should().BeApproximately(-2.0, 0.05);
    }

    // TODAY / WORKDAY.INTL / NETWORKDAYS.INTL

    [Fact]
    public void Today_ReturnsCurrentDateSerialWithoutTime()
    {
        _eval.Evaluate("=TODAY()", MakeSheet())
            .Should()
            .Be(new DateTimeValue(Math.Floor(DateTime.Today.ToOADate())));
    }

    [Fact]
    public void WorkdayIntl_UsesWeekendMaskAndHolidays()
    {
        var holiday = DateTimeValue.FromDateTime(new DateTime(2026, 5, 20));
        var sheet = MakeSheet((1, 1, holiday));

        var result = _eval.Evaluate("=WORKDAY.INTL(DATE(2026,5,18),3,\"0000011\",A1:A1)", sheet);

        result.Should().Be(new NumberValue(new DateTime(2026, 5, 22).ToOADate()));
    }

    [Fact]
    public void WorkdayIntl_StartAndDaysRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(new DateTime(2026, 5, 18).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2026, 5, 19).ToOADate())),
            (1, 2, new NumberValue(3)),
            (2, 2, new NumberValue(-1)),
            (3, 2, new NumberValue(1)));

        AssertColumn(
            _eval.Evaluate("=WORKDAY.INTL(A1:A2,B1:B2,\"0000011\")", sheet),
            new NumberValue(new DateTime(2026, 5, 21).ToOADate()),
            new NumberValue(new DateTime(2026, 5, 18).ToOADate()));

        _eval.Evaluate("=WORKDAY.INTL(A1:A2,B1:B3,\"0000011\")", sheet)
            .Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void NetworkdaysIntl_UsesWeekendMaskAndHolidays()
    {
        var holiday = DateTimeValue.FromDateTime(new DateTime(2026, 5, 20));
        var sheet = MakeSheet((1, 1, holiday));

        _eval.Evaluate("=NETWORKDAYS.INTL(DATE(2026,5,18),DATE(2026,5,22),\"0000011\",A1:A1)", sheet)
            .Should().Be(new NumberValue(4));
    }

    // NETWORKDAYS.INTL RANGE ARGUMENTS

    [Fact]
    public void NetworkdaysIntl_StartAndEndRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(new DateTime(2026, 5, 18).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2026, 5, 22).ToOADate())),
            (1, 2, new NumberValue(new DateTime(2026, 5, 22).ToOADate())),
            (2, 2, new NumberValue(new DateTime(2026, 5, 18).ToOADate())),
            (3, 2, new NumberValue(new DateTime(2026, 5, 19).ToOADate())));

        AssertColumn(
            _eval.Evaluate("=NETWORKDAYS.INTL(A1:A2,B1:B2,\"0000011\")", sheet),
            new NumberValue(5),
            new NumberValue(-5));

        _eval.Evaluate("=NETWORKDAYS.INTL(A1:A2,B1:B3,\"0000011\")", sheet)
            .Should().Be(ErrorValue.Value);
    }

    // DATE WEEKEND MASKS

    [Fact]
    public void NetworkdaysIntl_SunOnlyWeekend_CountsSatAsWorkday()
    {
        // Mon May 18 .. Sun May 24 2026 with Sun-only weekend = 6 workdays (Mon..Sat)
        var sheet = MakeSheet();
        _eval.Evaluate("=NETWORKDAYS.INTL(DATE(2026,5,18),DATE(2026,5,24),11)", sheet)
            .Should().Be(new NumberValue(6));
    }

    [Fact]
    public void WorkdayIntl_WithStringPattern_Advances3DaysSkippingWeekend()
    {
        // From Mon May 18 2026 + 3 workdays with weekend Sat+Sun (pattern "0000011") = Thu May 21
        var sheet = MakeSheet();
        _eval.Evaluate("=WORKDAY.INTL(DATE(2026,5,18),3,\"0000011\")", sheet)
            .Should().Be(new NumberValue(new DateTime(2026, 5, 21).ToOADate()));
    }

    [Fact]
    public void NetworkdaysIntl_InvalidStringPattern_ReturnsValueError() =>
        _eval.Evaluate("=NETWORKDAYS.INTL(DATE(2026,5,18),DATE(2026,5,22),\"1234567\")", MakeSheet())
            .Should().Be(ErrorValue.Value);

}
