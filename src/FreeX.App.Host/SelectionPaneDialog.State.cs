using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class SelectionPaneDialog
{
    private void AcceptVisibility()
    {
        Result = new SelectionPaneDialogResult(
            SelectionPaneDialogAction.ApplyVisibility,
            null,
            CurrentVisibilityChanges(),
            CurrentRenameChanges(),
            _moveChanges.ToList(),
            _deleteChanges.ToList());
        DialogResult = true;
    }

    private void AcceptMove(SelectionPaneDialogAction action)
    {
        if (_list.SelectedItem is not SelectionPaneDialogItem selected)
            return;

        var forward = action == SelectionPaneDialogAction.MoveUp;
        var plan = SelectionPanePlanner.PlanMove(CurrentItemStates(), selected.Source.Id, forward);
        if (plan is null)
            return;

        _moveChanges.AddRange(plan.MoveChanges);
        ApplyReorderPlan(plan);
        ApplySearchAndFilter(selected.Source.Id);
    }

    private void List_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(_list);
        _dragItem = FindListItem(e.OriginalSource);
    }

    private void List_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            _dragStartPoint = null;
            _dragItem = null;
            return;
        }

        if (_dragStartPoint is not { } start ||
            _dragItem is null)
            return;

        var current = e.GetPosition(_list);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        DragDrop.DoDragDrop(_list, _dragItem, DragDropEffects.Move);
        _dragStartPoint = null;
        _dragItem = null;
    }

    private void List_DragOver(object sender, DragEventArgs e)
    {
        var dragged = e.Data.GetData(typeof(SelectionPaneDialogItem)) as SelectionPaneDialogItem;
        var targetContainer = FindListBoxItem(e.OriginalSource);
        var target = targetContainer?.DataContext as SelectionPaneDialogItem;
        var placement = targetContainer is null ? SelectionPaneDropPlacement.Before : GetDropPlacement(e, targetContainer);
        var visualPlan = dragged is null || target is null
            ? null
            : SelectionPanePlanner.PlanDropVisual(
                CurrentItemStates(),
                dragged.Source.Id,
                target.Source.Id,
                placement);
        ApplyDropVisual(visualPlan);
        e.Effects = visualPlan?.IsAllowed == true ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void List_DragLeave(object sender, DragEventArgs e)
    {
        if (!IsPointerOverList(e))
            ClearDropVisual();
    }

    private void List_Drop(object sender, DragEventArgs e)
    {
        var dragged = e.Data.GetData(typeof(SelectionPaneDialogItem)) as SelectionPaneDialogItem;
        var targetContainer = FindListBoxItem(e.OriginalSource);
        var target = targetContainer?.DataContext as SelectionPaneDialogItem;
        if (!CanDropDraggedItem(dragged, target))
        {
            ClearDropVisual();
            return;
        }

        ClearDropVisual();
        DragReorder(dragged!, target!, GetDropPlacement(e, targetContainer!));
        e.Handled = true;
    }

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        // Delete inside the row's inline name TextBox (or the search/rename boxes, which don't
        // route through this handler at all since they aren't descendants of _list) must delete
        // TEXT, not the whole object -- unlike F2/Space, Delete is destructive and has an obvious,
        // frequently-used meaning inside a text editor, so it needs an explicit guard here.
        if (e.OriginalSource is TextBox)
            return;

        var action = SelectionPanePlanner.PlanKeyboardAction(
            ToSelectionPaneKeyboardKey(e.Key),
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control);
        switch (action)
        {
            case SelectionPaneKeyboardAction.MoveUp:
                AcceptMove(SelectionPaneDialogAction.MoveUp);
                e.Handled = true;
                break;
            case SelectionPaneKeyboardAction.MoveDown:
                AcceptMove(SelectionPaneDialogAction.MoveDown);
                e.Handled = true;
                break;
            case SelectionPaneKeyboardAction.FocusRename:
                FocusRenameBox();
                e.Handled = true;
                break;
            case SelectionPaneKeyboardAction.ToggleVisibility:
                ToggleSelectedVisibility();
                e.Handled = true;
                break;
            case SelectionPaneKeyboardAction.Delete:
                DeleteSelectedItem();
                e.Handled = true;
                break;
        }
    }

    private static SelectionPaneKeyboardKey ToSelectionPaneKeyboardKey(Key key) =>
        key switch
        {
            Key.F2 => SelectionPaneKeyboardKey.F2,
            Key.Space => SelectionPaneKeyboardKey.Space,
            Key.Up => SelectionPaneKeyboardKey.Up,
            Key.Down => SelectionPaneKeyboardKey.Down,
            Key.Delete => SelectionPaneKeyboardKey.Delete,
            _ => SelectionPaneKeyboardKey.Other
        };

    private void DragReorder(
        SelectionPaneDialogItem dragged,
        SelectionPaneDialogItem target,
        SelectionPaneDropPlacement placement)
    {
        var plan = SelectionPanePlanner.PlanDragReorder(
            CurrentItemStates(),
            dragged.Source.Id,
            target.Source.Id,
            placement);
        if (plan is null)
            return;

        _moveChanges.AddRange(plan.MoveChanges);
        ApplyReorderPlan(plan);
        ApplySearchAndFilter(dragged.Source.Id);
    }

    private IReadOnlyList<SelectionPaneVisibilityChange> CurrentVisibilityChanges() =>
        SelectionPanePlanner.CreateVisibilityChanges(_sourceItems, CurrentItemStates());

    private IReadOnlyList<SelectionPaneRenameChange> CurrentRenameChanges() =>
        SelectionPanePlanner.CreateRenameChanges(_sourceItems, CurrentItemStates());

    private void SetAllVisibility(bool isVisible)
    {
        foreach (var item in _items)
            item.IsVisible = isVisible;

        _list.Items.Refresh();
    }

    private void ApplySearchAndFilter() => ApplySearchAndFilter(null);

    private void ApplySearchAndFilter(Guid? preferredSelection)
    {
        var search = _searchBox.Text.Trim();
        var filter = (_filterBox.SelectedItem as SelectionPaneFilterChoice)?.Value ?? SelectionPaneFilterValues.All;
        var filteredIds = SelectionPanePlanner
            .FilterItems(CurrentItemStates(), search, filter)
            .Select(item => item.Id)
            .ToHashSet();
        var filtered = _items.Where(item => filteredIds.Contains(item.Source.Id)).ToList();

        _list.ItemsSource = filtered;
        if (preferredSelection is { } id)
        {
            foreach (var item in filtered)
            {
                if (item.Source.Id != id)
                    continue;

                _list.SelectedItem = item;
                break;
            }
        }

        if (_list.SelectedIndex < 0 && _list.Items.Count > 0)
            _list.SelectedIndex = 0;
        UpdateMoveButtons();
        UpdateRenameBox();
    }

    private SelectionPaneDialogItem? FindListItem(object originalSource)
    {
        return FindListBoxItem(originalSource)?.DataContext as SelectionPaneDialogItem;
    }

    private ListBoxItem? FindListBoxItem(object originalSource)
    {
        if (originalSource is not DependencyObject dependencyObject)
            return null;

        return ItemsControl.ContainerFromElement(_list, dependencyObject) as ListBoxItem;
    }

    private static SelectionPaneDropPlacement GetDropPlacement(DragEventArgs e, ListBoxItem target)
    {
        var midpoint = target.ActualHeight / 2;
        return e.GetPosition(target).Y > midpoint
            ? SelectionPaneDropPlacement.After
            : SelectionPaneDropPlacement.Before;
    }

    private bool IsPointerOverList(DragEventArgs e)
    {
        var position = e.GetPosition(_list);
        return position.X >= 0 &&
            position.Y >= 0 &&
            position.X <= _list.ActualWidth &&
            position.Y <= _list.ActualHeight;
    }

    private void ApplyDropVisual(SelectionPaneDropVisualPlan? plan)
    {
        var changed = false;
        foreach (var item in _items)
        {
            var isTarget = plan?.IsAllowed == true && item.Source.Id == plan.TargetId;
            var isBefore = isTarget && plan!.Placement == SelectionPaneDropPlacement.Before;
            var isAfter = isTarget && plan!.Placement == SelectionPaneDropPlacement.After;
            changed |= item.IsDropBefore != isBefore || item.IsDropAfter != isAfter;
            item.IsDropBefore = isBefore;
            item.IsDropAfter = isAfter;
        }

        if (changed)
            _list.Items.Refresh();
    }

    private void ClearDropVisual() => ApplyDropVisual(null);

    private static bool CanDropDraggedItem(SelectionPaneDialogItem? dragged, SelectionPaneDialogItem? target) =>
        dragged is not null &&
        target is not null &&
        !ReferenceEquals(dragged, target) &&
        SelectionPanePlanner.CanReorderKinds(dragged.Source.Kind, target.Source.Kind);

    private void RenameSelectedItem()
    {
        if (_list.SelectedItem is not SelectionPaneDialogItem selected)
            return;

        var name = _renameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        selected.Name = name;
        _list.Items.Refresh();
    }

    private void ToggleSelectedVisibility()
    {
        if (_list.SelectedItem is not SelectionPaneDialogItem selected)
            return;

        selected.IsVisible = !selected.IsVisible;
        _list.Items.Refresh();
    }

    // R125-selection-pane-delete-wiring: removes the selected object from the working list (so it
    // no longer shows in the pane, cannot also be renamed/moved/re-shown before OK, and won't be
    // re-selected as the search/filter is re-applied) and records the delete so AcceptVisibility
    // includes it in the SAME CompositeWorkbookCommand as any other pending changes -- applied
    // (and undoable) as one atomic operation only when the user clicks OK, exactly like every
    // other Selection Pane edit. Nothing is deleted from the sheet until then.
    private void DeleteSelectedItem()
    {
        if (_list.SelectedItem is not SelectionPaneDialogItem selected)
            return;

        var selectedId = selected.Source.Id;
        _deleteChanges.Add(new SelectionPaneDeleteChange(selected.Source.Kind, selectedId));
        _items.Remove(selected);
        // A delete supersedes any pending move for the same object -- the object is about to stop
        // existing, so there is nothing left to reorder.
        _moveChanges.RemoveAll(change => change.Id == selectedId);
        ApplySearchAndFilter();
    }

    private void UpdateRenameBox()
    {
        if (_list.SelectedItem is SelectionPaneDialogItem selected)
            _renameBox.Text = selected.Name;
    }

    private void FocusRenameBox()
    {
        DialogFocus.FocusAndSelect(_renameBox);
    }

    private void UpdateMoveButtons()
    {
        if (_list.SelectedItem is not SelectionPaneDialogItem selected)
        {
            _moveUpButton.IsEnabled = false;
            _moveDownButton.IsEnabled = false;
            _deleteButton.IsEnabled = false;
            return;
        }

        var currentIndex = _items.IndexOf(selected);
        var states = CurrentItemStates();
        _moveUpButton.IsEnabled = SelectionPanePlanner.FindMoveTargetIndex(states, currentIndex, forward: true) >= 0;
        _moveDownButton.IsEnabled = SelectionPanePlanner.FindMoveTargetIndex(states, currentIndex, forward: false) >= 0;
        _deleteButton.IsEnabled = true;
    }

    private IReadOnlyList<SelectionPaneItemState> CurrentItemStates() =>
        _items
            .Select(item => new SelectionPaneItemState(
                item.Source.Kind,
                item.Source.Id,
                item.Name,
                item.IsVisible))
            .ToList();

    private void ApplyReorderPlan(SelectionPaneReorderPlan plan)
    {
        var itemsById = _items.ToDictionary(item => item.Source.Id);
        _items.Clear();
        foreach (var id in plan.OrderedIds)
        {
            if (itemsById.TryGetValue(id, out var item))
                _items.Add(item);
        }
    }
}
