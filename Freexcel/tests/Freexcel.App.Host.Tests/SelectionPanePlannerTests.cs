using FluentAssertions;
using Freexcel.Core.Model;
using System.IO;

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
    public void SelectionPaneDialog_ExposesShowAllAndHideAllBulkButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SelectionPaneDialog.cs"));

        source.Should().Contain("_showAllButton");
        source.Should().Contain("_hideAllButton");
        source.Should().Contain("SetAllVisibility(true)");
        source.Should().Contain("SetAllVisibility(false)");
    }

    [Fact]
    public void SelectionPaneDialog_ExposesKeyboardAccessKeysForCommandButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SelectionPaneDialog.cs"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SelectionPaneDialog.cs"));

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
    public void SelectionPaneDialog_AccumulatesMoveChangesInsteadOfClosingOnMove()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SelectionPaneDialog.cs"));
        var hostSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Drawing.cs"));

        source.Should().Contain("private readonly List<SelectionPaneMoveChange> _moveChanges = [];");
        source.Should().Contain("_moveChanges.Add(new SelectionPaneMoveChange");
        source.Should().Contain("ApplySearchAndFilter(selected.Source.Id)");
        var acceptMoveBody = source.Substring(
            source.IndexOf("private void AcceptMove", StringComparison.Ordinal),
            source.IndexOf("private IReadOnlyList<SelectionPaneVisibilityChange>", StringComparison.Ordinal) -
            source.IndexOf("private void AcceptMove", StringComparison.Ordinal));
        acceptMoveBody.Should().NotContain("DialogResult = true");
        hostSource.Should().Contain("result.MoveChanges.Select");
        hostSource.Should().NotContain("SelectionPaneDialogAction.MoveUp when dialog.Result.Target");
    }
}
