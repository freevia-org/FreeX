using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R82-datetimevalue-1900-serial: a date literal typed into a cell must land on its true Excel
/// serial. Dates in 1900-01-01..1900-02-28 previously stored the .NET OLE Automation date, which is
/// one day higher than the Excel serial for that window (Excel reserves serial 60 for a fictitious
/// 1900-02-29), so typing "1/15/1900" stored serial 16 and the cell then rendered as 1/16/1900.
/// </summary>
public sealed class CellEntryParserEarly1900DateSerialTests
{
    private static readonly CellAddress Anchor = new(SheetId.New(), 2, 2);

    [Theory]
    [InlineData("1/1/1900", 1)]
    [InlineData("1/15/1900", 15)]
    [InlineData("2/28/1900", 59)]
    [InlineData("3/1/1900", 61)]
    [InlineData("1/15/2024", 45306)]
    public void CreateCell_StoresTypedDateLiteralsAsExcelSerials(string typed, double expectedSerial)
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell(typed, Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<DateTimeValue>().Which.Value.Should().Be(expectedSerial);
    }

    [Theory]
    [InlineData("1/15/1900", 1900, 1, 15)]
    [InlineData("2/28/1900", 1900, 2, 28)]
    public void CreateCell_RoundTripsTypedEarly1900DatesBackToTheSameCalendarDate(
        string typed, int year, int month, int day)
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell(typed, Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<DateTimeValue>()
            .Which.ToDateTime().Should().Be(new DateTime(year, month, day));
    }
}
