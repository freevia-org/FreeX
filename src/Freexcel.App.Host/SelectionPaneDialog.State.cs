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
