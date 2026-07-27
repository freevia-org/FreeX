using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for the Q-calc-settings group (J58): iterative-calculation settings
/// (enable/max iterations/max change) are wired to a real undoable workbook command.
/// </summary>
public sealed class QCalcSettingsIterativeCalculationCommandTests
{
    [Theory]
    [InlineData(" 250 ", true, 250)]
    [InlineData("0", false, 0)]
    [InlineData("", false, 0)]
    public void CalculationOptionsInputParser_ValidatesMaxIterations(string text, bool expected, int value)
    {
        CalculationOptionsInputParser.TryParseMaxIterations(text, out var parsed).Should().Be(expected);
        parsed.Should().Be(value);
    }

    [Theory]
    [InlineData(" 0.0005 ", true, 0.0005)]
    [InlineData("-0.1", false, 0)]
    [InlineData("not-a-number", false, 0)]
    public void CalculationOptionsInputParser_ValidatesMaxChange(string text, bool expected, double value)
    {
        CalculationOptionsInputParser.TryParseMaxChange(text, out var parsed).Should().Be(expected);
        parsed.Should().Be(value);
    }

    [Fact]
    public void CalculationOptionsInputParser_DisabledIterationUsesFallbacksForInvalidBounds()
    {
        CalculationOptionsInputParser.TryParseBounds(
                iterativeCalculationEnabled: false,
                maxIterationsText: string.Empty,
                maxChangeText: "not-a-number",
                fallbackMaxIterations: 250,
                fallbackMaxChange: 0.0005,
                out var maxIterations,
                out var maxChange,
                out var error)
            .Should()
            .BeTrue();

        error.Should().Be(CalculationOptionsInputError.None);
        maxIterations.Should().Be(250);
        maxChange.Should().Be(0.0005);
    }

    [Theory]
    [InlineData("", "0.001", CalculationOptionsInputError.InvalidMaxIterations)]
    [InlineData("100", "invalid", CalculationOptionsInputError.InvalidMaxChange)]
    public void CalculationOptionsInputParser_EnabledIterationRequiresValidBounds(
        string maxIterationsText,
        string maxChangeText,
        CalculationOptionsInputError expectedError)
    {
        CalculationOptionsInputParser.TryParseBounds(
                iterativeCalculationEnabled: true,
                maxIterationsText,
                maxChangeText,
                fallbackMaxIterations: 250,
                fallbackMaxChange: 0.0005,
                out _,
                out _,
                out var error)
            .Should()
            .BeFalse();

        error.Should().Be(expectedError);
    }

    [Fact]
    public void SetIterativeCalculationOptionsCommand_SetsSettingsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var ctx = new TestCommandContext(wb);
        wb.IterativeCalculation = false;
        wb.MaxCalculationIterations = null;
        wb.MaxCalculationChange = null;

        var command = new SetIterativeCalculationOptionsCommand(true, 250, 0.0005);

        command.Apply(ctx).Success.Should().BeTrue();
        wb.IterativeCalculation.Should().BeTrue();
        wb.MaxCalculationIterations.Should().Be(250);
        wb.MaxCalculationChange.Should().Be(0.0005);

        command.Revert(ctx);

        wb.IterativeCalculation.Should().BeFalse();
        wb.MaxCalculationIterations.Should().BeNull();
        wb.MaxCalculationChange.Should().BeNull();
    }

    [Fact]
    public void SetIterativeCalculationOptionsCommand_RejectsNonPositiveMaxIterations()
    {
        var wb = new Workbook("test");
        var ctx = new TestCommandContext(wb);
        wb.IterativeCalculation = false;

        var outcome = new SetIterativeCalculationOptionsCommand(true, 0, 0.001).Apply(ctx);

        outcome.Success.Should().BeFalse();
        wb.IterativeCalculation.Should().BeFalse();
    }

    [Fact]
    public void SetIterativeCalculationOptionsCommand_RejectsNegativeMaxChange()
    {
        var wb = new Workbook("test");
        var ctx = new TestCommandContext(wb);
        wb.IterativeCalculation = false;

        var outcome = new SetIterativeCalculationOptionsCommand(true, 100, -0.5).Apply(ctx);

        outcome.Success.Should().BeFalse();
        wb.IterativeCalculation.Should().BeFalse();
    }

    [Fact]
    public void SetIterativeCalculationOptionsCommand_AllowsDisablingWithNullBounds()
    {
        var wb = new Workbook("test");
        var ctx = new TestCommandContext(wb);
        wb.IterativeCalculation = true;
        wb.MaxCalculationIterations = 100;
        wb.MaxCalculationChange = 0.001;

        var outcome = new SetIterativeCalculationOptionsCommand(false, null, null).Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.IterativeCalculation.Should().BeFalse();
        wb.MaxCalculationIterations.Should().BeNull();
        wb.MaxCalculationChange.Should().BeNull();
    }
}
