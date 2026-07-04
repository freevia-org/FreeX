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
