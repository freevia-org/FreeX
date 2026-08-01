using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class WorksheetContextMenuPlannerTests
{
    [Fact]
    public void BuildCommands_ForPictureTargetIncludesExcelObjectCommands()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.Picture);

        commands.Select(command => command.Header).Should().ContainInOrder(
            "Cut",
            "Copy",
            "Paste",
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
            "Cut",
            "Copy",
            "Paste",
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

    [Fact]
    public void BuildCommands_ForChartTargetIncludesChartCommandsAndPaneAccess()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.Chart);

        commands.Select(command => command.Header).Should().ContainInOrder(
            "Cut",
            "Copy",
            "Paste",
            "Format Chart Area...",
            "Select Data...",
            "Change Chart Type...",
            "Chart Styles...",
            "Chart Titles...",
            "Size and Properties...",
            "Move Chart...",
            "Selection Pane...");
        commands.Single(command => command.Header == "Format Chart Area...")
            .Action.Should().Be(WorksheetContextMenuAction.FormatChartArea);
        commands.Single(command => command.Header == "Select Data...")
            .Action.Should().Be(WorksheetContextMenuAction.SelectChartData);
        commands.Single(command => command.Header == "Size and Properties...")
            .Action.Should().Be(WorksheetContextMenuAction.ChartSizeAndProperties);
        commands.Single(command => command.Header == "Selection Pane...")
            .Action.Should().Be(WorksheetContextMenuAction.SelectionPane);
    }

    [Theory]
    [InlineData(FreeX.Core.Model.SelectionPaneObjectKind.Picture, WorksheetContextMenuTargetKind.Picture)]
    [InlineData(FreeX.Core.Model.SelectionPaneObjectKind.Shape, WorksheetContextMenuTargetKind.Shape)]
    [InlineData(FreeX.Core.Model.SelectionPaneObjectKind.TextBox, WorksheetContextMenuTargetKind.TextBox)]
    [InlineData(FreeX.Core.Model.SelectionPaneObjectKind.Chart, WorksheetContextMenuTargetKind.Chart)]
    public void TargetKindForObject_MapsDrawingObjectKindsToTheirMenuTarget(
        FreeX.Core.Model.SelectionPaneObjectKind kind,
        WorksheetContextMenuTargetKind expected)
    {
        WorksheetContextMenuPlanner.TargetKindForObject(kind).Should().Be(expected);
    }

    [Fact]
    public void TargetKindForObject_FallsBackToWorksheet_ForUnknownKinds()
    {
        WorksheetContextMenuPlanner.TargetKindForObject((FreeX.Core.Model.SelectionPaneObjectKind)999)
            .Should().Be(WorksheetContextMenuTargetKind.Worksheet);
    }
}
