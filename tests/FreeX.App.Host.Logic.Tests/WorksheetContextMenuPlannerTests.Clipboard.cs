using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class WorksheetContextMenuPlannerTests
{
    [Fact]
    public void InsertCopiedCells_IsDisabledWithoutOutlinedCopy()
    {
        var command = FindInsertCopiedCells(WorksheetContextMenuState.Default);

        command.Header.Should().Be("Insert Copied Cells...");
        command.IsEnabled.Should().BeFalse("Excel greys Insert Copied Cells out until cells are copied");
    }

    [Fact]
    public void InsertCopiedCells_IsEnabledAfterCopy()
    {
        var command = FindInsertCopiedCells(
            WorksheetContextMenuState.Default with { Clipboard = WorksheetContextMenuClipboardState.Copied });

        command.Header.Should().Be("Insert Copied Cells...");
        command.AccessHeader.Should().Be("Insert Copied _Cells...");
        command.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void InsertCopiedCells_ReadsInsertCutCellsAfterCut()
    {
        var command = FindInsertCopiedCells(
            WorksheetContextMenuState.Default with { Clipboard = WorksheetContextMenuClipboardState.Cut });

        command.Header.Should().Be("Insert Cut Cells...");
        command.AccessHeader.Should().Be("Insert Cut _Cells...");
        command.IsEnabled.Should().BeTrue();
        command.Action.Should().Be(WorksheetContextMenuAction.InsertCopiedCells);
    }

    private static WorksheetContextMenuCommand FindInsertCopiedCells(WorksheetContextMenuState state)
    {
        static IEnumerable<WorksheetContextMenuCommand> Flatten(IEnumerable<WorksheetContextMenuCommand> commands) =>
            commands.SelectMany(command => new[] { command }.Concat(Flatten(command.Children)));

        return Flatten(WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.Worksheet, state))
            .Single(command => command.Action == WorksheetContextMenuAction.InsertCopiedCells);
    }
}
