using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed class SelectionPaneGroupedCommandPlannerTests
{
    [Fact]
    public void CreateCommand_RemapsPictureSelectionPaneChangesAcrossGroupedSheets()
    {
        var wb = new Workbook("test");
        var activeSheet = wb.AddSheet("Sheet1");
        var groupedSheet = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);
        var activeBack = AddPicture(activeSheet, 2, 2, "Active Back");
        var activeFront = AddPicture(activeSheet, 3, 2, "Active Front");
        var groupedBack = AddPicture(groupedSheet, 2, 2, "Grouped Back");
        var groupedFront = AddPicture(groupedSheet, 3, 2, "Grouped Front");
        var result = new SelectionPaneDialogResult(
            SelectionPaneDialogAction.ApplyVisibility,
            null,
            [new SelectionPaneVisibilityChange(SelectionPaneObjectKind.Picture, activeBack.Id, false)],
            [new SelectionPaneRenameChange(SelectionPaneObjectKind.Picture, activeBack.Id, "Quarter Logo")],
            [new SelectionPaneMoveChange(SelectionPaneObjectKind.Picture, activeBack.Id, Forward: true)],
            []);
        var command = new CompositeWorkbookCommand(
            "Selection Pane",
            [
                SelectionPaneGroupedCommandPlanner.CreateCommand(wb, activeSheet.Id, activeSheet.Id, result),
                SelectionPaneGroupedCommandPlanner.CreateCommand(wb, activeSheet.Id, groupedSheet.Id, result)
            ]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        activeBack.IsVisible.Should().BeFalse();
        groupedBack.IsVisible.Should().BeFalse();
        activeBack.Name.Should().Be("Quarter Logo");
        groupedBack.Name.Should().Be("Quarter Logo");
        activeSheet.DrawingObjectZOrder.Should().Equal(
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, activeFront.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, activeBack.Id));
        groupedSheet.DrawingObjectZOrder.Should().Equal(
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, groupedFront.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, groupedBack.Id));

        command.Revert(ctx);

        activeBack.IsVisible.Should().BeTrue();
        groupedBack.IsVisible.Should().BeTrue();
        activeBack.Name.Should().Be("Active Back");
        groupedBack.Name.Should().Be("Grouped Back");
        activeSheet.DrawingObjectZOrder.Should().BeEmpty();
        groupedSheet.DrawingObjectZOrder.Should().BeEmpty();
    }

    [Fact]
    public void CreateCommand_FailsWhenGroupedSheetCannotResolveEquivalentObjectAndRollsBack()
    {
        var wb = new Workbook("test");
        var activeSheet = wb.AddSheet("Sheet1");
        var groupedSheet = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);
        var activeShape = new DrawingShapeModel
        {
            Anchor = new CellAddress(activeSheet.Id, 4, 2),
            Name = "Active Shape"
        };
        activeSheet.DrawingShapes.Add(activeShape);
        var result = new SelectionPaneDialogResult(
            SelectionPaneDialogAction.ApplyVisibility,
            null,
            [new SelectionPaneVisibilityChange(SelectionPaneObjectKind.Shape, activeShape.Id, false)],
            [new SelectionPaneRenameChange(SelectionPaneObjectKind.Shape, activeShape.Id, "Grouped Shape")],
            [],
            []);
        var command = new CompositeWorkbookCommand(
            "Selection Pane",
            [
                SelectionPaneGroupedCommandPlanner.CreateCommand(wb, activeSheet.Id, activeSheet.Id, result),
                SelectionPaneGroupedCommandPlanner.CreateCommand(wb, activeSheet.Id, groupedSheet.Id, result)
            ]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Selection pane object was not found.");
        activeShape.IsVisible.Should().BeTrue();
        activeShape.Name.Should().Be("Active Shape");
    }

    [Fact]
    public void CreateCommand_PropagatesMixedSupportedDrawingObjectMovesAcrossGroupedSheets()
    {
        var wb = new Workbook("test");
        var activeSheet = wb.AddSheet("Sheet1");
        var groupedSheet = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);
        var activeShape = AddShape(activeSheet, 2, 2, "Active Shape");
        var activePicture = AddPicture(activeSheet, 3, 2, "Active Picture");
        var groupedShape = AddShape(groupedSheet, 2, 2, "Grouped Shape");
        var groupedPicture = AddPicture(groupedSheet, 3, 2, "Grouped Picture");
        var result = new SelectionPaneDialogResult(
            SelectionPaneDialogAction.ApplyVisibility,
            null,
            [],
            [],
            [new SelectionPaneMoveChange(SelectionPaneObjectKind.Shape, activeShape.Id, Forward: true)],
            []);
        var command = new CompositeWorkbookCommand(
            "Selection Pane",
            [
                SelectionPaneGroupedCommandPlanner.CreateCommand(wb, activeSheet.Id, activeSheet.Id, result),
                SelectionPaneGroupedCommandPlanner.CreateCommand(wb, activeSheet.Id, groupedSheet.Id, result)
            ]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        activeSheet.DrawingObjectZOrder.Should().Equal(
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, activePicture.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, activeShape.Id));
        groupedSheet.DrawingObjectZOrder.Should().Equal(
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, groupedPicture.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, groupedShape.Id));

        command.Revert(ctx);

        activeSheet.DrawingObjectZOrder.Should().BeEmpty();
        groupedSheet.DrawingObjectZOrder.Should().BeEmpty();
    }

    [Fact]
    public void MainWindowDrawing_RoutesSelectionPaneChangesThroughGroupedPlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Drawing.cs");
        var method = SourceMethod(source, "private void ApplySelectionPaneChanges(", "private DrawingShapeModel? GetTargetDrawingShape(");

        method.Should().Contain("SelectionPaneGroupedCommandPlanner.HasChanges(result)");
        method.Should().Contain("TryExecuteGroupedSheetCommand(");
        method.Should().Contain("SelectionPaneGroupedCommandPlanner.CreateCommand(_workbook, _currentSheetId, sheetId, result)");
        method.Should().NotContain("new RenameSelectionPaneObjectCommand(_currentSheetId");
        method.Should().NotContain("new SetSelectionPaneObjectVisibilityCommand(_currentSheetId");
        method.Should().NotContain("new MoveSelectionPaneObjectCommand(_currentSheetId");

        WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Presentation", "DrawingUI", "SelectionPaneGroupedCommandPlanner.cs")
            .Should()
            .Contain("public static class SelectionPaneGroupedCommandPlanner");
        File.Exists(Path.Combine(
                WorkspaceFileLocator.FindWorkspaceRoot(),
                "src",
                "FreeX.App.Host",
                "SelectionPaneGroupedCommandPlanner.cs"))
            .Should()
            .BeFalse("grouped selection-pane command composition is shared DrawingUI planning, not WPF renderer code");
        File.Exists(Path.Combine(
                WorkspaceFileLocator.FindWorkspaceRoot(),
                "src",
                "FreeX.App.Services",
                "SelectionPaneGroupedCommandPlanner.cs"))
            .Should()
            .BeFalse("drawing/selection command planning should stay with the shared DrawingUI planner layer");
    }

    private static PictureModel AddPicture(Sheet sheet, uint row, uint col, string name)
    {
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, row, col),
            Name = name,
            IsVisible = true
        };
        sheet.Pictures.Add(picture);
        return picture;
    }

    private static DrawingShapeModel AddShape(Sheet sheet, uint row, uint col, string name)
    {
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, row, col),
            Name = name,
            IsVisible = true
        };
        sheet.DrawingShapes.Add(shape);
        return shape;
    }

    private static string SourceMethod(string source, string start, string end) =>
        source[source.IndexOf(start, StringComparison.Ordinal)..source.IndexOf(end, StringComparison.Ordinal)];

}
