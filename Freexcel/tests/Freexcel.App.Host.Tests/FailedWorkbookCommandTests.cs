using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class FailedWorkbookCommandTests
{
    [Fact]
    public void Apply_ReturnsFailedOutcomeWithMessage()
    {
        var command = new FailedWorkbookCommand("Sheet not found.");
        var context = new TestCommandContext(new Workbook());

        var outcome = command.Apply(context);

        command.Label.Should().Be("Unavailable");
        outcome.Should().Be(new CommandOutcome(false, "Sheet not found."));
    }

    [Fact]
    public void Revert_DoesNotMutateWorkbook()
    {
        var workbook = new Workbook();
        var command = new FailedWorkbookCommand("Command failed.");
        var context = new TestCommandContext(workbook);

        command.Revert(context);

        context.Workbook.Should().BeSameAs(workbook);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.Sheets.First(sheet => sheet.Id.Equals(sheetId));
    }
}
