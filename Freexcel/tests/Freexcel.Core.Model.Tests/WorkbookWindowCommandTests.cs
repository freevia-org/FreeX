using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Model.Tests;

public sealed class WorkbookWindowCommandTests
{
    [Fact]
    public void SetWorkbookWindowArrangementCommand_SetsArrangementAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var ctx = new SimpleCtx(workbook);
        workbook.WindowArrangement = WorkbookWindowArrangement.Horizontal;

        var command = new SetWorkbookWindowArrangementCommand(WorkbookWindowArrangement.Tiled);

        command.Apply(ctx).Success.Should().BeTrue();
        workbook.WindowArrangement.Should().Be(WorkbookWindowArrangement.Tiled);

        command.Revert(ctx);

        workbook.WindowArrangement.Should().Be(WorkbookWindowArrangement.Horizontal);
    }

    [Fact]
    public void SetWorkbookWindowArrangementCommand_RejectsInvalidArrangement()
    {
        var workbook = new Workbook("test");
        var ctx = new SimpleCtx(workbook);
        workbook.WindowArrangement = WorkbookWindowArrangement.Vertical;

        var outcome = new SetWorkbookWindowArrangementCommand((WorkbookWindowArrangement)99).Apply(ctx);

        outcome.Success.Should().BeFalse();
        workbook.WindowArrangement.Should().Be(WorkbookWindowArrangement.Vertical);
    }

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
