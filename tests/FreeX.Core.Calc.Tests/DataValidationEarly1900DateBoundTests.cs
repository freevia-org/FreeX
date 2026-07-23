using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R82-datetimevalue-1900-serial: a date-validation bound is parsed into a serial and compared
/// against the cell's raw stored serial, so both sides must use the Excel serial space. Parsing the
/// bound with a bare ToOADate() put 1900-01-01..1900-02-28 one day high, which would reject a cell
/// holding exactly the bound date once DateTimeValue started storing the true Excel serial.
/// </summary>
public sealed class DataValidationEarly1900DateBoundTests
{
    private static DataValidation Between(string from, string to) => new()
    {
        Type = DvType.Date,
        Operator = DvOperator.Between,
        Formula1 = from,
        Formula2 = to,
    };

    [Theory]
    [InlineData(1900, 1, 15)]
    [InlineData(1900, 1, 1)]
    [InlineData(1900, 2, 28)]
    [InlineData(1900, 3, 1)]
    [InlineData(2024, 1, 15)]
    public void Validate_Date_AcceptsACellHoldingExactlyTheBoundDate(int year, int month, int day)
    {
        var date = new DateTime(year, month, day);
        var dv = Between($"{month}/{day}/{year}", $"{month}/{day}/{year}");

        DataValidationService.Validate(dv, DateTimeValue.FromDateTime(date)).Should().BeNull();
    }

    [Fact]
    public void Validate_Date_RejectsACellOneDayOutsideAnEarly1900Bound()
    {
        var dv = Between("1/15/1900", "1/15/1900");

        DataValidationService.Validate(dv, DateTimeValue.FromDateTime(new DateTime(1900, 1, 16)))
            .Should().NotBeNull();
        DataValidationService.Validate(dv, DateTimeValue.FromDateTime(new DateTime(1900, 1, 14)))
            .Should().NotBeNull();
    }

    [Fact]
    public void Validate_Date_AcceptsTheFullEarly1900Range()
    {
        var dv = Between("1/1/1900", "2/28/1900");

        foreach (var day in new[] { 1, 15, 31 })
            DataValidationService.Validate(dv, DateTimeValue.FromDateTime(new DateTime(1900, 1, day)))
                .Should().BeNull($"1900-01-{day:00} is inside the range");

        DataValidationService.Validate(dv, DateTimeValue.FromDateTime(new DateTime(1900, 3, 1)))
            .Should().NotBeNull("1900-03-01 is past the upper bound");
    }
}
