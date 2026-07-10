using FluentAssertions;
using FreeX.Core.Formula;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Backlog item serial-60: Excel's phantom 1900-02-29 (serial 60) has no DateTime
/// representation, so SerialToDate(59) and SerialToDate(60) collide on 1900-02-28.
/// These tests document that known, intentionally-unchanged collision, and verify the
/// new serial-space helpers (SerialDayDifference / IsPhantomLeapDaySerial) added to
/// ExcelDateSystem give correct Excel day-count semantics across the 59/60/61 boundary
/// without needing DateTime to represent the phantom day at all.
/// </summary>
public sealed class Backlog_serial_60_Tests
{
    [Fact]
    public void SerialToDate_Serials59And60_CollideOnSameDateTime()
    {
        // Documents the known, unavoidable .NET limitation: 1900 is not a leap year in
        // the real Gregorian calendar, so there is no DateTime for "1900-02-29".
        var date59 = ExcelDateSystem.SerialToDate(59);
        var date60 = ExcelDateSystem.SerialToDate(60);

        date59.Should().Be(new DateTime(1900, 2, 28));
        date60.Should().Be(new DateTime(1900, 2, 28));
        date59.Should().Be(date60);
    }

    [Fact]
    public void SerialDayDifference_AcrossPhantomLeapDayBoundary_MatchesExcelSerialArithmetic()
    {
        // Excel treats serial 61 ("1900-03-01") minus serial 59 ("1900-02-28") as a
        // 2-day span (it counts the phantom leap day, serial 60, in between).
        ExcelDateSystem.SerialDayDifference(59, 61).Should().Be(2);

        // Naive DateTime subtraction after SerialToDate loses that day, because serials
        // 59 and 60 collide onto the same DateTime (1900-02-28 -> 1900-03-01 = 1 day).
        var naiveDifference = (ExcelDateSystem.SerialToDate(61) - ExcelDateSystem.SerialToDate(59)).TotalDays;
        naiveDifference.Should().Be(1);
        naiveDifference.Should().NotBe(ExcelDateSystem.SerialDayDifference(59, 61));
    }

    [Theory]
    [InlineData(59, 60, 1)]
    [InlineData(60, 61, 1)]
    [InlineData(59, 61, 2)]
    [InlineData(1, 61, 60)]
    [InlineData(61, 59, -2)]
    public void SerialDayDifference_ReturnsPlainSerialSubtraction(double start, double end, double expected)
    {
        ExcelDateSystem.SerialDayDifference(start, end).Should().Be(expected);
    }

    [Theory]
    [InlineData(58, false)]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    [InlineData(59.5, false)]
    public void IsPhantomLeapDaySerial_OnlyTrueForSerial60(double serial, bool expected)
    {
        ExcelDateSystem.IsPhantomLeapDaySerial(serial).Should().Be(expected);
    }
}
