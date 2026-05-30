using FluentAssertions;
using FreeX.Core.Model;
using System.Diagnostics;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed class SelectionPanePlannerTests
{
    [Fact]
    public void SelectionPaneDialogStatePlanner_FilterItems_AppliesSearchAndKindFilters()
    {
        var picture = DialogState(SelectionPaneObjectKind.Picture, "Logo", isVisible: true);
        var hiddenShape = DialogState(SelectionPaneObjectKind.Shape, "Process Box", isVisible: false);
        var textBox = DialogState(SelectionPaneObjectKind.TextBox, "Quarter Notes", isVisible: true);

        var visibleMatches = SelectionPaneDialogStatePlanner.FilterItems(
            [picture, hiddenShape, textBox],
            "  notes  ",
            "Visible");
        var shapeMatches = SelectionPaneDialogStatePlanner.FilterItems(
            [picture, hiddenShape, textBox],
            "shape",
            "All");

        visibleMatches.Should().Equal(textBox);
        shapeMatches.Should().Equal(hiddenShape);
    }

    [Fact]
    public void SelectionPaneDialogStatePlanner_FindSameKindMoveTargetIndex_SkipsOtherKinds()
    {
        var frontPicture = DialogState(SelectionPaneObjectKind.Picture, "Front", isVisible: true);
        var shape = DialogState(SelectionPaneObjectKind.Shape, "Shape", isVisible: true);
        var backPicture = DialogState(SelectionPaneObjectKind.Picture, "Back", isVisible: true);

        var forwardTarget = SelectionPaneDialogStatePlanner.FindSameKindMoveTargetIndex(
            [frontPicture, shape, backPicture],
            currentIndex: 2,
            forward: true);
        var backwardTarget = SelectionPaneDialogStatePlanner.FindSameKindMoveTargetIndex(
            [frontPicture, shape, backPicture],
            currentIndex: 0,
            forward: false);

        forwardTarget.Should().Be(0);
        backwardTarget.Should().Be(2);
    }

    [Fact]
    public void SelectionPaneDialogStatePlanner_PlanMove_ReordersAgainstSameKindTarget()
    {
        var frontPicture = DialogState(SelectionPaneObjectKind.Picture, "Front", isVisible: true);
        var shape = DialogState(SelectionPaneObjectKind.Shape, "Shape", isVisible: true);
        var backPicture = DialogState(SelectionPaneObjectKind.Picture, "Back", isVisible: true);

        var plan = SelectionPaneDialogStatePlanner.PlanMove(
            [frontPicture, shape, backPicture],
            backPicture.Id,
            forward: true);

        plan.Should().NotBeNull();
        plan!.OrderedIds.Should().Equal(backPicture.Id, shape.Id, frontPicture.Id);
        plan.MoveChanges.Should().Equal(new SelectionPaneMoveChange(
            SelectionPaneObjectKind.Picture,
            backPicture.Id,
            Forward: true));
    }

    [Fact]
    public void SelectionPaneDialogStatePlanner_PlanDragReorder_ReordersAndPlansAdjacentMoves()
    {
        var front = DialogState(SelectionPaneObjectKind.Picture, "Front", isVisible: true);
        var middle = DialogState(SelectionPaneObjectKind.Picture, "Middle", isVisible: true);
        var back = DialogState(SelectionPaneObjectKind.Picture, "Back", isVisible: true);

        var plan = SelectionPaneDialogStatePlanner.PlanDragReorder(
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
    public void SelectionPaneDialogStatePlanner_PlanDragReorder_CanInsertAfterTarget()
    {
        var front = DialogState(SelectionPaneObjectKind.Picture, "Front", isVisible: true);
        var middle = DialogState(SelectionPaneObjectKind.Picture, "Middle", isVisible: true);
        var back = DialogState(SelectionPaneObjectKind.Picture, "Back", isVisible: true);

        var plan = SelectionPaneDialogStatePlanner.PlanDragReorder(
            [front, middle, back],
            draggedId: front.Id,
            targetId: back.Id,
            placement: SelectionPaneDropPlacement.After);

        plan.Should().NotBeNull();
        plan!.OrderedIds.Should().Equal(middle.Id, back.Id, front.Id);
        plan.MoveChanges.Should().Equal(
            new SelectionPaneMoveChange(SelectionPaneObjectKind.Picture, front.Id, Forward: false),
            new SelectionPaneMoveChange(SelectionPaneObjectKind.Picture, front.Id, Forward: false));
    }

    [Fact]
    public void SelectionPaneDialogStatePlanner_PlanDragReorder_HandlesLargeListsWithConsolidatedLookup()
    {
        const int itemCount = 5_000;
        var items = Enumerable.Range(0, itemCount)
            .Select(index => DialogState(SelectionPaneObjectKind.Picture, $"Picture {index}", isVisible: true))
            .ToArray();
        var dragged = items[^1];
        var target = items[0];

        var plan = SelectionPaneDialogStatePlanner.PlanDragReorder(
            items,
            draggedId: dragged.Id,
            targetId: target.Id);

        plan.Should().NotBeNull();
        plan!.OrderedIds[0].Should().Be(dragged.Id);
        plan.OrderedIds[1].Should().Be(target.Id);
        plan.OrderedIds.Should().HaveCount(itemCount);
        plan.MoveChanges.Should().HaveCount(itemCount - 1);
        plan.MoveChanges.Should().OnlyContain(move =>
            move.Kind == SelectionPaneObjectKind.Picture &&
            move.Id == dragged.Id &&
            move.Forward);
    }

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
    public void SelectionPaneDialog_FilterItems_ReturnsOriginalListForDefaultView()
    {
        var items = new[]
        {
            DialogState(SelectionPaneObjectKind.Picture, "Logo", isVisible: true),
            DialogState(SelectionPaneObjectKind.Shape, "Process Box", isVisible: false)
        };

        var filtered = SelectionPaneDialogStatePlanner.FilterItems(items, " ", "");

        filtered.Should().BeSameAs(items);
    }

    [Fact]
    public void SelectionPaneDialog_PlannerAvoidsLinqScaffoldingInRepeatedStatePaths()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "SelectionPaneDialog.Planning.cs"));

        var filterItems = SourceMethod(
            source,
            "public static IReadOnlyList<SelectionPaneDialogItemState> FilterItems",
            "public static SelectionPaneDialogReorderPlan? PlanMove");
        var createVisibilityChanges = SourceMethod(
            source,
            "public static IReadOnlyList<SelectionPaneVisibilityChange> CreateVisibilityChanges",
            "public static IReadOnlyList<SelectionPaneRenameChange> CreateRenameChanges");
        var createRenameChanges = SourceMethod(
            source,
            "public static IReadOnlyList<SelectionPaneRenameChange> CreateRenameChanges",
            "public static SelectionPaneDialogResult CreateResult");

        filterItems.Should().NotContain(".Where(");
        filterItems.Should().NotContain(".ToList(");
        createVisibilityChanges.Should().NotContain(".Where(");
        createVisibilityChanges.Should().NotContain(".Select(");
        createVisibilityChanges.Should().NotContain("states[item.Id]");
        createRenameChanges.Should().NotContain(".Where(");
        createRenameChanges.Should().NotContain(".Select(");
        createRenameChanges.Should().NotContain("names[item.Id]");
    }

    [Fact]
    public void Benchmark_SelectionPaneDefaultFilter_AvoidsCopyAllocation()
    {
        const int itemCount = 10_000;
        var items = Enumerable.Range(0, itemCount)
            .Select(index => DialogState(SelectionPaneObjectKind.Picture, $"Picture {index}", isVisible: true))
            .ToArray();

        SelectionPaneDialogStatePlanner.FilterItems(items, "", "All").Should().BeSameAs(items);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < 1_000; index++)
        {
            if (!ReferenceEquals(SelectionPaneDialogStatePlanner.FilterItems(items, "", "All"), items))
                throw new InvalidOperationException("Default Selection Pane filtering should return the source list.");
        }

        stopwatch.Stop();

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine(
            $"Selection pane default filter: {stopwatch.Elapsed.TotalMilliseconds:F2}ms, {allocated:N0} bytes for 1000 runs");

        allocated.Should().BeLessThan(200_000);
    }

    [Fact]
    public void SelectionPaneDialog_PlannerUsesIndexedLookupsForStateProjection()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "SelectionPaneDialog.Planning.cs"));

        source.Should().Contain("private static IReadOnlyList<(Guid Id, bool IsVisible, string Name)> ToNamedCurrentStates");
        source.Should().Contain("TryGetValue(state.Id");
        source.Should().NotContain("originalItems.FirstOrDefault(item => item.Id == state.Id)");
        source.Should().NotContain("itemsById.ContainsKey(state.Id)");
    }

    [Fact]
    public void SelectionPaneDialog_PlannerConsolidatesDragReorderIndexLookups()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "SelectionPaneDialog.Planning.cs"));

        source.Should().Contain("private static (int DraggedIndex, int TargetIndex) FindDragIndexes");
        source.Should().Contain("var dragPlan = CreateDragMovePlan(items, draggedId, targetId, placement);");
        source.Should().NotContain("items.Select(item => (item.Kind, item.Id)).ToList()");
        source.Should().NotContain("var draggedIndex = FindIndex(items, draggedId);");
        source.Should().NotContain("var targetIndex = FindIndex(items, targetId);");
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
    public void SelectionPaneDialog_CreateDragMoveChanges_RejectsCrossKindDrops()
    {
        var picture = Guid.NewGuid();
        var shape = Guid.NewGuid();

        var moves = SelectionPaneDialog.CreateDragMoveChanges(
            [
                (SelectionPaneObjectKind.Picture, picture),
                (SelectionPaneObjectKind.Shape, shape)
            ],
            draggedId: picture,
            targetId: shape);

        moves.Should().BeEmpty();
    }

    [Fact]
    public void SelectionPaneDialog_ExposesShowAllAndHideAllBulkButtons()
    {
        var source = ReadSelectionPaneDialogSources();

        source.Should().Contain("_showAllButton");
        source.Should().Contain("_hideAllButton");
        source.Should().Contain("SetAllVisibility(true)");
        source.Should().Contain("SetAllVisibility(false)");
    }

    [Fact]
    public void SelectionPaneDialog_ExposesKeyboardAccessKeysForCommandButtons()
    {
        var source = ReadSelectionPaneDialogSources();

        source.Should().Contain("Content = UiText.Get(\"SelectionPane_BringForwardButton\")");
        source.Should().Contain("Content = UiText.Get(\"SelectionPane_SendBackwardButton\")");
        source.Should().Contain("Content = UiText.Get(\"SelectionPane_ShowAllButton\")");
        source.Should().Contain("Content = UiText.Get(\"SelectionPane_HideAllButton\")");
        source.Should().Contain("Content = UiText.Ok");
        source.Should().Contain("Content = UiText.Cancel");
    }

    [Fact]
    public void SelectionPaneDialog_ExposesSearchFilterRenameAndEyeLikeVisibilityAffordances()
    {
        var source = ReadSelectionPaneDialogSources();

        source.Should().Contain("_searchBox");
        source.Should().Contain("_filterBox");
        source.Should().Contain("_renameBox");
        source.Should().Contain("Content = UiText.Get(\"SelectionPane_NameLabel\")");
        source.Should().Contain("_renameButton");
        source.Should().Contain("_toggleVisibilityButton");
        source.Should().Contain("CreateEyeIcon()");
        source.Should().NotContain("Content = \"Eye\"");
        source.Should().Contain("ApplySearchAndFilter");
        source.Should().Contain("RenameSelectedItem");
        source.Should().Contain("ToggleSelectedVisibility");
        source.Should().Contain("ToolTip = UiText.Get(\"SelectionPane_ToggleVisibilityToolTip\")");
    }

    [Fact]
    public void SelectionPaneDialog_ObjectListExposesAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SelectionPaneDialog.cs"));

        foreach (var key in new[]
        {
            "SelectionPane_ObjectListAutomationName",
            "SelectionPane_ObjectListHelpText",
            "SelectionPane_SearchAutomationName",
            "SelectionPane_SearchHelpText",
            "SelectionPane_FilterAutomationName",
            "SelectionPane_FilterHelpText",
            "SelectionPane_ObjectNameAutomationName",
            "SelectionPane_ObjectNameHelpText",
            "SelectionPane_RenameButtonAutomationName",
            "SelectionPane_RenameButtonHelpText",
            "SelectionPane_ToggleVisibilityAutomationName",
            "SelectionPane_ToggleVisibilityHelpText",
            "SelectionPane_BringForwardAutomationName",
            "SelectionPane_BringForwardHelpText",
            "SelectionPane_SendBackwardAutomationName",
            "SelectionPane_SendBackwardHelpText",
            "SelectionPane_ShowAllAutomationName",
            "SelectionPane_ShowAllHelpText",
            "SelectionPane_HideAllAutomationName",
            "SelectionPane_HideAllHelpText",
            "SelectionPane_OkAutomationName",
            "SelectionPane_OkHelpText",
            "SelectionPane_CancelAutomationName",
            "SelectionPane_CancelHelpText",
            "SelectionPane_ItemVisibilityAutomationName",
            "SelectionPane_ItemVisibilityHelpText"
        })
        {
            source.Should().Contain($"UiText.Get(\"{key}\")");
        }
    }

    [Fact]
    public void SelectionPaneDialogOpenedFromKeyboard_FocusesSearchBox()
    {
        var source = ReadSelectionPaneDialogSources();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_searchBox);");
    }

    [Fact]
    public void SelectionPaneDialog_AllowsInlineRenameInObjectList()
    {
        var source = ReadSelectionPaneDialogSources();

        source.Should().Contain("new FrameworkElementFactory(typeof(TextBox))");
        source.Should().Contain("TextBox.TextProperty");
        source.Should().Contain("UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged");
        source.Should().Contain("ToolTipProperty, UiText.Get(\"SelectionPane_ItemRenameToolTip\")");
    }

    [Fact]
    public void SelectionPaneDialog_ListKeyboardShortcutsRenameAndToggleVisibility()
    {
        var source = ReadSelectionPaneDialogSources();

        source.Should().Contain("_list.KeyDown += List_KeyDown;");
        source.Should().Contain("private void List_KeyDown(object sender, KeyEventArgs e)");
        source.Should().Contain("if (e.Key == Key.F2)");
        source.Should().Contain("FocusRenameBox();");
        source.Should().Contain("if (e.Key == Key.Space)");
        source.Should().Contain("ToggleSelectedVisibility();");
        source.Should().Contain("private void FocusRenameBox()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_renameBox);");
    }

    [Fact]
    public void SelectionPaneDialog_AccumulatesMoveChangesInsteadOfClosingOnMove()
    {
        var source = ReadSelectionPaneDialogSources();
        var hostSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Drawing.cs"));

        source.Should().Contain("private readonly List<SelectionPaneMoveChange> _moveChanges = [];");
        source.Should().Contain("SelectionPaneDialogStatePlanner.PlanMove");
        source.Should().Contain("_moveChanges.AddRange(plan.MoveChanges)");
        source.Should().Contain("ApplySearchAndFilter(selected.Source.Id)");
        var acceptMoveBody = source.Substring(
            source.IndexOf("private void AcceptMove", StringComparison.Ordinal),
            source.IndexOf("private IReadOnlyList<SelectionPaneVisibilityChange>", StringComparison.Ordinal) -
            source.IndexOf("private void AcceptMove", StringComparison.Ordinal));
        acceptMoveBody.Should().NotContain("DialogResult = true");
        hostSource.Should().Contain("result.MoveChanges.Select");
        hostSource.Should().NotContain("SelectionPaneDialogAction.MoveUp when dialog.Result.Target");
    }

    [Fact]
    public void SelectionPaneDialog_SupportsDragDropReorder()
    {
        var source = ReadSelectionPaneDialogSources();

        source.Should().Contain("_list.AllowDrop = true");
        source.Should().Contain("_list.PreviewMouseLeftButtonDown");
        source.Should().Contain("_list.MouseMove");
        source.Should().Contain("_list.DragOver");
        source.Should().Contain("_list.Drop");
        source.Should().Contain("DragDrop.DoDragDrop");
        source.Should().Contain("SelectionPaneDialogStatePlanner.PlanDragReorder");
        source.Should().Contain("GetDropPlacement");
        source.Should().Contain("SelectionPaneDropPlacement.After");
        source.Should().Contain("CreateDragMoveChanges");
    }

    private static string ReadSelectionPaneDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SelectionPaneDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SelectionPaneDialog.State.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SelectionPaneDialog.Planning.cs")));

    private static string SourceMethod(string source, string start, string end) =>
        source[source.IndexOf(start, StringComparison.Ordinal)..source.IndexOf(end, StringComparison.Ordinal)];

    private static SelectionPaneDialogItemState DialogState(
        SelectionPaneObjectKind kind,
        string name,
        bool isVisible) =>
        new(kind, Guid.NewGuid(), name, isVisible);
}
