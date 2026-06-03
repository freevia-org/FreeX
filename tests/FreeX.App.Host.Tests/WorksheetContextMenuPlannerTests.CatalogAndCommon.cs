using FluentAssertions;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class WorksheetContextMenuPlannerTests
{
    [Fact]
    public void UiTestCatalog_WorksheetContextMenuCommandCountMatchesPlanner()
    {
        var catalog = File.ReadAllText(WorkspaceFileLocator.Find("docs", "UI_TEST_CATALOG.md"));
        var commandCount = WorksheetContextMenuPlanner.BuildCommands()
            .Count(command => !command.IsSeparator);

        catalog.Should().Contain(
            $"| Worksheet context menu commands | {commandCount} | From `WorksheetContextMenuPlanner.BuildCommands()`. |");
        catalog.Should().Contain($"Worksheet context menu has {commandCount} planner commands");
        catalog.Should().Contain($"| Worksheet context menu | {commandCount} planner commands via right-click, Shift+F10, Menu key. |");
        catalog.Should().Contain($"| UI-CAT-CONTEXT-001 | Worksheet context menu | {commandCount} worksheet context-menu planner commands. |");
        catalog.Should().NotContain("47 planner commands");
        catalog.Should().NotContain("47 worksheet context-menu planner commands");
    }

    [Fact]
    public void BuildCommands_IncludesCommonExcelWorksheetContextActions()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands();

        commands.Select(command => command.Header).Should().ContainInOrder(
            "Cut",
            "Copy",
            "Paste",
            "Paste Special...",
            "Insert Copied Cells...",
            "Insert...",
            "Insert Row Above",
            "Delete...",
            "Delete Row(s)",
            "Sort A to Z",
            "Custom Sort...",
            "Filter...",
            "Clear Filter",
            "Reapply Filter",
            "Pick From Drop-down List...",
            "Quick Analysis",
            "Define Name...",
            "Create Table...",
            "Format as Table...",
            "Text to Columns...",
            "Remove Duplicates...",
            "Data Validation...",
            "Hide Rows",
            "Unhide Rows",
            "Row Height...",
            "AutoFit Row Height",
            "Hide Columns",
            "Unhide Columns",
            "Column Width...",
            "AutoFit Column Width",
            "New Comment",
            "Edit Comment...",
            "Resolve Comment",
            "Delete Comment",
            "New Note",
            "Edit Note...",
            "Delete Note",
            "Show Notes",
            "Hyperlink...",
            "Format Cells...",
            "Clear All",
            "Clear Formats",
            "Clear Comments and Notes",
            "Clear Hyperlinks",
            "Clear Contents");

        commands.Single(command => command.Header == "Clear Filter")
            .Action.Should().Be(WorksheetContextMenuAction.ClearFilter);
        commands.Single(command => command.Header == "Custom Sort...")
            .Action.Should().Be(WorksheetContextMenuAction.CustomSort);
        commands.Single(command => command.Header == "Reapply Filter")
            .Action.Should().Be(WorksheetContextMenuAction.ReapplyFilter);
        commands.Single(command => command.Header == "Pick From Drop-down List...")
            .Action.Should().Be(WorksheetContextMenuAction.PickFromDropDown);
        commands.Single(command => command.Header == "Quick Analysis")
            .Action.Should().Be(WorksheetContextMenuAction.QuickAnalysis);
        commands.Single(command => command.Header == "Insert Copied Cells...")
            .Action.Should().Be(WorksheetContextMenuAction.InsertCopiedCells);
        commands.Single(command => command.Header == "Define Name...")
            .Action.Should().Be(WorksheetContextMenuAction.DefineName);
        commands.Single(command => command.Header == "Create Table...")
            .Action.Should().Be(WorksheetContextMenuAction.CreateTable);
        commands.Single(command => command.Header == "Format as Table...")
            .Action.Should().Be(WorksheetContextMenuAction.FormatAsTable);
        commands.Single(command => command.Header == "Text to Columns...")
            .Action.Should().Be(WorksheetContextMenuAction.TextToColumns);
        commands.Single(command => command.Header == "Remove Duplicates...")
            .Action.Should().Be(WorksheetContextMenuAction.RemoveDuplicates);
        commands.Single(command => command.Header == "Data Validation...")
            .Action.Should().Be(WorksheetContextMenuAction.DataValidation);
        commands.Single(command => command.Header == "Row Height...")
            .Action.Should().Be(WorksheetContextMenuAction.RowHeight);
        commands.Single(command => command.Header == "AutoFit Row Height")
            .Action.Should().Be(WorksheetContextMenuAction.AutoFitRowHeight);
        commands.Single(command => command.Header == "Column Width...")
            .Action.Should().Be(WorksheetContextMenuAction.ColumnWidth);
        commands.Single(command => command.Header == "AutoFit Column Width")
            .Action.Should().Be(WorksheetContextMenuAction.AutoFitColumnWidth);
        commands.Single(command => command.Header == "Clear All")
            .Action.Should().Be(WorksheetContextMenuAction.ClearAll);
        commands.Single(command => command.Header == "Clear Comments and Notes")
            .Action.Should().Be(WorksheetContextMenuAction.ClearComments);
        commands.Single(command => command.Header == "New Comment")
            .Action.Should().Be(WorksheetContextMenuAction.NewComment);
        commands.Single(command => command.Header == "Edit Comment...")
            .Action.Should().Be(WorksheetContextMenuAction.EditComment);
        commands.Single(command => command.Header == "Resolve Comment")
            .Action.Should().Be(WorksheetContextMenuAction.ResolveComment);
        commands.Single(command => command.Header == "Delete Comment")
            .Action.Should().Be(WorksheetContextMenuAction.DeleteComment);
        commands.Single(command => command.Header == "Edit Note...")
            .Action.Should().Be(WorksheetContextMenuAction.EditNote);
        commands.Single(command => command.Header == "Show Notes")
            .Action.Should().Be(WorksheetContextMenuAction.ShowNotes);
    }

    [Fact]
    public void BuildCommands_ExposesInsertDeleteGroupInExcelLikeOrder()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands()
            .Where(command => !command.IsSeparator)
            .ToList();

        commands.Select(command => command.Header).Should().ContainInOrder(
            "Insert...",
            "Insert Row Above",
            "Insert Row Below",
            "Insert Column Left",
            "Insert Column Right",
            "Delete...",
            "Delete Row(s)",
            "Delete Column(s)");

        commands.Single(command => command.Header == "Insert...")
            .Should().BeEquivalentTo(new WorksheetContextMenuCommand(
                "Insert...",
                WorksheetContextMenuAction.InsertCells,
                AccessHeader: "_Insert..."));
        commands.Single(command => command.Header == "Insert Row Above")
            .Should().BeEquivalentTo(new WorksheetContextMenuCommand(
                "Insert Row Above",
                WorksheetContextMenuAction.InsertRowAbove,
                AccessHeader: "Insert Row _Above"));
        commands.Single(command => command.Header == "Insert Row Below")
            .Should().BeEquivalentTo(new WorksheetContextMenuCommand(
                "Insert Row Below",
                WorksheetContextMenuAction.InsertRowBelow,
                AccessHeader: "Insert Row _Below"));
        commands.Single(command => command.Header == "Insert Column Left")
            .Should().BeEquivalentTo(new WorksheetContextMenuCommand(
                "Insert Column Left",
                WorksheetContextMenuAction.InsertColumnLeft,
                AccessHeader: "Insert Column _Left"));
        commands.Single(command => command.Header == "Insert Column Right")
            .Should().BeEquivalentTo(new WorksheetContextMenuCommand(
                "Insert Column Right",
                WorksheetContextMenuAction.InsertColumnRight,
                AccessHeader: "Insert Column _Right"));
        commands.Single(command => command.Header == "Delete...")
            .Should().BeEquivalentTo(new WorksheetContextMenuCommand(
                "Delete...",
                WorksheetContextMenuAction.DeleteCells,
                AccessHeader: "_Delete..."));
        commands.Single(command => command.Header == "Delete Row(s)")
            .Should().BeEquivalentTo(new WorksheetContextMenuCommand(
                "Delete Row(s)",
                WorksheetContextMenuAction.DeleteRows,
                AccessHeader: "Delete _Row(s)"));
        commands.Single(command => command.Header == "Delete Column(s)")
            .Should().BeEquivalentTo(new WorksheetContextMenuCommand(
                "Delete Column(s)",
                WorksheetContextMenuAction.DeleteColumns,
                AccessHeader: "Delete _Column(s)"));
    }

    [Theory]
    [InlineData("Cut", "Cu_t")]
    [InlineData("Copy", "_Copy")]
    [InlineData("Paste", "_Paste")]
    [InlineData("Paste Special...", "Paste _Special...")]
    [InlineData("Insert Copied Cells...", "Insert Copied _Cells...")]
    [InlineData("Insert...", "_Insert...")]
    [InlineData("Insert Row Above", "Insert Row _Above")]
    [InlineData("Insert Row Below", "Insert Row _Below")]
    [InlineData("Insert Column Left", "Insert Column _Left")]
    [InlineData("Insert Column Right", "Insert Column _Right")]
    [InlineData("Delete...", "_Delete...")]
    [InlineData("Delete Row(s)", "Delete _Row(s)")]
    [InlineData("Delete Column(s)", "Delete _Column(s)")]
    [InlineData("Quick Analysis", "_Quick Analysis")]
    [InlineData("Hide Rows", "_Hide Rows")]
    [InlineData("Unhide Rows", "Unhide Ro_ws")]
    [InlineData("Row Height...", "Row _Height...")]
    [InlineData("AutoFit Row Height", "AutoFit Row He_ight")]
    [InlineData("Hide Columns", "Hide Col_umns")]
    [InlineData("Unhide Columns", "Unhide Co_lumns")]
    [InlineData("Column Width...", "Column _Width...")]
    [InlineData("AutoFit Column Width", "AutoFit Column Wi_dth")]
    [InlineData("Edit Comment...", "_Edit Comment...")]
    [InlineData("Resolve Comment", "Resol_ve Comment")]
    [InlineData("Delete Comment", "Delete _Comment")]
    [InlineData("Format Cells...", "_Format Cells...")]
    [InlineData("Clear Contents", "Clear C_ontents")]
    public void BuildCommands_ProvidesKeyboardAccessHeaders(string header, string expectedAccessHeader)
    {
        var command = WorksheetContextMenuPlanner.BuildCommands()
            .Single(command => command.Header == header);

        command.AccessHeader.Should().Be(expectedAccessHeader);
    }
}
