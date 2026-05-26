using FluentAssertions;
using Freexcel.Core.Model;
using System.IO;

namespace Freexcel.App.Host.Tests;

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

        source.Should().Contain("Content = \"_Bring Forward\"");
        source.Should().Contain("Content = \"Send _Backward\"");
        source.Should().Contain("Content = \"Show _All\"");
        source.Should().Contain("Content = \"_Hide All\"");
        source.Should().Contain("Content = \"_OK\"");
        source.Should().Contain("Content = \"_Cancel\"");
    }

    [Fact]
    public void SelectionPaneDialog_ExposesSearchFilterRenameAndEyeLikeVisibilityAffordances()
    {
        var source = ReadSelectionPaneDialogSources();

        source.Should().Contain("_searchBox");
        source.Should().Contain("_filterBox");
        source.Should().Contain("_renameBox");
        source.Should().Contain("Content = \"_Name:\"");
        source.Should().Contain("_renameButton");
        source.Should().Contain("_toggleVisibilityButton");
        source.Should().Contain("CreateEyeIcon()");
        source.Should().NotContain("Content = \"Eye\"");
        source.Should().Contain("ApplySearchAndFilter");
        source.Should().Contain("RenameSelectedItem");
        source.Should().Contain("ToggleSelectedVisibility");
        source.Should().Contain("ToolTip = \"Toggle visibility\"");
    }

    [Fact]
    public void SelectionPaneDialogOpenedFromKeyboard_FocusesSearchBox()
    {
        var source = ReadSelectionPaneDialogSources();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_searchBox.Focus();");
        source.Should().Contain("_searchBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_searchBox);");
    }

    [Fact]
    public void SelectionPaneDialog_AllowsInlineRenameInObjectList()
    {
        var source = ReadSelectionPaneDialogSources();

        source.Should().Contain("new FrameworkElementFactory(typeof(TextBox))");
        source.Should().Contain("TextBox.TextProperty");
        source.Should().Contain("UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged");
        source.Should().Contain("ToolTipProperty, \"Rename object\"");
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
        source.Should().Contain("_renameBox.Focus();");
        source.Should().Contain("_renameBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_renameBox);");
    }

    [Fact]
    public void SelectionPaneDialog_AccumulatesMoveChangesInsteadOfClosingOnMove()
    {
        var source = ReadSelectionPaneDialogSources();
        var hostSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Drawing.cs"));

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
        source.Should().Contain("CreateDragMoveChanges");
    }

    private static string ReadSelectionPaneDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SelectionPaneDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SelectionPaneDialog.State.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SelectionPaneDialog.Planning.cs")));

    private static SelectionPaneDialogItemState DialogState(
        SelectionPaneObjectKind kind,
        string name,
        bool isVisible) =>
        new(kind, Guid.NewGuid(), name, isVisible);
}
