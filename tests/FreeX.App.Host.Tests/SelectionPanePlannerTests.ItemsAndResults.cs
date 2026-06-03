using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class SelectionPanePlannerTests
{
    [Fact]
    public void BuildItems_ListsVisibleObjectsTopToBottomWithExcelLikeNames()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var chart = new ChartModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            IsVisible = true
        };
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.Rectangle,
            IsVisible = false
        };
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 3),
            Text = "Notes",
            Name = "Executive Notes",
            IsVisible = true
        };
        sheet.Charts.Add(chart);
        sheet.DrawingShapes.Add(shape);
        sheet.TextBoxes.Add(textBox);

        var items = SelectionPanePlanner.BuildItems(sheet);

        items.Select(item => item.Name).Should().Equal("Executive Notes", "Rectangle 1", "Chart 1");
        items.Select(item => item.Kind).Should().Equal(
            SelectionPaneObjectKind.TextBox,
            SelectionPaneObjectKind.Shape,
            SelectionPaneObjectKind.Chart);
        items.Single(item => item.Id == shape.Id).IsVisible.Should().BeFalse();
    }

    [Fact]
    public void BuildItems_ExposesMoveFlagsWithinMixedSupportedObjectStack()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var back = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        var middle = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 2) };
        var front = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 3) };
        sheet.DrawingShapes.Add(back);
        sheet.Pictures.Add(middle);
        sheet.TextBoxes.Add(front);

        var items = SelectionPanePlanner.BuildItems(sheet);

        var frontItem = items.Single(item => item.Id == front.Id);
        var middleItem = items.Single(item => item.Id == middle.Id);
        var backItem = items.Single(item => item.Id == back.Id);
        frontItem.CanMoveUp.Should().BeFalse();
        frontItem.CanMoveDown.Should().BeTrue();
        middleItem.CanMoveUp.Should().BeTrue();
        middleItem.CanMoveDown.Should().BeTrue();
        backItem.CanMoveUp.Should().BeTrue();
        backItem.CanMoveDown.Should().BeFalse();
    }

    [Fact]
    public void SelectionPaneDialog_CreateResult_PreservesVisibilityChangesWhenMoving()
    {
        var item = new SelectionPaneItem(
            SelectionPaneObjectKind.Picture,
            Guid.NewGuid(),
            "Picture 1",
            IsVisible: true,
            CanMoveUp: true,
            CanMoveDown: false);

        var result = SelectionPaneDialog.CreateResult(
            SelectionPaneDialogAction.MoveUp,
            item,
            [item],
            [(item.Id, false, "Picture 1")]);

        result.Action.Should().Be(SelectionPaneDialogAction.MoveUp);
        result.Target.Should().Be(item);
        result.VisibilityChanges.Should().Equal(new SelectionPaneVisibilityChange(
            SelectionPaneObjectKind.Picture,
            item.Id,
            IsVisible: false));
        result.RenameChanges.Should().BeEmpty();
        result.MoveChanges.Should().BeEmpty();
    }

    [Fact]
    public void SelectionPaneDialog_CreateResult_CapturesRenameChanges()
    {
        var item = new SelectionPaneItem(
            SelectionPaneObjectKind.Shape,
            Guid.NewGuid(),
            "Rectangle 1",
            IsVisible: true,
            CanMoveUp: false,
            CanMoveDown: false);

        var result = SelectionPaneDialog.CreateResult(
            SelectionPaneDialogAction.ApplyVisibility,
            null,
            [item],
            [(item.Id, true, "  Process Box  ")]);

        result.RenameChanges.Should().Equal(new SelectionPaneRenameChange(
            SelectionPaneObjectKind.Shape,
            item.Id,
            "Process Box"));
    }

    [Fact]
    public void SelectionPaneDialog_CreateResult_HandlesLargeStateListsWithUnnamedCurrentStates()
    {
        const int itemCount = 10_000;
        var items = Enumerable.Range(0, itemCount)
            .Select(index => new SelectionPaneItem(
                index % 2 == 0 ? SelectionPaneObjectKind.Picture : SelectionPaneObjectKind.Shape,
                Guid.NewGuid(),
                $"Object {index}",
                IsVisible: index % 3 != 0,
                CanMoveUp: index > 0,
                CanMoveDown: index < itemCount - 1))
            .ToArray();
        var states = items
            .Select((item, index) => (item.Id, IsVisible: index % 4 == 0))
            .ToArray();

        var result = SelectionPaneDialog.CreateResult(
            SelectionPaneDialogAction.ApplyVisibility,
            null,
            items,
            states);
        var expectedChangeCount = 0;
        for (var index = 0; index < items.Length; index++)
        {
            if (items[index].IsVisible != (index % 4 == 0))
                expectedChangeCount++;
        }

        result.VisibilityChanges.Should().HaveCount(expectedChangeCount);
        result.RenameChanges.Should().BeEmpty();
    }

    [Fact]
    public void SelectionPaneDialog_CreateDragMoveChanges_PlansAdjacentMovesToDroppedPosition()
    {
        var front = Guid.NewGuid();
        var middle = Guid.NewGuid();
        var back = Guid.NewGuid();

        var moves = SelectionPaneDialog.CreateDragMoveChanges(
            [
                (SelectionPaneObjectKind.Picture, front),
                (SelectionPaneObjectKind.Picture, middle),
                (SelectionPaneObjectKind.Picture, back)
            ],
            draggedId: back,
            targetId: front);

        moves.Should().Equal(
            new SelectionPaneMoveChange(SelectionPaneObjectKind.Picture, back, Forward: true),
            new SelectionPaneMoveChange(SelectionPaneObjectKind.Picture, back, Forward: true));
    }

    [Fact]
    public void SelectionPaneDialog_CreateDragMoveChanges_PlansMovesAfterDroppedPosition()
    {
        var front = Guid.NewGuid();
        var middle = Guid.NewGuid();
        var back = Guid.NewGuid();

        var moves = SelectionPaneDialog.CreateDragMoveChanges(
            [
                (SelectionPaneObjectKind.Picture, front),
                (SelectionPaneObjectKind.Picture, middle),
                (SelectionPaneObjectKind.Picture, back)
            ],
            draggedId: front,
            targetId: back,
            placement: SelectionPaneDropPlacement.After);

        moves.Should().Equal(
            new SelectionPaneMoveChange(SelectionPaneObjectKind.Picture, front, Forward: false),
            new SelectionPaneMoveChange(SelectionPaneObjectKind.Picture, front, Forward: false));
    }

    [Fact]
    public void SelectionPaneDialog_CreateDragMoveChanges_AllowsMixedSupportedDrops()
    {
        var picture = Guid.NewGuid();
        var shape = Guid.NewGuid();

        var moves = SelectionPaneDialog.CreateDragMoveChanges(
            [
                (SelectionPaneObjectKind.Picture, picture),
                (SelectionPaneObjectKind.Shape, shape)
            ],
            draggedId: picture,
            targetId: shape,
            placement: SelectionPaneDropPlacement.After);

        moves.Should().Equal(new SelectionPaneMoveChange(SelectionPaneObjectKind.Picture, picture, Forward: false));
    }

    [Fact]
    public void SelectionPaneDialog_CreateDragMoveChanges_RejectsChartMixedDrops()
    {
        var picture = Guid.NewGuid();
        var chart = Guid.NewGuid();

        var moves = SelectionPaneDialog.CreateDragMoveChanges(
            [
                (SelectionPaneObjectKind.Picture, picture),
                (SelectionPaneObjectKind.Chart, chart)
            ],
            draggedId: picture,
            targetId: chart);

        moves.Should().BeEmpty();
    }
}
