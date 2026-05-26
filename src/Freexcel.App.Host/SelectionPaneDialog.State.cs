using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
        var currentIndex = _items.IndexOf(selected);
        var targetIndex = FindMoveTargetIndex(currentIndex, forward);
        if (targetIndex < 0)
            return;

        _moveChanges.Add(new SelectionPaneMoveChange(selected.Source.Kind, selected.Source.Id, forward));
        (_items[currentIndex], _items[targetIndex]) = (_items[targetIndex], _items[currentIndex]);
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
        var moves = CreateDragMoveChanges(
            _items.Select(item => (item.Source.Kind, item.Source.Id)).ToList(),
            dragged.Source.Id,
            target.Source.Id);
        if (moves.Count == 0)
            return;

        _moveChanges.AddRange(moves);
        var fromIndex = _items.IndexOf(dragged);
        var toIndex = _items.IndexOf(target);
        if (fromIndex < 0 || toIndex < 0)
            return;

        _items.RemoveAt(fromIndex);
        if (fromIndex < toIndex)
            toIndex--;
        _items.Insert(toIndex, dragged);
        ApplySearchAndFilter(dragged.Source.Id);
    }

    private IReadOnlyList<SelectionPaneVisibilityChange> CurrentVisibilityChanges() =>
        CreateVisibilityChanges(
            _sourceItems,
            _items.Select(item => (item.Source.Id, item.IsVisible, item.Name)).ToList());

    private IReadOnlyList<SelectionPaneRenameChange> CurrentRenameChanges() =>
        CreateRenameChanges(
            _sourceItems,
            _items.Select(item => (item.Source.Id, item.IsVisible, item.Name)).ToList());

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
        var filter = _filterBox.SelectedItem as string ?? "All";
        var filtered = _items.Where(item =>
            MatchesSearch(item, search) &&
            MatchesFilter(item, filter)).ToList();

        _list.ItemsSource = filtered;
        if (preferredSelection is { } id)
            _list.SelectedItem = filtered.FirstOrDefault(item => item.Source.Id == id);
        if (_list.SelectedIndex < 0 && _list.Items.Count > 0)
            _list.SelectedIndex = 0;
        UpdateMoveButtons();
        UpdateRenameBox();
    }

    private static bool MatchesSearch(SelectionPaneDialogItem item, string search) =>
        string.IsNullOrWhiteSpace(search) ||
        item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        item.Kind.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesFilter(SelectionPaneDialogItem item, string filter) =>
        filter switch
        {
            "Visible" => item.IsVisible,
            "Hidden" => !item.IsVisible,
            "Charts" => item.Source.Kind == SelectionPaneObjectKind.Chart,
            "Pictures" => item.Source.Kind == SelectionPaneObjectKind.Picture,
            "Shapes" => item.Source.Kind == SelectionPaneObjectKind.Shape,
            "Text Boxes" => item.Source.Kind == SelectionPaneObjectKind.TextBox,
            _ => true
        };

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

    private int FindMoveTargetIndex(int currentIndex, bool forward)
    {
        if (currentIndex < 0 || currentIndex >= _items.Count)
            return -1;

        var step = forward ? -1 : 1;
        for (var index = currentIndex + step; index >= 0 && index < _items.Count; index += step)
        {
            if (_items[index].Source.Kind == _items[currentIndex].Source.Kind)
                return index;
        }

        return -1;
    }

    private void UpdateRenameBox()
    {
        if (_list.SelectedItem is SelectionPaneDialogItem selected)
            _renameBox.Text = selected.Name;
    }

    private void FocusRenameBox()
    {
        _renameBox.Focus();
        _renameBox.SelectAll();
        Keyboard.Focus(_renameBox);
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
        _moveUpButton.IsEnabled = FindMoveTargetIndex(currentIndex, forward: true) >= 0;
        _moveDownButton.IsEnabled = FindMoveTargetIndex(currentIndex, forward: false) >= 0;
    }
}
