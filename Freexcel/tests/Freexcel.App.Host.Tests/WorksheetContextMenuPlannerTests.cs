using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class WorksheetContextMenuPlannerTests
{
    [Fact]
    public void BuildCommands_IncludesCommonExcelWorksheetContextActions()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands();

        commands.Select(command => command.Header).Should().ContainInOrder(
            "Cut",
            "Copy",
            "Paste",
            "Paste Special...",
            "Insert...",
            "Insert Row Above",
            "Delete...",
            "Delete Row(s)",
            "Sort A to Z",
            "Filter...",
            "Clear Filter",
            "Hide Rows",
            "Unhide Rows",
            "Hide Columns",
            "Unhide Columns",
            "New Note",
            "Delete Note",
            "Hyperlink...",
            "Format Cells...",
            "Clear Formats",
            "Clear Hyperlinks",
            "Clear Contents");

        commands.Single(command => command.Header == "Clear Filter")
            .Action.Should().Be(WorksheetContextMenuAction.ClearFilter);
    }
}
