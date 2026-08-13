using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class SelectionPaneInteractionSourceTests
{
    [Fact]
    public void SelectionPane_UsesSharedSessionForKeyboardFilteringAndMutations()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.SelectionPane.cs"));

        source.Should().Contain("SelectionPaneSession.Create(");
        source.Should().Contain("session.SetView(");
        source.Should().Contain("session.HandleKeyboard(");
        source.Should().Contain("session.MoveSelected(");
        source.Should().Contain("session.RenameSelected(");
        source.Should().Contain("session.ToggleSelectedVisibility(");
        source.Should().Contain("session.SetAllVisibility(");
        source.Should().Contain("session.CreateCommand(");
        source.Should().Contain("ToSelectionPaneKeyboardKey(e.Key)");
        source.Should().Contain("KeyModifiers.Control");
        source.Should().NotContain("SelectionPanePlanner.FilterItems(");
        source.Should().NotContain("SelectionPanePlanner.PlanKeyboardAction(");
        source.Should().NotContain("SelectionPanePlanner.PlanMove(");
        source.Should().NotContain("SelectionPanePlanner.CreateCommand(");
        source.Should().NotContain("ToItemStates(");
        source.Should().NotContain("var moveChanges =");
        source.Should().NotContain("bool MatchesFilter(SelectionPaneRow row)");
    }

    [Fact]
    public void SelectionPane_PointerDragUsesPlannerAndClearsCaptureState()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.SelectionPane.cs"));

        source.Should().Contain("InputElement.PointerPressedEvent");
        source.Should().Contain("listBox.PointerMoved");
        source.Should().Contain("listBox.PointerReleased");
        source.Should().Contain("listBox.PointerCaptureLost");
        source.Should().Contain("session.BeginDrag(");
        source.Should().Contain("session.UpdateDrag(");
        source.Should().Contain("session.Drop(");
        source.Should().Contain("session.ClearDropVisual(");
        source.Should().Contain("session.CancelDrag(");
        source.Should().Contain("SelectionPaneDropPlacement.After");
        source.Should().Contain("nameof(SelectionPaneRow.IsDropBefore)");
        source.Should().Contain("nameof(SelectionPaneRow.IsDropAfter)");
        source.Should().Contain("ClearDragState(releasePointer: true)");
        source.Should().Contain("ClearDragState(releasePointer: false)");
        source.Should().NotContain("SelectionPanePlanner.PlanDropVisual(");
        source.Should().NotContain("SelectionPanePlanner.PlanDragReorder(");
    }

    [Fact]
    public void SelectionPane_RetainsWpfSelectedRowsSeparatorsAndMetrics()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.SelectionPane.cs"));

        source.Should().Contain("Brush(246, 246, 246)");
        source.Should().Contain("Brush(218, 218, 218)");
        source.Should().Contain("new GridLength(32)");
        source.Should().Contain("new GridLength(160)");
        source.Should().Contain("buttonRow.Spacing = 6;");
        source.Should().Contain("new GridLength(37)");
        source.Should().Contain("listBox.SelectedItem = session.SelectedId is { } selectedId");
        source.Should().Contain("filtered.FirstOrDefault(row => row.Id == selectedId)");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
