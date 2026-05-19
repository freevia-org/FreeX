using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class GoalSeekInputParserTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Fact]
    public void TryParse_AcceptsTrimmedCellsAndCurrentCultureOrInvariantNumber()
    {
        GoalSeekInputParser.TryParse(
                SheetId,
                " B2 ",
                "12.5",
                " A1 ",
                out var input,
                out var error)
            .Should().BeTrue();

        error.Should().BeEmpty();
        input.SetCell.Should().Be(new CellAddress(SheetId, 2, 2));
        input.TargetValue.Should().Be(12.5);
        input.ChangingCell.Should().Be(new CellAddress(SheetId, 1, 1));
    }

    [Theory]
    [InlineData("", "1", "A1", "Please enter the Set cell address.")]
    [InlineData("bad", "1", "A1", "'bad' is not a valid cell address.")]
    [InlineData("B2", "not-number", "A1", "'not-number' is not a valid number.")]
    [InlineData("B2", "NaN", "A1", "'NaN' is not a valid number.")]
    [InlineData("B2", "Infinity", "A1", "'Infinity' is not a valid number.")]
    [InlineData("B2", "1", "", "Please enter the By changing cell address.")]
    [InlineData("B2", "1", "bad", "'bad' is not a valid cell address.")]
    [InlineData("B2", "1", "B2", "The Set cell and the By changing cell must be different.")]
    public void TryParse_RejectsInvalidInputsWithDialogErrorText(
        string setCellText,
        string targetValueText,
        string changingCellText,
        string expectedError)
    {
        GoalSeekInputParser.TryParse(
                SheetId,
                setCellText,
                targetValueText,
                changingCellText,
                out _,
                out var error)
            .Should().BeFalse();

        error.Should().Be(expectedError);
    }
}
