using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class DataValidationTests
{
    [Fact]
    public void GetInvalidEntryAction_ReturnsBlockForStopAlert()
    {
        var dv = new DataValidation { AlertStyle = DvAlertStyle.Stop, ShowErrorMessage = true };

        DataValidationService.GetInvalidEntryAction(dv)
            .Should().Be(DataValidationInvalidEntryAction.Block);
    }

    [Fact]
    public void GetInvalidEntryAction_ReturnsAllowForHiddenErrorAlert()
    {
        var dv = new DataValidation { AlertStyle = DvAlertStyle.Stop, ShowErrorMessage = false };

        DataValidationService.GetInvalidEntryAction(dv)
            .Should().Be(DataValidationInvalidEntryAction.Allow);
    }

    [Fact]
    public void Validate_WholeNumber_Between_AcceptsInRange()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(5));

        result.Should().BeNull("5 is between 1 and 10");
    }

    [Fact]
    public void Validate_WholeNumber_Between_RejectsOutOfRange()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(15));

        result.Should().NotBeNull("15 is outside the range 1-10");
    }

    [Fact]
    public void Validate_WholeNumber_RejectsDecimalValueInsideRange()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(5.5));

        result.Should().NotBeNull("Excel whole-number validation rejects decimal values even when they are in range");
    }

    [Fact]
    public void Validate_WholeNumber_GreaterThan_AcceptsLargerValue()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThan,
            Formula1 = "0",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(1));

        result.Should().BeNull("1 > 0");
    }

    [Fact]
    public void Validate_WholeNumber_GreaterThan_RejectsSmallerValue()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThan,
            Formula1 = "0",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(-1));

        result.Should().NotBeNull("-1 is not > 0");
    }

    // ─── TextLength validation ────────────────────────────────────────────────

    [Fact]
    public void Validate_TextLength_LessThan_AcceptsShortText()
    {
        var dv = new DataValidation
        {
            Type = DvType.TextLength,
            Operator = DvOperator.LessThan,
            Formula1 = "10",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Hi"));

        result.Should().BeNull("'Hi' has length 2 which is < 10");
    }

    [Fact]
    public void Validate_TextLength_LessThan_RejectsLongText()
    {
        var dv = new DataValidation
        {
            Type = DvType.TextLength,
            Operator = DvOperator.LessThan,
            Formula1 = "5",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Hello World"));

        result.Should().NotBeNull("'Hello World' has length 11 which is not < 5");
    }

    // ─── Any validation ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_Any_AlwaysAccepts()
    {
        var dv = new DataValidation { Type = DvType.Any };

        DataValidationService.Validate(dv, new NumberValue(99)).Should().BeNull();
        DataValidationService.Validate(dv, new TextValue("x")).Should().BeNull();
        DataValidationService.Validate(dv, BlankValue.Instance).Should().BeNull();
    }

    // ─── GetApplicable ────────────────────────────────────────────────────────

    [Fact]
    public void Validate_Decimal_Between_AcceptsInRange()
    {
        var dv = new DataValidation
        {
            Type = DvType.Decimal,
            Operator = DvOperator.Between,
            Formula1 = "0.5",
            Formula2 = "9.5",
            AllowBlank = true,
        };

        DataValidationService.Validate(dv, new NumberValue(5.0)).Should().BeNull("5.0 is between 0.5 and 9.5");
        DataValidationService.Validate(dv, new NumberValue(10.0)).Should().NotBeNull("10.0 is outside the range");
    }

    // ─── minimal test helpers ─────────────────────────────────────────────────

    [Fact]
    public void Validate_Date_Between_ParsesIsoDateBounds()
    {
        var dv = new DataValidation
        {
            Type = DvType.Date,
            Operator = DvOperator.Between,
            Formula1 = "2026-05-01",
            Formula2 = "2026-05-31",
            AllowBlank = true,
        };

        DataValidationService.Validate(dv, DateTimeValue.FromDateTime(new DateTime(2026, 5, 15)))
            .Should().BeNull("May 15 is within the May date validation window");
        DataValidationService.Validate(dv, DateTimeValue.FromDateTime(new DateTime(2026, 6, 1)))
            .Should().NotBeNull("June 1 is outside the May date validation window");
    }

    [Fact]
    public void Validate_Time_Between_ParsesClockTimeBounds()
    {
        var dv = new DataValidation
        {
            Type = DvType.Time,
            Operator = DvOperator.Between,
            Formula1 = "09:00",
            Formula2 = "17:30",
            AllowBlank = true,
        };

        DataValidationService.Validate(dv, new NumberValue(new TimeSpan(10, 30, 0).TotalDays))
            .Should().BeNull("10:30 is within the workday validation window");
        DataValidationService.Validate(dv, new NumberValue(new TimeSpan(18, 0, 0).TotalDays))
            .Should().NotBeNull("18:00 is outside the workday validation window");
    }
}
