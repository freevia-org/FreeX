using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R82-datetimevalue-1900-serial: <see cref="DateTimeValue"/> stores an EXCEL serial, not a .NET
/// OLE Automation date. Excel's 1900 calendar contains a fictitious 1900-02-29 (serial 60) that
/// OADate reserves no slot for, so every genuine date in 1900-01-01..1900-02-28 is one day later
/// in OADate space than its Excel serial. FromDateTime/ToDateTime must correct for that, otherwise
/// a date typed or loaded in that window renders and computes one day late (the formula engine and
/// the number formatter both read Value as a true Excel serial).
/// </summary>
public sealed class DateTimeValueExcelSerialTests
{
    [Theory]
    [InlineData(1900, 1, 1, 1)]
    [InlineData(1900, 1, 15, 15)]
    [InlineData(1900, 2, 28, 59)]
    // 1900-03-01 onward: OADate and the Excel serial agree again.
    [InlineData(1900, 3, 1, 61)]
    [InlineData(1900, 12, 31, 366)]
    [InlineData(2024, 1, 15, 45306)]
    public void FromDateTime_ProducesTheExcelSerial(int year, int month, int day, double expectedSerial)
    {
        DateTimeValue.FromDateTime(new DateTime(year, month, day)).Value.Should().Be(expectedSerial);
    }

    [Theory]
    [InlineData(1, 1900, 1, 1)]
    [InlineData(15, 1900, 1, 15)]
    [InlineData(59, 1900, 2, 28)]
    [InlineData(61, 1900, 3, 1)]
    [InlineData(45306, 2024, 1, 15)]
    public void ToDateTime_InterpretsValueAsAnExcelSerial(double serial, int year, int month, int day)
    {
        new DateTimeValue(serial).ToDateTime().Should().Be(new DateTime(year, month, day));
    }

    [Theory]
    [InlineData(1900, 1, 1)]
    [InlineData(1900, 1, 15)]
    [InlineData(1900, 2, 28)]
    [InlineData(1900, 3, 1)]
    [InlineData(2024, 2, 29)]
    public void FromDateTime_AndToDateTime_RoundTripAcrossThe1900Boundary(int year, int month, int day)
    {
        var date = new DateTime(year, month, day);

        DateTimeValue.FromDateTime(date).ToDateTime().Should().Be(date);
    }

    [Fact]
    public void FromDateTime_PreservesTheTimeOfDayWithinTheCorrectedWindow()
    {
        DateTimeValue.FromDateTime(new DateTime(1900, 1, 15, 6, 0, 0)).Value.Should().Be(15.25);
        new DateTimeValue(15.25).ToDateTime().Should().Be(new DateTime(1900, 1, 15, 6, 0, 0));
    }

    /// <summary>
    /// A serial below 1 carries no date part — it is a pure time of day (how the text/HTML readers
    /// store a bare "09:30:00"). Those stay on the plain OADate convention, whose 1899-12-30 zero
    /// point is the sentinel the matching writers use to recognize a time-only value, so the
    /// phantom-leap-day correction must NOT reach down into that band.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    public void ToDateTime_LeavesTimeOnlySerialsAnchoredToTheOADateZeroPoint(double serial)
    {
        var dateTime = new DateTimeValue(serial).ToDateTime();

        dateTime.Date.Should().Be(new DateTime(1899, 12, 30));
        dateTime.TimeOfDay.Should().Be(TimeSpan.FromDays(serial));
    }

    /// <summary>
    /// Serial 60 is Excel's phantom 1900-02-29, which no .NET DateTime can represent; it collapses
    /// onto 1900-02-28 (the same day serial 59 maps to). Pinned so the correction above is not
    /// mistaken for a fix for that separate, inherent case.
    /// </summary>
    [Fact]
    public void ToDateTime_CollapsesThePhantomLeapDayOntoFebruary28()
    {
        new DateTimeValue(60).ToDateTime().Should().Be(new DateTime(1900, 2, 28));
        DateTimeValue.FromDateTime(new DateTime(1900, 2, 28)).Value.Should().Be(59);
    }
}
