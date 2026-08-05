using FluentAssertions;
using FreeX.App.Presentation.ThemeUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ThemeUI;

public sealed class WorkbookThemeCommandPlannerTests
{
    [Fact]
    public void PlanApply_BuildsSharedUndoableThemeCommand()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        var theme = WorkbookThemeCatalog.FreeXColorfulThemePreset.CreateTheme();

        var plan = WorkbookThemeCommandPlanner.PlanApply(theme);
        var outcome = plan.Command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        plan.CommandLabel.Should().Be(WorkbookThemeCommandPlanner.CommandLabel);
        plan.Theme.Should().Be(theme);
        workbook.Theme.Should().Be(theme);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
