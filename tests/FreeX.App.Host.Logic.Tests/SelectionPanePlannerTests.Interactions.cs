using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class SelectionPanePlannerTests
{
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
    public void SelectionPaneDialog_ListKeyboardShortcutsRenameToggleVisibilityAndReorder()
    {
        var source = ReadSelectionPaneDialogSources();

        source.Should().Contain("_list.KeyDown += List_KeyDown;");
        source.Should().Contain("private void List_KeyDown(object sender, KeyEventArgs e)");
        source.Should().Contain("_session.HandleKeyboard(");
        source.Should().Contain("ToSelectionPaneKeyboardKey(e.Key)");
        source.Should().Contain("ModifierKeys.Control");
        source.Should().Contain("outcome.FocusRename");
        source.Should().Contain("outcome.StateChanged");
        source.Should().Contain("e.Handled = outcome.IsHandled");
        source.Should().NotContain("SelectionPanePlanner.PlanKeyboardAction(");
        source.Should().NotContain("TryHandleListReorderShortcut");
        source.Should().NotContain("if (e.Key == Key.F2)");
        source.Should().NotContain("if (e.Key == Key.Space)");
        source.Should().Contain("private void FocusRenameBox()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_renameBox);");
    }

    [Fact]
    public void SelectionPaneDialog_AccumulatesMoveChangesInsteadOfClosingOnMove()
    {
        var source = ReadSelectionPaneDialogSources();
        var hostSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Drawing.cs");

        source.Should().Contain("private readonly SelectionPaneSession _session;");
        source.Should().Contain("_session.MoveSelected(");
        source.Should().Contain("ApplySearchAndFilter(selected.Source.Id)");
        source.Should().NotContain("private readonly List<SelectionPaneMoveChange> _moveChanges");
        source.Should().NotContain("SelectionPanePlanner.PlanMove");
        var acceptMoveBody = source.Substring(
            source.IndexOf("private void AcceptMove", StringComparison.Ordinal),
            source.IndexOf("private void List_PreviewMouseLeftButtonDown", StringComparison.Ordinal) -
            source.IndexOf("private void AcceptMove", StringComparison.Ordinal));
        acceptMoveBody.Should().NotContain("DialogResult = true");
        hostSource.Should().Contain("SelectionPaneGroupedCommandPlanner.CreateCommand");
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
        source.Should().Contain("_session.BeginDrag(");
        source.Should().Contain("_session.UpdateDrag(");
        source.Should().Contain("_session.Drop(");
        source.Should().Contain("GetDropPlacement");
        source.Should().Contain("SelectionPaneDropPlacement.After");
        source.Should().Contain("CreateDragMoveChanges");
        source.Should().Contain("_session.ClearDropVisual(");
        source.Should().Contain("_session.CancelDrag(");
        source.Should().Contain("IsDropBefore");
        source.Should().Contain("IsDropAfter");
        source.Should().Contain("List_DragLeave");
        source.Should().Contain("ClearDropVisual");
    }

    [Fact]
    public void SelectionPaneDialog_MouseMoveClearsStaleDragStateWhenButtonReleased()
    {
        var source = ReadSelectionPaneDialogSources();
        var mouseMove = source[
            source.IndexOf("private void List_MouseMove", StringComparison.Ordinal)..
            source.IndexOf("private void List_DragOver", StringComparison.Ordinal)];

        mouseMove.Should().Contain("if (e.LeftButton != MouseButtonState.Pressed)");
        mouseMove.Should().Contain("ClearNativeDragState();");
        mouseMove.Should().Contain("_session.CancelDrag();");
        mouseMove.IndexOf("ClearNativeDragState();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseMove.IndexOf("if (_dragStartPoint is not { } start", StringComparison.Ordinal));
    }
}
