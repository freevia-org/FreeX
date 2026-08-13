using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class SelectionPaneSessionTests
{
    [Fact]
    public void ConstructorProjectsItemsAndSelectsFirstItem()
    {
        var front = Item(SelectionPaneObjectKind.Picture, "Front");
        var back = Item(SelectionPaneObjectKind.Shape, "Back", isVisible: false);

        var session = new SelectionPaneSession([front, back]);

        session.Items.Select(item => item.Id).Should().Equal(front.Id, back.Id);
        session.FilteredItems.Should().HaveCount(2);
        session.SelectedId.Should().Be(front.Id);
        session.CanMoveUp.Should().BeFalse();
        session.CanMoveDown.Should().BeTrue();
        session.CanRename.Should().BeTrue();
        session.CanToggleVisibility.Should().BeTrue();
        session.CanDelete.Should().BeTrue();
    }

    [Fact]
    public void SetViewOwnsFilteringAndSelectionFallback()
    {
        var picture = Item(SelectionPaneObjectKind.Picture, "Logo");
        var hiddenShape = Item(SelectionPaneObjectKind.Shape, "Process Box", isVisible: false);
        var textBox = Item(SelectionPaneObjectKind.TextBox, "Quarter Notes");
        var session = new SelectionPaneSession([picture, hiddenShape, textBox]);
        session.Select(hiddenShape.Id);

        var outcome = session.SetView("notes", SelectionPaneFilterValues.Visible);

        outcome.StateChanged.Should().BeTrue();
        session.FilteredItems.Select(item => item.Id).Should().Equal(textBox.Id);
        session.SelectedId.Should().Be(textBox.Id);
    }

    [Fact]
    public void InlineMutationKeepsSelectionStableUntilRendererRefreshesView()
    {
        var picture = Item(SelectionPaneObjectKind.Picture, "Logo");
        var session = new SelectionPaneSession([picture]);
        session.SetView("logo", SelectionPaneFilterValues.All);

        session.SetName(picture.Id, "Brand Mark");

        session.SelectedId.Should().Be(picture.Id);
        session.FilteredItems.Should().ContainSingle();
        session.SetView("logo", SelectionPaneFilterValues.All);
        session.FilteredItems.Should().BeEmpty();
        session.SelectedId.Should().BeNull();
    }

    [Fact]
    public void VisibilityRenameAndShowHideAllMutatePortableStateAndResult()
    {
        var picture = Item(SelectionPaneObjectKind.Picture, "Logo");
        var shape = Item(SelectionPaneObjectKind.Shape, "Process Box", isVisible: false);
        var session = new SelectionPaneSession([picture, shape]);

        session.RenameSelected("  Brand Mark  ").StateChanged.Should().BeTrue();
        session.ToggleSelectedVisibility().StateChanged.Should().BeTrue();
        session.SetAllVisibility(isVisible: true).StateChanged.Should().BeTrue();

        var result = session.CreateResult();
        result.RenameChanges.Should().Equal(new SelectionPaneRenameChange(
            SelectionPaneObjectKind.Picture,
            picture.Id,
            "Brand Mark"));
        result.VisibilityChanges.Should().Equal(new SelectionPaneVisibilityChange(
            SelectionPaneObjectKind.Shape,
            shape.Id,
            IsVisible: true));
        session.FindItem(picture.Id)!.Name.Should().Be("Brand Mark");
        session.Items.Should().OnlyContain(item => item.IsVisible);
    }

    [Fact]
    public void MoveAndDropOwnOrderSelectionAndAccumulatedMoves()
    {
        var front = Item(SelectionPaneObjectKind.Picture, "Front");
        var middle = Item(SelectionPaneObjectKind.Shape, "Middle");
        var back = Item(SelectionPaneObjectKind.TextBox, "Back");
        var session = new SelectionPaneSession([front, middle, back]);
        session.Select(back.Id);

        session.MoveSelected(forward: true).StateChanged.Should().BeTrue();
        session.Items.Select(item => item.Id).Should().Equal(front.Id, back.Id, middle.Id);
        session.BeginDrag(front.Id).IsHandled.Should().BeTrue();
        session.UpdateDrag(middle.Id, SelectionPaneDropPlacement.After).StateChanged.Should().BeTrue();
        session.Drop(middle.Id, SelectionPaneDropPlacement.After).StateChanged.Should().BeTrue();

        session.Items.Select(item => item.Id).Should().Equal(back.Id, middle.Id, front.Id);
        session.SelectedId.Should().Be(front.Id);
        session.MoveChanges.Should().HaveCount(3);
        session.DropVisual.Should().BeNull();
        session.DraggedId.Should().BeNull();
    }

    [Theory]
    [InlineData(SelectionPaneKeyboardKey.Up, true, true, false)]
    [InlineData(SelectionPaneKeyboardKey.F2, false, false, true)]
    [InlineData(SelectionPaneKeyboardKey.Space, false, true, false)]
    [InlineData(SelectionPaneKeyboardKey.Other, false, false, false)]
    public void HandleKeyboardReturnsPortableOutcomes(
        SelectionPaneKeyboardKey key,
        bool hasControlModifier,
        bool stateChanged,
        bool focusRename)
    {
        var front = Item(SelectionPaneObjectKind.Picture, "Front");
        var back = Item(SelectionPaneObjectKind.Picture, "Back");
        var session = new SelectionPaneSession([front, back]);
        session.Select(back.Id);

        var outcome = session.HandleKeyboard(key, hasControlModifier);

        outcome.StateChanged.Should().Be(stateChanged);
        outcome.FocusRename.Should().Be(focusRename);
        outcome.IsHandled.Should().Be(key != SelectionPaneKeyboardKey.Other);
    }

    [Fact]
    public void CreateCommandBuildsOnePortableCompositeForPendingChanges()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var picture = Item(SelectionPaneObjectKind.Picture, "Logo");
        var session = new SelectionPaneSession([picture]);
        session.RenameSelected("Brand Mark");
        session.ToggleSelectedVisibility();

        var command = session.CreateCommand(sheet.Id);

        command.Should().BeOfType<CompositeWorkbookCommand>();
        session.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void DeleteRemovesTheItemAndProjectsADeleteCommand()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var front = Item(SelectionPaneObjectKind.Picture, "Front");
        var back = Item(SelectionPaneObjectKind.Shape, "Back");
        var session = new SelectionPaneSession([front, back]);
        session.Select(back.Id);
        session.MoveSelected(forward: true);

        var outcome = session.HandleKeyboard(SelectionPaneKeyboardKey.Delete, hasControlModifier: false);

        outcome.Should().Be(SelectionPaneSessionOutcome.Changed);
        session.Items.Select(item => item.Id).Should().Equal(front.Id);
        session.DeleteChanges.Should().Equal(new SelectionPaneDeleteChange(back.Kind, back.Id));
        session.MoveChanges.Should().NotContain(change => change.Id == back.Id);
        session.CreateResult().DeleteChanges.Should().Equal(new SelectionPaneDeleteChange(back.Kind, back.Id));
        session.CreateCommand(sheet.Id).Should().BeOfType<CompositeWorkbookCommand>()
            .Which.Commands.Should().ContainSingle(command => command is DeleteDrawingObjectCommand);
    }

    [Fact]
    public void InvalidDragAndBlankRenameReturnPortableNoOpOutcomes()
    {
        var picture = Item(SelectionPaneObjectKind.Picture, "Picture");
        var session = new SelectionPaneSession([picture]);

        session.RenameSelected("  ").IsHandled.Should().BeFalse();
        session.BeginDrag(picture.Id);
        session.UpdateDrag(picture.Id, SelectionPaneDropPlacement.Before).IsHandled.Should().BeTrue();
        session.Drop(picture.Id, SelectionPaneDropPlacement.Before).StateChanged.Should().BeFalse();
        session.Items.Select(item => item.Id).Should().Equal(picture.Id);
        session.MoveChanges.Should().BeEmpty();
    }

    private static SelectionPaneItem Item(
        SelectionPaneObjectKind kind,
        string name,
        bool isVisible = true) =>
        new(
            kind,
            Guid.NewGuid(),
            name,
            isVisible,
            CanMoveUp: true,
            CanMoveDown: true);
}
