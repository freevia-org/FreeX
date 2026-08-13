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
    [InlineData("List", "Between", "=$A$1:$A$10000", "")]
    [InlineData("List", "Between", "=$A$1:$A$10001", "")]
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

    // ----- Culture-sensitive number parsing (R10 regression) -----

    [Fact]
    public void ValidateCriteriaInputs_DeDE_CommaDecimal_AcceptedAsDecimal()
    {
        // In de-DE, "3,5" is 3.5 (decimal comma). With NumberStyles.Any + InvariantCulture the comma
        // was treated as a thousands separator, silently accepting 35 as a whole number.
        using var scope = TestCultureScope.CurrentCulture("de-DE");

        DataValidationDialog.TryValidateCriteriaInputs(
                "Decimal",
                "GreaterThan",
                "3,5",
                "",
                out var error)
            .Should()
            .BeTrue("\"3,5\" is a valid decimal in de-DE (= 3.5)");

        error.Should().BeNull();
    }

    [Fact]
    public void ValidateCriteriaInputs_DeDE_CommaDecimal_RejectedAsWholeNumber()
    {
        // "3,5" in de-DE is 3.5 — a non-integer — so WholeNumber must reject it.
        // The bug would have silently accepted it (as 35, a whole number).
        using var scope = TestCultureScope.CurrentCulture("de-DE");

        DataValidationDialog.TryValidateCriteriaInputs(
                "WholeNumber",
                "GreaterThan",
                "3,5",
                "",
                out var error)
            .Should()
            .BeFalse("\"3,5\" in de-DE is 3.5, which is not a whole number");

        error.Should().Contain("Whole number");
    }

    [Fact]
    public void ValidateCriteriaInputs_EnglishDotDecimal_StillParsesCorrectly()
    {
        // Regression guard: English-format "3.5" must still parse as 3.5 via InvariantCulture fallback.
        DataValidationDialog.TryValidateCriteriaInputs(
                "Decimal",
                "GreaterThan",
                "3.5",
                "",
                out var error)
            .Should()
            .BeTrue("\"3.5\" is always a valid decimal via InvariantCulture fallback");

        error.Should().BeNull();
    }
}
