using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class SelectionPaneInteractionSourceTests
{
    [Fact]
    public void SelectionPane_UsesSharedPlannerForKeyboardAndFiltering()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.SelectionPane.cs"));

        source.Should().Contain("SelectionPanePlanner.FilterItems(");
        source.Should().Contain("SelectionPanePlanner.PlanKeyboardAction(");
        source.Should().Contain("ToSelectionPaneKeyboardKey(e.Key)");
        source.Should().Contain("KeyModifiers.Control");
        source.Should().Contain("SelectionPaneKeyboardAction.MoveUp");
        source.Should().Contain("SelectionPaneKeyboardAction.MoveDown");
        source.Should().Contain("SelectionPaneKeyboardAction.FocusRename");
        source.Should().Contain("SelectionPaneKeyboardAction.ToggleVisibility");
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
        source.Should().Contain("SelectionPanePlanner.PlanDropVisual(");
        source.Should().Contain("SelectionPanePlanner.PlanDragReorder(");
        source.Should().Contain("SelectionPaneDropPlacement.After");
        source.Should().Contain("row.IsDropBefore");
        source.Should().Contain("row.IsDropAfter");
        source.Should().Contain("ClearDragState(releasePointer: true)");
        source.Should().Contain("ClearDragState(releasePointer: false)");
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
        source.Should().Contain("listBox.SelectedItem = filtered.FirstOrDefault();");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
