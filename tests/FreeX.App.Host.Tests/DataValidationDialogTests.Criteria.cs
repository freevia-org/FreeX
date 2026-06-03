using FreeX.App.Host;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class DataValidationDialogTests
{
    [Theory]
    [InlineData("List", "Between", "", "", "Source")]
    [InlineData("Custom", "Between", "", "", "Formula")]
    [InlineData("WholeNumber", "Between", "1", "", "Maximum")]
    [InlineData("WholeNumber", "Equal", "", "", "Value")]
    public void ValidateCriteriaInputs_RejectsIncompleteValidationCriteria(
        string typeTag,
        string operatorTag,
        string formula1,
        string formula2,
        string expectedMessageFragment)
    {
        DataValidationDialog.TryValidateCriteriaInputs(
                typeTag,
                operatorTag,
                formula1,
                formula2,
                out var error)
            .Should()
            .BeFalse();

        error.Should().Contain(expectedMessageFragment);
    }

    [Fact]
    public void ValidateCriteriaInputs_AllowsAnyValueAndCompleteBetweenCriteria()
    {
        DataValidationDialog.TryValidateCriteriaInputs(
                "Any",
                "Between",
                "",
                "",
                out var anyError)
            .Should()
            .BeTrue();
        anyError.Should().BeNull();

        DataValidationDialog.TryValidateCriteriaInputs(
                "Decimal",
                "Between",
                "1.5",
                "2.5",
                out var betweenError)
            .Should()
            .BeTrue();
        betweenError.Should().BeNull();
    }

    [Theory]
    [InlineData("WholeNumber", "Equal", "1.5", "", "Whole number")]
    [InlineData("Decimal", "GreaterThan", "one", "", "Decimal")]
    [InlineData("Date", "Equal", "not-a-date", "", "Date")]
    [InlineData("Time", "Equal", "25:00", "", "Time")]
    [InlineData("TextLength", "Equal", "2.5", "", "Text length")]
    [InlineData("List", "Between", "=\"unterminated", "", "Source")]
    [InlineData("Custom", "Between", "=SUM(", "", "Formula")]
    [InlineData("WholeNumber", "Between", "1", "two", "Whole number")]
    public void ValidateCriteriaInputs_RejectsMalformedTypeSpecificCriteria(
        string typeTag,
        string operatorTag,
        string formula1,
        string formula2,
        string expectedMessageFragment)
    {
        DataValidationDialog.TryValidateCriteriaInputs(
                typeTag,
                operatorTag,
                formula1,
                formula2,
                out var error)
            .Should()
            .BeFalse();

        error.Should().Contain(expectedMessageFragment);
    }

    [Theory]
    [InlineData("WholeNumber", "Between", "1", "10")]
    [InlineData("WholeNumber", "Equal", "=A1", "")]
    [InlineData("Decimal", "GreaterThan", "-1.5", "")]
    [InlineData("Date", "Equal", "2026-05-01", "")]
    [InlineData("Time", "Equal", "09:30", "")]
    [InlineData("TextLength", "LessThanOrEqual", "12", "")]
    [InlineData("List", "Between", "Red,\"Blue, Green\"", "")]
    [InlineData("List", "Between", "=$A$1:$A$5", "")]
    [InlineData("Custom", "Between", "=MOD(A1,2)=0", "")]
    public void ValidateCriteriaInputs_AllowsWellFormedTypeSpecificCriteria(
        string typeTag,
        string operatorTag,
        string formula1,
        string formula2)
    {
        DataValidationDialog.TryValidateCriteriaInputs(
                typeTag,
                operatorTag,
                formula1,
                formula2,
                out var error)
            .Should()
            .BeTrue();

        error.Should().BeNull();
    }
}
