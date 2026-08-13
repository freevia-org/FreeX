using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.DrawingUI;

namespace FreeX.App.Host;

public sealed partial class SelectionPaneDialog
{
    private void AcceptVisibility()
    {
        Result = _session.CreateResult();
        DialogResult = true;
    }

    private void AcceptMove(SelectionPaneDialogAction action)
    {
        if (_list.SelectedItem is not SelectionPaneDialogItem selected)
            return;

        _session.Select(selected.Source.Id);
        var outcome = _session.MoveSelected(action == SelectionPaneDialogAction.MoveUp);
        if (outcome.StateChanged)
            ApplySearchAndFilter(selected.Source.Id);
    }

    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(_list);
        _dragItem = FindListItem(e.OriginalSource);
        _session.BeginDrag(_dragItem?.Source.Id);
    }

    private void List_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ClearNativeDragState();
            _session.CancelDrag();
            RefreshItemBindings();
            return;
        }

        if (_dragStartPoint is not { } start || _dragItem is null)
            return;

        var current = e.GetPosition(_list);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(_list, _dragItem, DragDropEffects.Move);
        ClearNativeDragState();
        _session.CancelDrag();
        RefreshItemBindings();
    }

    private void List_DragOver(object sender, DragEventArgs e)
    {
        var dragged = e.Data.GetData(typeof(SelectionPaneDialogItem)) as SelectionPaneDialogItem;
        var targetContainer = FindListBoxItem(e.OriginalSource);
        var target = targetContainer?.DataContext as SelectionPaneDialogItem;
        if (dragged is null || target is null)
        {
            _session.ClearDropVisual();
        }
        else
        {
            _session.BeginDrag(dragged.Source.Id);
            _session.UpdateDrag(
                target.Source.Id,
                GetDropPlacement(e, targetContainer!));
        }

        RefreshItemBindings();
        e.Effects = _session.DropVisual?.IsAllowed == true ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void List_DragLeave(object sender, DragEventArgs e)
    {
        if (!IsPointerOverList(e))
        {
            _session.ClearDropVisual();
            RefreshItemBindings();
        }
    }

    private void List_Drop(object sender, DragEventArgs e)
    {
        var dragged = e.Data.GetData(typeof(SelectionPaneDialogItem)) as SelectionPaneDialogItem;
        var targetContainer = FindListBoxItem(e.OriginalSource);
        var target = targetContainer?.DataContext as SelectionPaneDialogItem;
        if (dragged is null || target is null)
        {
            _session.CancelDrag();
            RefreshItemBindings();
            return;
        }

        _session.BeginDrag(dragged.Source.Id);
        var outcome = _session.Drop(
            target.Source.Id,
            GetDropPlacement(e, targetContainer!));
        if (outcome.StateChanged)
            ApplySearchAndFilter(dragged.Source.Id);
        else
            RefreshItemBindings();

        e.Handled = outcome.IsHandled;
    }

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        var selectedId = _session.SelectedId;
        var outcome = _session.HandleKeyboard(
            ToSelectionPaneKeyboardKey(e.Key),
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control);
        if (outcome.StateChanged)
            ApplySearchAndFilter(selectedId);
        if (outcome.FocusRename)
            FocusRenameBox();

        e.Handled = outcome.IsHandled;
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

    private void SetAllVisibility(bool isVisible)
    {
        _session.SetAllVisibility(isVisible);
        ApplySearchAndFilter(_session.SelectedId);
    }

    private void ApplySearchAndFilter() => ApplySearchAndFilter(null);

    private void ApplySearchAndFilter(Guid? preferredSelection)
    {
        var filter = (_filterBox.SelectedItem as SelectionPaneFilterChoice)?.Value ?? SelectionPaneFilterValues.All;
        _session.SetView(_searchBox.Text, filter, preferredSelection);
        RebuildItemOrder();

        var itemsById = _items.ToDictionary(item => item.Source.Id);
        var filtered = _session.FilteredItems
            .Select(item => itemsById[item.Id])
            .ToList();

        _isRebinding = true;
        try
        {
            _list.ItemsSource = filtered;
            _list.SelectedItem = _session.SelectedId is { } selectedId && itemsById.TryGetValue(selectedId, out var selected)
                ? selected
                : null;
        }
        finally
        {
            _isRebinding = false;
        }

        RefreshItemBindings();
        UpdateMoveButtons();
        UpdateRenameBox();
    }

    private SelectionPaneDialogItem? FindListItem(object originalSource) =>
        FindListBoxItem(originalSource)?.DataContext as SelectionPaneDialogItem;

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

    private void RenameSelectedItem()
    {
        if (_session.RenameSelected(_renameBox.Text).StateChanged)
            ApplySearchAndFilter(_session.SelectedId);
    }

    private void ToggleSelectedVisibility()
    {
        if (_session.ToggleSelectedVisibility().StateChanged)
            ApplySearchAndFilter(_session.SelectedId);
    }

    private void DeleteSelectedItem()
    {
        if (_session.DeleteSelected().StateChanged)
            ApplySearchAndFilter(_session.SelectedId);
    }

    private void UpdateRenameBox()
    {
        if (_session.SelectedItem is { } selected &&
            !string.Equals(_renameBox.Text, selected.Name, StringComparison.Ordinal))
        {
            _renameBox.Text = selected.Name;
        }

        _renameButton.IsEnabled = _session.CanRename;
        _toggleVisibilityButton.IsEnabled = _session.CanToggleVisibility;
        _deleteButton.IsEnabled = _session.CanDelete;
    }

    private void FocusRenameBox() => DialogFocus.FocusAndSelect(_renameBox);

    private void UpdateMoveButtons()
    {
        _moveUpButton.IsEnabled = _session.CanMoveUp;
        _moveDownButton.IsEnabled = _session.CanMoveDown;
    }

    private void RebuildItemOrder()
    {
        var itemsById = _items.ToDictionary(item => item.Source.Id);
        _items.Clear();
        foreach (var item in _session.Items)
        {
            if (itemsById.TryGetValue(item.Id, out var adapter))
                _items.Add(adapter);
        }
    }

    private void RefreshItemBindings() => _list.Items.Refresh();

    private void ClearNativeDragState()
    {
        _dragStartPoint = null;
        _dragItem = null;
    }
}
