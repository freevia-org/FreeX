using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class WorksheetContextMenuPlannerTests
{
    [Fact]
    public void BuildCommands_ForWholeRowSelectionIncludesOnlyRowLayoutCommands()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.RowSelection);

        commands.Select(command => command.Header).Should().ContainInOrder(
            "Cut",
            "Copy",
            "Paste",
            "Insert Row Above",
            "Delete Row(s)",
            "Row Height...",
            "AutoFit Row Height",
            "Hide Rows",
            "Unhide Rows",
            "Group",
            "Ungroup",
            "Format Cells...",
            "Clear Contents");
        commands.Single(command => command.Header == "Group").Should().BeEquivalentTo(
            new WorksheetContextMenuCommand(
                "Group",
                WorksheetContextMenuAction.Group,
                AccessHeader: "_Group"));
        commands.Single(command => command.Header == "Ungroup").Should().BeEquivalentTo(
            new WorksheetContextMenuCommand(
                "Ungroup",
                WorksheetContextMenuAction.Ungroup,
                AccessHeader: "_Ungroup"));
        commands.Single(command => command.Header == "Format Cells...").Should().BeEquivalentTo(
            new WorksheetContextMenuCommand(
                "Format Cells...",
                WorksheetContextMenuAction.FormatCells,
                AccessHeader: "_Format Cells..."));
        commands.Select(command => command.Header).Should().NotContain([
            "Insert Column Left",
            "Delete Column(s)",
            "Column Width...",
            "AutoFit Column Width",
            "Hide Columns",
            "Unhide Columns"
        ]);
    }

    [Fact]
    public void BuildCommands_ForWholeColumnSelectionIncludesOnlyColumnLayoutCommands()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.ColumnSelection);

        commands.Select(command => command.Header).Should().ContainInOrder(
            "Cut",
            "Copy",
            "Paste",
            "Insert Column Left",
            "Delete Column(s)",
            "Column Width...",
            "AutoFit Column Width",
            "Hide Columns",
            "Unhide Columns",
            "Group",
            "Ungroup",
            "Format Cells...",
            "Clear Contents");
        commands.Single(command => command.Header == "Group").Should().BeEquivalentTo(
            new WorksheetContextMenuCommand(
                "Group",
                WorksheetContextMenuAction.Group,
                AccessHeader: "_Group"));
        commands.Single(command => command.Header == "Ungroup").Should().BeEquivalentTo(
            new WorksheetContextMenuCommand(
                "Ungroup",
                WorksheetContextMenuAction.Ungroup,
                AccessHeader: "_Ungroup"));
        commands.Single(command => command.Header == "Format Cells...").Should().BeEquivalentTo(
            new WorksheetContextMenuCommand(
                "Format Cells...",
                WorksheetContextMenuAction.FormatCells,
                AccessHeader: "_Format Cells..."));
        commands.Select(command => command.Header).Should().NotContain([
            "Insert Row Above",
            "Delete Row(s)",
            "Row Height...",
            "AutoFit Row Height",
            "Hide Rows",
            "Unhide Rows"
        ]);
    }

    [Theory]
    [MemberData(nameof(RowColumnSizingVisibilityCases))]
    public void BuildCommands_RowAndColumnTargetsExposeSizingVisibilityMetadata(
        WorksheetContextMenuTargetKind targetKind,
        WorksheetContextMenuCommand[] expectedCommands)
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(targetKind)
            .Where(command => !command.IsSeparator)
            .ToList();

        commands.Select(command => command.Header).Should().ContainInOrder(
            expectedCommands.Select(command => command.Header));
        foreach (var expectedCommand in expectedCommands)
        {
            commands.Single(command => command.Header == expectedCommand.Header)
                .Should()
                .BeEquivalentTo(expectedCommand);
        }
    }

    [Theory]
    [MemberData(nameof(TargetSpecificCommandEnvelopeCases))]
    public void BuildCommands_TargetSpecificMenusExposeOnlyExpectedCommandFamilies(
        WorksheetContextMenuTargetKind targetKind,
        string[] expectedHeaders,
        string[] absentHeaders)
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(targetKind);

        commands.Select(command => command.Header)
            .Where(header => header.Length > 0)
            .Should()
            .Contain(expectedHeaders)
            .And.NotContain(absentHeaders);
    }
}
