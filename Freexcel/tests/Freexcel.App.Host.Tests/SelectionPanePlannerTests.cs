using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class SelectionPanePlannerTests
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
            IsVisible = true
        };
        sheet.Charts.Add(chart);
        sheet.DrawingShapes.Add(shape);
        sheet.TextBoxes.Add(textBox);

        var items = SelectionPanePlanner.BuildItems(sheet);

        items.Select(item => item.Name).Should().Equal("Text Box 1", "Rectangle 1", "Chart 1");
        items.Select(item => item.Kind).Should().Equal(
            SelectionPaneObjectKind.TextBox,
            SelectionPaneObjectKind.Shape,
            SelectionPaneObjectKind.Chart);
        items.Single(item => item.Id == shape.Id).IsVisible.Should().BeFalse();
    }

    [Fact]
    public void BuildItems_ExposesMoveFlagsWithinObjectKindStack()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var back = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        var front = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 2) };
        sheet.Pictures.Add(back);
        sheet.Pictures.Add(front);

        var items = SelectionPanePlanner.BuildItems(sheet);

        var frontItem = items.Single(item => item.Id == front.Id);
        var backItem = items.Single(item => item.Id == back.Id);
        frontItem.CanMoveUp.Should().BeFalse();
        frontItem.CanMoveDown.Should().BeTrue();
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
            [(item.Id, false)]);

        result.Action.Should().Be(SelectionPaneDialogAction.MoveUp);
        result.Target.Should().Be(item);
        result.VisibilityChanges.Should().Equal(new SelectionPaneVisibilityChange(
            SelectionPaneObjectKind.Picture,
            item.Id,
            IsVisible: false));
    }
}
