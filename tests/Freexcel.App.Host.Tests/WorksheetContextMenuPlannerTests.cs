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
            "Pick From Drop-down List...",
            "Quick Analysis",
            "Hide Rows",
            "Unhide Rows",
            "AutoFit Row Height",
            "Hide Columns",
            "Unhide Columns",
            "AutoFit Column Width",
            "New Note",
            "Edit Note...",
            "Delete Note",
            "Show Notes",
            "Hyperlink...",
            "Format Cells...",
            "Clear All",
            "Clear Formats",
            "Clear Comments",
            "Clear Hyperlinks",
            "Clear Contents");

        commands.Single(command => command.Header == "Clear Filter")
            .Action.Should().Be(WorksheetContextMenuAction.ClearFilter);
        commands.Single(command => command.Header == "Pick From Drop-down List...")
            .Action.Should().Be(WorksheetContextMenuAction.PickFromDropDown);
        commands.Single(command => command.Header == "Quick Analysis")
            .Action.Should().Be(WorksheetContextMenuAction.QuickAnalysis);
        commands.Single(command => command.Header == "AutoFit Row Height")
            .Action.Should().Be(WorksheetContextMenuAction.AutoFitRowHeight);
        commands.Single(command => command.Header == "AutoFit Column Width")
            .Action.Should().Be(WorksheetContextMenuAction.AutoFitColumnWidth);
        commands.Single(command => command.Header == "Clear All")
            .Action.Should().Be(WorksheetContextMenuAction.ClearAll);
        commands.Single(command => command.Header == "Clear Comments")
            .Action.Should().Be(WorksheetContextMenuAction.ClearComments);
        commands.Single(command => command.Header == "Edit Note...")
            .Action.Should().Be(WorksheetContextMenuAction.EditNote);
        commands.Single(command => command.Header == "Show Notes")
            .Action.Should().Be(WorksheetContextMenuAction.ShowNotes);
    }
}
