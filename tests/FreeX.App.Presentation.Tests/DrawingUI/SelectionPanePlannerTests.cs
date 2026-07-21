using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class SelectionPanePlannerTests
{
    [Fact]
    public void ParityFixture_CreatesSharedDialogItemsForWpfAndAvaloniaCapture()
    {
        var chartId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var shapeId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var items = SelectionPaneParityFixture.CreateDialogItems(
            chartId,
            shapeId,
            chartIsVisible: true,
            shapeIsVisible: false);

        items.Should().Equal(
            new SelectionPaneItem(
                SelectionPaneObjectKind.Chart,
                chartId,
                SelectionPaneParityFixture.ChartName,
                IsVisible: true,
                CanMoveUp: false,
                CanMoveDown: false),
            new SelectionPaneItem(
                SelectionPaneObjectKind.Shape,
                shapeId,
                SelectionPaneParityFixture.ShapeName,
                IsVisible: false,
                CanMoveUp: false,
                CanMoveDown: false));
    }

    [Fact]
    public void BuildItems_ListsVisibleObjectsTopToBottomWithDefaultNames()
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
    public void BuildItems_AcceptsHostSuppliedDefaultText()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.Ellipse
        };
        sheet.DrawingShapes.Add(shape);

        var items = SelectionPanePlanner.BuildItems(
            sheet,
            new SelectionPanePlannerText(
                "Localized Chart {0}",
                "Localized Picture {0}",
                "Localized Text {0}",
                "{0} #{1}",
                "Oval",
                "Connector",
                "Box"));

        items.Single().Name.Should().Be("Oval #1");
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
    public void CreateResult_PreservesVisibilityChangesWhenMoving()
    {
        var item = new SelectionPaneItem(
            SelectionPaneObjectKind.Picture,
            Guid.NewGuid(),
            "Picture 1",
            IsVisible: true,
            CanMoveUp: true,
            CanMoveDown: false);

        var result = SelectionPanePlanner.CreateResult(
            SelectionPaneDialogAction.MoveUp,
            item,
            [item],
            [new SelectionPaneItemState(item.Kind, item.Id, "Picture 1", IsVisible: false)],
            []);

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
    public void CreateResult_CapturesTrimmedRenameChanges()
    {
        var item = new SelectionPaneItem(
            SelectionPaneObjectKind.Shape,
            Guid.NewGuid(),
            "Rectangle 1",
            IsVisible: true,
            CanMoveUp: false,
            CanMoveDown: false);

        var result = SelectionPanePlanner.CreateResult(
            SelectionPaneDialogAction.ApplyVisibility,
            null,
            [item],
            [new SelectionPaneItemState(item.Kind, item.Id, "  Process Box  ", IsVisible: true)],
            []);

        result.RenameChanges.Should().Equal(new SelectionPaneRenameChange(
            SelectionPaneObjectKind.Shape,
            item.Id,
            "Process Box"));
    }

    [Fact]
    public void CreateResult_HandlesLargeStateListsWithIndexedLookups()
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
            .Select((item, index) => new SelectionPaneItemState(item.Kind, item.Id, item.Name, index % 4 == 0))
            .ToArray();

        var result = SelectionPanePlanner.CreateResult(
            SelectionPaneDialogAction.ApplyVisibility,
            null,
            items,
            states,
            []);
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
    public void FilterItems_AppliesSearchAndKindFilters()
    {
        var picture = State(SelectionPaneObjectKind.Picture, "Logo", isVisible: true);
        var hiddenShape = State(SelectionPaneObjectKind.Shape, "Process Box", isVisible: false);
        var textBox = State(SelectionPaneObjectKind.TextBox, "Quarter Notes", isVisible: true);

        var visibleMatches = SelectionPanePlanner.FilterItems(
            [picture, hiddenShape, textBox],
            "  notes  ",
            SelectionPaneFilterValues.Visible);
        var shapeMatches = SelectionPanePlanner.FilterItems(
            [picture, hiddenShape, textBox],
            "shape",
            SelectionPaneFilterValues.All);

        visibleMatches.Should().Equal(textBox);
        shapeMatches.Should().Equal(hiddenShape);
    }

    [Fact]
    public void FilterItems_ReturnsOriginalListForDefaultView()
    {
        var items = new[]
        {
            State(SelectionPaneObjectKind.Picture, "Logo"),
            State(SelectionPaneObjectKind.Shape, "Process Box", isVisible: false)
        };

        var filtered = SelectionPanePlanner.FilterItems(items, " ", "");

        filtered.Should().BeSameAs(items);
    }

    [Theory]
    [InlineData(SelectionPaneKeyboardKey.Up, true, SelectionPaneKeyboardAction.MoveUp)]
    [InlineData(SelectionPaneKeyboardKey.Down, true, SelectionPaneKeyboardAction.MoveDown)]
    [InlineData(SelectionPaneKeyboardKey.Up, false, SelectionPaneKeyboardAction.None)]
    [InlineData(SelectionPaneKeyboardKey.F2, false, SelectionPaneKeyboardAction.FocusRename)]
    [InlineData(SelectionPaneKeyboardKey.F2, true, SelectionPaneKeyboardAction.FocusRename)]
    [InlineData(SelectionPaneKeyboardKey.Space, false, SelectionPaneKeyboardAction.ToggleVisibility)]
    [InlineData(SelectionPaneKeyboardKey.Other, true, SelectionPaneKeyboardAction.None)]
    public void PlanKeyboardAction_MapsSelectionPaneShortcuts(
        SelectionPaneKeyboardKey key,
        bool hasControlModifier,
        SelectionPaneKeyboardAction expected) =>
        SelectionPanePlanner.PlanKeyboardAction(key, hasControlModifier).Should().Be(expected);

    [Fact]
    public void PlanDragReorder_CreatesAdjacentMovesAndNewOrder()
    {
        var front = State(SelectionPaneObjectKind.Picture);
        var middle = State(SelectionPaneObjectKind.Picture);
        var back = State(SelectionPaneObjectKind.Picture);

        var plan = SelectionPanePlanner.PlanDragReorder(
            [front, middle, back],
            draggedId: back.Id,
            targetId: front.Id);

        plan.Should().NotBeNull();
        plan!.OrderedIds.Should().Equal(back.Id, front.Id, middle.Id);
        plan.MoveChanges.Should().Equal(
            new SelectionPaneMoveChange(SelectionPaneObjectKind.Picture, back.Id, Forward: true),
            new SelectionPaneMoveChange(SelectionPaneObjectKind.Picture, back.Id, Forward: true));
    }

    [Fact]
    public void PlanDropVisual_AllowsMixedSupportedInsertionCue()
    {
        var front = State(SelectionPaneObjectKind.Picture, "Front");
        var back = State(SelectionPaneObjectKind.Shape, "Back");

        var plan = SelectionPanePlanner.PlanDropVisual(
            [front, back],
            draggedId: back.Id,
            targetId: front.Id,
            placement: SelectionPaneDropPlacement.Before);

        plan.Should().Be(new SelectionPaneDropVisualPlan(
            front.Id,
            SelectionPaneDropPlacement.Before,
            IsAllowed: true));
    }

    [Fact]
    public void PlanDropVisual_RejectsSameItemUnsupportedChartAndNoOpCues()
    {
        var front = State(SelectionPaneObjectKind.Picture, "Front");
        var back = State(SelectionPaneObjectKind.Picture, "Back");
        var chart = State(SelectionPaneObjectKind.Chart, "Chart");

        var sameItem = SelectionPanePlanner.PlanDropVisual(
            [front, chart],
            draggedId: front.Id,
            targetId: front.Id,
            placement: SelectionPaneDropPlacement.After);
        var chartCue = SelectionPanePlanner.PlanDropVisual(
            [front, chart],
            draggedId: front.Id,
            targetId: chart.Id,
            placement: SelectionPaneDropPlacement.After);
        var noOpCue = SelectionPanePlanner.PlanDropVisual(
            [front, back],
            draggedId: front.Id,
            targetId: back.Id,
            placement: SelectionPaneDropPlacement.Before);

        sameItem.Should().Be(new SelectionPaneDropVisualPlan(
            front.Id,
            SelectionPaneDropPlacement.After,
            IsAllowed: false));
        // Charts are now a supported Selection Pane z-order participant (matching Excel, which lets
        // you drag a chart to reorder it alongside shapes/pictures/textboxes in the pane), so a drop
        // cue targeting a chart is allowed rather than rejected.
        chartCue.Should().Be(new SelectionPaneDropVisualPlan(
            chart.Id,
            SelectionPaneDropPlacement.After,
            IsAllowed: true));
        noOpCue.Should().Be(new SelectionPaneDropVisualPlan(
            back.Id,
            SelectionPaneDropPlacement.Before,
            IsAllowed: false));
    }

    [Fact]
    public void CreateDragMoveChanges_RejectsChartMixedDrops()
    {
        var picture = Guid.NewGuid();
        var chart = Guid.NewGuid();

        var moves = SelectionPanePlanner.CreateDragMoveChanges(
            [
                (SelectionPaneObjectKind.Picture, picture),
                (SelectionPaneObjectKind.Chart, chart)
            ],
            draggedId: picture,
            targetId: chart);

        moves.Should().BeEmpty();
    }

    [Fact]
    public void CreateCommand_BuildsCompositeForAllChangeKinds()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var id = Guid.NewGuid();

        var command = SelectionPanePlanner.CreateCommand(
            sheet.Id,
            [new SelectionPaneVisibilityChange(SelectionPaneObjectKind.Picture, id, IsVisible: false)],
            [new SelectionPaneRenameChange(SelectionPaneObjectKind.Picture, id, "New")],
            [new SelectionPaneMoveChange(SelectionPaneObjectKind.Picture, id, Forward: true)]);

        command.Should().BeOfType<CompositeWorkbookCommand>();
    }

    [Fact]
    public void CreateCommand_ReturnsNullWhenNoChanges()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        SelectionPanePlanner.CreateCommand(sheet.Id, [], [], []).Should().BeNull();
    }

    [Fact]
    public void HasChanges_ReportsPendingVisibilityRenameOrMove()
    {
        SelectionPanePlanner.HasChanges([], [], []).Should().BeFalse();

        SelectionPanePlanner.HasChanges(
                [new SelectionPaneVisibilityChange(SelectionPaneObjectKind.Picture, Guid.NewGuid(), IsVisible: false)],
                [],
                [])
            .Should()
            .BeTrue();
        SelectionPanePlanner.HasChanges(
                [],
                [new SelectionPaneRenameChange(SelectionPaneObjectKind.Shape, Guid.NewGuid(), "Renamed")],
                [])
            .Should()
            .BeTrue();
        SelectionPanePlanner.HasChanges(
                [],
                [],
                [new SelectionPaneMoveChange(SelectionPaneObjectKind.TextBox, Guid.NewGuid(), Forward: true)])
            .Should()
            .BeTrue();
    }

    private static SelectionPaneItemState State(SelectionPaneObjectKind kind) =>
        State(kind, kind.ToString());

    private static SelectionPaneItemState State(
        SelectionPaneObjectKind kind,
        string name,
        bool isVisible = true) =>
        new(kind, Guid.NewGuid(), name, isVisible);
}
