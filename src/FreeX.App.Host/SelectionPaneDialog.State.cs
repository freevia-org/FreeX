using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
            _moveChanges.ToList());
        DialogResult = true;
    }

    private void AcceptMove(SelectionPaneDialogAction action)
    {
        if (_list.SelectedItem is not SelectionPaneDialogItem selected)
            return;

        var forward = action == SelectionPaneDialogAction.MoveUp;
        var plan = SelectionPaneDialogStatePlanner.PlanMove(CurrentItemStates(), selected.Source.Id, forward);
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
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed ||
            _dragStartPoint is not { } start ||
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
        var target = FindListItem(e.OriginalSource);
        e.Effects = CanDropDraggedItem(dragged, target) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void List_Drop(object sender, DragEventArgs e)
    {
        var dragged = e.Data.GetData(typeof(SelectionPaneDialogItem)) as SelectionPaneDialogItem;
        var target = FindListItem(e.OriginalSource);
        if (!CanDropDraggedItem(dragged, target))
            return;

        DragReorder(dragged!, target!);
        e.Handled = true;
    }

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            FocusRenameBox();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            ToggleSelectedVisibility();
            e.Handled = true;
        }
    }

    private void DragReorder(SelectionPaneDialogItem dragged, SelectionPaneDialogItem target)
    {
        var plan = SelectionPaneDialogStatePlanner.PlanDragReorder(
            CurrentItemStates(),
            dragged.Source.Id,
            target.Source.Id);
        if (plan is null)
            return;

        _moveChanges.AddRange(plan.MoveChanges);
        ApplyReorderPlan(plan);
        ApplySearchAndFilter(dragged.Source.Id);
    }

    private IReadOnlyList<SelectionPaneVisibilityChange> CurrentVisibilityChanges() =>
        SelectionPaneDialogStatePlanner.CreateVisibilityChanges(_sourceItems, CurrentItemStates());

    private IReadOnlyList<SelectionPaneRenameChange> CurrentRenameChanges() =>
        SelectionPaneDialogStatePlanner.CreateRenameChanges(_sourceItems, CurrentItemStates());

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
        var filteredIds = SelectionPaneDialogStatePlanner
            .FilterItems(CurrentItemStates(), search, filter)
            .Select(item => item.Id)
            .ToHashSet();
        var filtered = _items.Where(item => filteredIds.Contains(item.Source.Id)).ToList();

        _list.ItemsSource = filtered;
        if (preferredSelection is { } id)
            _list.SelectedItem = filtered.FirstOrDefault(item => item.Source.Id == id);
        if (_list.SelectedIndex < 0 && _list.Items.Count > 0)
            _list.SelectedIndex = 0;
        UpdateMoveButtons();
        UpdateRenameBox();
    }

    private SelectionPaneDialogItem? FindListItem(object originalSource)
    {
        if (originalSource is not DependencyObject dependencyObject)
            return null;

        return ItemsControl.ContainerFromElement(_list, dependencyObject) is ListBoxItem item
            ? item.DataContext as SelectionPaneDialogItem
            : null;
    }

    private static bool CanDropDraggedItem(SelectionPaneDialogItem? dragged, SelectionPaneDialogItem? target) =>
        dragged is not null &&
        target is not null &&
        !ReferenceEquals(dragged, target) &&
        dragged.Source.Kind == target.Source.Kind;

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
            return;
        }

        var currentIndex = _items.IndexOf(selected);
        var states = CurrentItemStates();
        _moveUpButton.IsEnabled = SelectionPaneDialogStatePlanner.FindSameKindMoveTargetIndex(states, currentIndex, forward: true) >= 0;
        _moveDownButton.IsEnabled = SelectionPaneDialogStatePlanner.FindSameKindMoveTargetIndex(states, currentIndex, forward: false) >= 0;
    }

    private IReadOnlyList<SelectionPaneDialogItemState> CurrentItemStates() =>
        _items
            .Select(item => new SelectionPaneDialogItemState(
                item.Source.Kind,
                item.Source.Id,
                item.Name,
                item.IsVisible))
            .ToList();

    private void ApplyReorderPlan(SelectionPaneDialogReorderPlan plan)
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
