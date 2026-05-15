using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Model.Tests;

public sealed class CalculationOptionsCommandTests
{
    [Fact]
    public void SetCalculationModeCommand_SetsModeAndUndoRestores()
    {
        var wb = new Workbook("test");
        var ctx = new SimpleCtx(wb);
        wb.CalculationMode = WorkbookCalculationMode.Manual;

        var command = new SetCalculationModeCommand(WorkbookCalculationMode.Automatic);

        command.Apply(ctx).Success.Should().BeTrue();
        wb.CalculationMode.Should().Be(WorkbookCalculationMode.Automatic);

        command.Revert(ctx);

        wb.CalculationMode.Should().Be(WorkbookCalculationMode.Manual);
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
