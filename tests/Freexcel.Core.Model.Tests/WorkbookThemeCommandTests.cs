using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public sealed class WorkbookThemeCommandTests
{
    [Fact]
    public void SetWorkbookThemeCommand_UpdatesThemeAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var ctx = new SimpleCtx(workbook);
        var theme = WorkbookTheme.Office
            .WithName("Freexcel Custom")
            .WithFonts("Aptos Display", "Aptos")
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(1, 2, 3));

        var command = new SetWorkbookThemeCommand(theme);

        command.Apply(ctx).Success.Should().BeTrue();
        workbook.Theme.Should().Be(theme);

        command.Revert(ctx);

        workbook.Theme.Should().Be(WorkbookTheme.Office);
    }

    [Fact]
    public void SetWorkbookThemeCommand_RejectsNullTheme()
    {
        var workbook = new Workbook("test");
        var ctx = new SimpleCtx(workbook);
        var command = new SetWorkbookThemeCommand(null);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Theme is required.");
        workbook.Theme.Should().Be(WorkbookTheme.Office);
    }

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.Sheets.First(sheet => sheet.Id == sheetId);
    }
}
