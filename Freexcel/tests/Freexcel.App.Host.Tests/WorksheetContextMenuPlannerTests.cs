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
            "Delete Comment",
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
        commands.Single(command => command.Header == "Clear Comments")
            .Action.Should().Be(WorksheetContextMenuAction.ClearComments);
        commands.Single(command => command.Header == "New Comment")
            .Action.Should().Be(WorksheetContextMenuAction.NewComment);
        commands.Single(command => command.Header == "Edit Comment...")
            .Action.Should().Be(WorksheetContextMenuAction.EditComment);
        commands.Single(command => command.Header == "Delete Comment")
            .Action.Should().Be(WorksheetContextMenuAction.DeleteComment);
        commands.Single(command => command.Header == "Edit Note...")
            .Action.Should().Be(WorksheetContextMenuAction.EditNote);
        commands.Single(command => command.Header == "Show Notes")
            .Action.Should().Be(WorksheetContextMenuAction.ShowNotes);
    }

    [Theory]
    [InlineData("Cut", "Cu_t")]
    [InlineData("Copy", "_Copy")]
    [InlineData("Paste", "_Paste")]
    [InlineData("Paste Special...", "Paste _Special...")]
    [InlineData("Insert Copied Cells...", "Insert Copied _Cells...")]
    [InlineData("Quick Analysis", "_Quick Analysis")]
    [InlineData("Edit Comment...", "_Edit Comment...")]
    [InlineData("Delete Comment", "Delete _Comment")]
    [InlineData("Format Cells...", "_Format Cells...")]
    [InlineData("Clear Contents", "Clear C_ontents")]
    public void BuildCommands_ProvidesKeyboardAccessHeaders(string header, string expectedAccessHeader)
    {
        var command = WorksheetContextMenuPlanner.BuildCommands()
            .Single(command => command.Header == header);

        command.AccessHeader.Should().Be(expectedAccessHeader);
    }

    [Fact]
    public void BuildCommands_ForPictureTargetIncludesExcelObjectCommands()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.Picture);

        commands.Select(command => command.Header).Should().ContainInOrder(
            "Format Picture...",
            "Crop...",
            "Reset Crop");
        commands.Single(command => command.Header == "Format Picture...")
            .Action.Should().Be(WorksheetContextMenuAction.FormatPicture);
    }

    [Theory]
    [InlineData(WorksheetContextMenuTargetKind.Shape, "Format Shape...", true)]
    [InlineData(WorksheetContextMenuTargetKind.TextBox, "Format Text Box...", false)]
    public void BuildCommands_ForDrawingObjectTargetsIncludesExcelObjectCommands(
        WorksheetContextMenuTargetKind targetKind,
        string formatHeader,
        bool includesReorder)
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(targetKind);

        commands.Select(command => command.Header).Should().ContainInOrder(
            formatHeader,
            "Size and Properties...",
            "Rotate...",
            "Shape Fill...",
            "Shape Outline...");
        if (includesReorder)
        {
            commands.Select(command => command.Header).Should().ContainInOrder(
                "Bring Forward",
                "Send Backward");
        }

        commands.Single(command => command.Header == formatHeader)
            .Action.Should().Be(WorksheetContextMenuAction.FormatDrawingObject);
    }
}
