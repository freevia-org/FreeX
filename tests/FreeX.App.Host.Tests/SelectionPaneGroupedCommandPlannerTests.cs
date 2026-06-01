using System.IO;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class SelectionPaneGroupedCommandPlannerTests
{
    [Fact]
    public void CreateCommand_RemapsPictureSelectionPaneChangesAcrossGroupedSheets()
    {
        var wb = new Workbook("test");
        var activeSheet = wb.AddSheet("Sheet1");
        var groupedSheet = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);
        var activeBack = AddPicture(activeSheet, 2, 2, "Active Back");
        var activeFront = AddPicture(activeSheet, 3, 2, "Active Front");
        var groupedBack = AddPicture(groupedSheet, 2, 2, "Grouped Back");
        var groupedFront = AddPicture(groupedSheet, 3, 2, "Grouped Front");
        var result = new SelectionPaneDialogResult(
            SelectionPaneDialogAction.ApplyVisibility,
            null,
            [new SelectionPaneVisibilityChange(SelectionPaneObjectKind.Picture, activeBack.Id, false)],
            [new SelectionPaneRenameChange(SelectionPaneObjectKind.Picture, activeBack.Id, "Quarter Logo")],
            [new SelectionPaneMoveChange(SelectionPaneObjectKind.Picture, activeBack.Id, Forward: true)]);
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
        activeSheet.Pictures.Should().Equal(activeFront, activeBack);
        groupedSheet.Pictures.Should().Equal(groupedFront, groupedBack);

        command.Revert(ctx);

        activeBack.IsVisible.Should().BeTrue();
        groupedBack.IsVisible.Should().BeTrue();
        activeBack.Name.Should().Be("Active Back");
        groupedBack.Name.Should().Be("Grouped Back");
        activeSheet.Pictures.Should().Equal(activeBack, activeFront);
        groupedSheet.Pictures.Should().Equal(groupedBack, groupedFront);
    }

    [Fact]
    public void CreateCommand_FailsWhenGroupedSheetCannotResolveEquivalentObjectAndRollsBack()
    {
        var wb = new Workbook("test");
        var activeSheet = wb.AddSheet("Sheet1");
        var groupedSheet = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);
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
    public void MainWindowDrawing_RoutesSelectionPaneChangesThroughGroupedPlanner()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Drawing.cs"));
        var method = SourceMethod(source, "private void ApplySelectionPaneChanges(", "private DrawingShapeModel? GetTargetDrawingShape(");

        method.Should().Contain("SelectionPaneGroupedCommandPlanner.HasChanges(result)");
        method.Should().Contain("TryExecuteGroupedSheetCommand(");
        method.Should().Contain("SelectionPaneGroupedCommandPlanner.CreateCommand(_workbook, _currentSheetId, sheetId, result)");
        method.Should().NotContain("new RenameSelectionPaneObjectCommand(_currentSheetId");
        method.Should().NotContain("new SetSelectionPaneObjectVisibilityCommand(_currentSheetId");
        method.Should().NotContain("new MoveSelectionPaneObjectCommand(_currentSheetId");
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

    private static string SourceMethod(string source, string start, string end) =>
        source[source.IndexOf(start, StringComparison.Ordinal)..source.IndexOf(end, StringComparison.Ordinal)];

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
