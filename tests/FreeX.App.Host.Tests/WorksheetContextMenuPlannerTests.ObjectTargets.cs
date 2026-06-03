using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class WorksheetContextMenuPlannerTests
{
    [Fact]
    public void BuildCommands_ForPictureTargetIncludesExcelObjectCommands()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.Picture);

        commands.Select(command => command.Header).Should().ContainInOrder(
            "Format Picture...",
            "Crop...",
            "Reset Crop",
            "Edit Alt Text...",
            "Selection Pane...");
        commands.Single(command => command.Header == "Format Picture...")
            .Action.Should().Be(WorksheetContextMenuAction.FormatPicture);
        commands.Single(command => command.Header == "Edit Alt Text...")
            .Action.Should().Be(WorksheetContextMenuAction.EditAltText);
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
            "Shape Outline...",
            "Edit Alt Text...",
            "Selection Pane...");
        if (includesReorder)
        {
            commands.Select(command => command.Header).Should().ContainInOrder(
                "Bring Forward",
                "Send Backward");
        }

        commands.Single(command => command.Header == formatHeader)
            .Action.Should().Be(WorksheetContextMenuAction.FormatDrawingObject);
        commands.Single(command => command.Header == formatHeader)
            .AccessHeader.Should().Be($"_Format {formatHeader["Format ".Length..]}");
    }
}
