using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class SelectionPaneViewPlannerTests
{
    private static SelectionPaneViewPlanner.Text Text => SelectionPaneViewPlanner.Text.Default;

    [Fact]
    public void BuildItems_ListsObjectsFrontToBackWithDefaultNamesAndKindLabels()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.Charts.Add(new ChartModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            IsVisible = true,
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.Rectangle,
            IsVisible = false,
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 3),
            Name = "Executive Notes",
            IsVisible = true,
        });

        var items = SelectionPaneViewPlanner.BuildItems(sheet, Text);

        items.Select(item => item.Name).Should().Equal("Executive Notes", "Rectangle 1", "Chart 1");
        items.Select(item => item.Kind).Should().Equal(
            SelectionPaneObjectKind.TextBox,
            SelectionPaneObjectKind.Shape,
            SelectionPaneObjectKind.Chart);
        items.Single(item => item.Kind == SelectionPaneObjectKind.Shape).IsVisible.Should().BeFalse();
        items.Single(item => item.Kind == SelectionPaneObjectKind.Shape).KindLabel.Should().Be("Shape");
    }

    [Fact]
    public void BuildItems_ExposesMoveFlagsWithinSupportedStack()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var back = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        var middle = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 2) };
        var front = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 3) };
        sheet.DrawingShapes.Add(back);
        sheet.Pictures.Add(middle);
        sheet.TextBoxes.Add(front);

        var items = SelectionPaneViewPlanner.BuildItems(sheet, Text);

        items.Single(i => i.Id == front.Id).CanMoveUp.Should().BeFalse();
        items.Single(i => i.Id == front.Id).CanMoveDown.Should().BeTrue();
        items.Single(i => i.Id == middle.Id).CanMoveUp.Should().BeTrue();
        items.Single(i => i.Id == middle.Id).CanMoveDown.Should().BeTrue();
        items.Single(i => i.Id == back.Id).CanMoveUp.Should().BeTrue();
        items.Single(i => i.Id == back.Id).CanMoveDown.Should().BeFalse();
    }

    [Fact]
    public void PlanMove_BringForward_SwapsWithNeighborAndRecordsForwardMove()
    {
        var front = Item(SelectionPaneObjectKind.Picture, "Front");
        var back = Item(SelectionPaneObjectKind.Picture, "Back");
        var items = new[] { front, back };

        var plan = SelectionPaneViewPlanner.PlanMove(items, currentIndex: 1, forward: true);

        plan.Should().NotBeNull();
        plan!.Value.Ordered.Select(i => i.Name).Should().Equal("Back", "Front");
        plan.Value.Change.Should().Be(new SelectionPaneViewPlanner.MoveChange(SelectionPaneObjectKind.Picture, back.Id, Forward: true));
    }

    [Fact]
    public void PlanMove_AtTopBringForward_ReturnsNull()
    {
        var items = new[] { Item(SelectionPaneObjectKind.Shape, "A"), Item(SelectionPaneObjectKind.Shape, "B") };

        SelectionPaneViewPlanner.PlanMove(items, currentIndex: 0, forward: true).Should().BeNull();
    }

    [Fact]
    public void CanReorderKinds_AllowsSupportedKindsButRejectsChartMix()
    {
        SelectionPaneViewPlanner.CanReorderKinds(SelectionPaneObjectKind.Picture, SelectionPaneObjectKind.Shape).Should().BeTrue();
        SelectionPaneViewPlanner.CanReorderKinds(SelectionPaneObjectKind.Picture, SelectionPaneObjectKind.Chart).Should().BeFalse();
        SelectionPaneViewPlanner.CanReorderKinds(SelectionPaneObjectKind.Chart, SelectionPaneObjectKind.Chart).Should().BeTrue();
    }

    [Fact]
    public void CreateVisibilityAndRenameChanges_OnlyReportActualEdits()
    {
        var a = Item(SelectionPaneObjectKind.Picture, "Pic", isVisible: true);
        var b = Item(SelectionPaneObjectKind.Shape, "Shape", isVisible: true);
        var originals = new[] { a, b };
        var edited = new[]
        {
            a with { IsVisible = false },
            b with { Name = "  Renamed  " },
        };

        SelectionPaneViewPlanner.CreateVisibilityChanges(originals, edited)
            .Should().Equal(new SelectionPaneViewPlanner.VisibilityChange(SelectionPaneObjectKind.Picture, a.Id, IsVisible: false));
        SelectionPaneViewPlanner.CreateRenameChanges(originals, edited)
            .Should().Equal(new SelectionPaneViewPlanner.RenameChange(SelectionPaneObjectKind.Shape, b.Id, "Renamed"));
    }

    [Fact]
    public void CreateCommand_BuildsCompositeForAllChangeKinds()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var id = Guid.NewGuid();

        var command = SelectionPaneViewPlanner.CreateCommand(
            sheet.Id,
            [new SelectionPaneViewPlanner.VisibilityChange(SelectionPaneObjectKind.Picture, id, IsVisible: false)],
            [new SelectionPaneViewPlanner.RenameChange(SelectionPaneObjectKind.Picture, id, "New")],
            [new SelectionPaneViewPlanner.MoveChange(SelectionPaneObjectKind.Picture, id, Forward: true)]);

        command.Should().BeOfType<CompositeWorkbookCommand>();
    }

    [Fact]
    public void CreateCommand_ReturnsNullWhenNoChanges()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        SelectionPaneViewPlanner.CreateCommand(sheet.Id, [], [], []).Should().BeNull();
    }

    private static SelectionPaneViewPlanner.Item Item(SelectionPaneObjectKind kind, string name, bool isVisible = true) =>
        new(kind, Guid.NewGuid(), name, kind.ToString(), isVisible, CanMoveUp: false, CanMoveDown: false);
}
