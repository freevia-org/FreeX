using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

public sealed class CalculationOptionsCommandTests
{
    [Fact]
    public void SetCalculationModeCommand_SetsModeAndUndoRestores()
    {
        var wb = new Workbook("test");
        var ctx = new TestCommandContext(wb);
        wb.CalculationMode = WorkbookCalculationMode.Manual;

        var command = new SetCalculationModeCommand(WorkbookCalculationMode.Automatic);

        command.Apply(ctx).Success.Should().BeTrue();
        wb.CalculationMode.Should().Be(WorkbookCalculationMode.Automatic);

        command.Revert(ctx);

        wb.CalculationMode.Should().Be(WorkbookCalculationMode.Manual);
    }

    [Fact]
    public void SetCalculationModeCommand_RejectsInvalidMode()
    {
        var wb = new Workbook("test");
        var ctx = new TestCommandContext(wb);
        wb.CalculationMode = WorkbookCalculationMode.Automatic;

        var outcome = new SetCalculationModeCommand((WorkbookCalculationMode)99).Apply(ctx);

        outcome.Success.Should().BeFalse();
        wb.CalculationMode.Should().Be(WorkbookCalculationMode.Automatic);
    }
}
