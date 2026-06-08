using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class GoalSeekRequestParserTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Fact]
    public void Parse_AcceptsTrimmedCellsAndCurrentCultureNumber()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("fr-FR");

        var result = GoalSeekRequestParser.Parse(
            SheetId,
            " B2 ",
            "12,5",
            " A1 ");

        result.Success.Should().BeTrue();
        result.Error.Should().Be(GoalSeekRequestParseError.None);
        result.InvalidText.Should().BeEmpty();
        result.Request.Should().Be(new GoalSeekRequest(
            new CellAddress(SheetId, 2, 2),
            12.5,
            new CellAddress(SheetId, 1, 1)));
    }

    [Fact]
    public void Parse_FallsBackToInvariantNumber()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("fr-FR");

        var result = GoalSeekRequestParser.Parse(
            SheetId,
            "B2",
            "12.5",
            "A1");

        result.Request.Should().NotBeNull();
        result.Request!.TargetValue.Should().Be(12.5);
    }

    [Theory]
    [InlineData(null, "1", "A1", GoalSeekRequestParseError.SetCellRequired, "")]
    [InlineData("", "1", "A1", GoalSeekRequestParseError.SetCellRequired, "")]
    [InlineData("bad", "1", "A1", GoalSeekRequestParseError.InvalidSetCellAddress, "bad")]
    [InlineData("B2", null, "A1", GoalSeekRequestParseError.InvalidTargetValue, "")]
    [InlineData("B2", "not-number", "A1", GoalSeekRequestParseError.InvalidTargetValue, "not-number")]
    [InlineData("B2", "NaN", "A1", GoalSeekRequestParseError.InvalidTargetValue, "NaN")]
    [InlineData("B2", "Infinity", "A1", GoalSeekRequestParseError.InvalidTargetValue, "Infinity")]
    [InlineData("B2", "1", null, GoalSeekRequestParseError.ChangingCellRequired, "")]
    [InlineData("B2", "1", "", GoalSeekRequestParseError.ChangingCellRequired, "")]
    [InlineData("B2", "1", "bad", GoalSeekRequestParseError.InvalidChangingCellAddress, "bad")]
    [InlineData("B2", "1", "B2", GoalSeekRequestParseError.CellsMustDiffer, "")]
    public void Parse_RejectsInvalidInputsWithPortableError(
        string? setCellText,
        string? targetValueText,
        string? changingCellText,
        GoalSeekRequestParseError expectedError,
        string expectedInvalidText)
    {
        var result = GoalSeekRequestParser.Parse(
            SheetId,
            setCellText,
            targetValueText,
            changingCellText);

        result.Success.Should().BeFalse();
        result.Request.Should().BeNull();
        result.Error.Should().Be(expectedError);
        result.InvalidText.Should().Be(expectedInvalidText);
    }

    [Fact]
    public void TryParse_ReturnsRequestAndParseResult()
    {
        GoalSeekRequestParser.TryParse(
                SheetId,
                "C3",
                "42",
                "A1",
                out var request,
                out var result)
            .Should().BeTrue();

        result.Success.Should().BeTrue();
        request.Should().Be(result.Request);
    }
}
