using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

public sealed class SelectionPaneSessionItem
{
    internal SelectionPaneSessionItem(SelectionPaneItem source)
    {
        Source = source;
        Name = source.Name;
        IsVisible = source.IsVisible;
    }

    public SelectionPaneItem Source { get; }
    public SelectionPaneObjectKind Kind => Source.Kind;
    public Guid Id => Source.Id;
    public string Name { get; internal set; }
    public bool IsVisible { get; internal set; }

    internal SelectionPaneItemState ToState() => new(Kind, Id, Name, IsVisible);
}

public sealed record SelectionPaneSessionOutcome(
    bool IsHandled,
    bool StateChanged,
    bool FocusRename)
{
    public static SelectionPaneSessionOutcome NotHandled { get; } = new(false, false, false);
    public static SelectionPaneSessionOutcome Handled { get; } = new(true, false, false);
    public static SelectionPaneSessionOutcome Changed { get; } = new(true, true, false);
    public static SelectionPaneSessionOutcome RenameFocusRequested { get; } = new(true, false, true);
}

public sealed class SelectionPaneSession
{
    private readonly IReadOnlyList<SelectionPaneItem> _originalItems;
    private readonly List<SelectionPaneSessionItem> _items;
    private readonly IReadOnlyList<SelectionPaneSessionItem> _itemsView;
    private readonly List<SelectionPaneMoveChange> _moveChanges = [];
    private readonly IReadOnlyList<SelectionPaneMoveChange> _moveChangesView;
    private readonly List<SelectionPaneDeleteChange> _deleteChanges = [];
    private readonly IReadOnlyList<SelectionPaneDeleteChange> _deleteChangesView;
    private IReadOnlyList<SelectionPaneSessionItem> _filteredItems;
    private Guid? _selectedId;
    private Guid? _draggedId;

    public SelectionPaneSession(IReadOnlyList<SelectionPaneItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _originalItems = items.ToArray();
        _items = items.Select(item => new SelectionPaneSessionItem(item)).ToList();
        _itemsView = _items.AsReadOnly();
        _moveChangesView = _moveChanges.AsReadOnly();
        _deleteChangesView = _deleteChanges.AsReadOnly();
        _filteredItems = _items.ToArray();
        _selectedId = _filteredItems.FirstOrDefault()?.Id;
    }

    public IReadOnlyList<SelectionPaneSessionItem> Items => _itemsView;
    public IReadOnlyList<SelectionPaneSessionItem> FilteredItems => _filteredItems;
    public IReadOnlyList<SelectionPaneMoveChange> MoveChanges => _moveChangesView;
    public IReadOnlyList<SelectionPaneDeleteChange> DeleteChanges => _deleteChangesView;
    public Guid? SelectedId => _selectedId;
    public SelectionPaneSessionItem? SelectedItem => FindItem(_selectedId);
    public string Search { get; private set; } = string.Empty;
    public string Filter { get; private set; } = SelectionPaneFilterValues.All;
    public Guid? DraggedId => _draggedId;
    public SelectionPaneDropVisualPlan? DropVisual { get; private set; }
    public bool CanRename => SelectedItem is not null;
    public bool CanToggleVisibility => SelectedItem is not null;
    public bool CanDelete => SelectedItem is not null;
    public bool CanMoveUp => CanMoveSelected(forward: true);
    public bool CanMoveDown => CanMoveSelected(forward: false);
    public bool HasChanges => SelectionPanePlanner.HasChanges(
        SelectionPanePlanner.CreateVisibilityChanges(_originalItems, CreateItemStates()),
        SelectionPanePlanner.CreateRenameChanges(_originalItems, CreateItemStates()),
        _moveChanges,
        _deleteChanges);

    public static SelectionPaneSession Create(Sheet sheet, SelectionPanePlannerText text) =>
        new(SelectionPanePlanner.BuildItems(sheet, text));

    public SelectionPaneSessionItem? FindItem(Guid? id)
    {
        if (id is null)
            return null;

        for (var index = 0; index < _items.Count; index++)
        {
            if (_items[index].Id == id.Value)
                return _items[index];
        }

        return null;
    }

    public SelectionPaneSessionOutcome SetView(
        string? search,
        string? filter,
        Guid? preferredSelection = null)
    {
        var normalizedSearch = search?.Trim() ?? string.Empty;
        var normalizedFilter = string.IsNullOrWhiteSpace(filter)
            ? SelectionPaneFilterValues.All
            : filter;
        var viewChanged = !string.Equals(Search, normalizedSearch, StringComparison.Ordinal) ||
            !string.Equals(Filter, normalizedFilter, StringComparison.Ordinal);
        Search = normalizedSearch;
        Filter = normalizedFilter;

        var previousSelection = _selectedId;
        RefreshProjection(preferredSelection ?? _selectedId);
        return viewChanged || previousSelection != _selectedId
            ? SelectionPaneSessionOutcome.Changed
            : SelectionPaneSessionOutcome.Handled;
    }

    public SelectionPaneSessionOutcome ApplyStates(IReadOnlyList<SelectionPaneItemState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        var itemsById = _items.ToDictionary(item => item.Id);
        var changed = false;
        for (var index = 0; index < states.Count; index++)
        {
            var state = states[index];
            if (!itemsById.TryGetValue(state.Id, out var item))
                continue;

            changed |= !string.Equals(item.Name, state.Name, StringComparison.Ordinal) ||
                item.IsVisible != state.IsVisible;
            item.Name = state.Name;
            item.IsVisible = state.IsVisible;
        }

        if (!changed)
            return SelectionPaneSessionOutcome.Handled;

        return SelectionPaneSessionOutcome.Changed;
    }

    public SelectionPaneSessionOutcome Select(Guid? id)
    {
        var next = ContainsFilteredItem(id) ? id : null;
        if (_selectedId == next)
            return SelectionPaneSessionOutcome.Handled;

        _selectedId = next;
        return SelectionPaneSessionOutcome.Changed;
    }

    public SelectionPaneSessionOutcome SetName(Guid id, string? name)
    {
        var item = FindItem(id);
        var next = name ?? string.Empty;
        if (item is null || string.Equals(item.Name, next, StringComparison.Ordinal))
            return item is null ? SelectionPaneSessionOutcome.NotHandled : SelectionPaneSessionOutcome.Handled;

        item.Name = next;
        return SelectionPaneSessionOutcome.Changed;
    }

    public SelectionPaneSessionOutcome RenameSelected(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        return normalized.Length == 0 || _selectedId is not { } selectedId
            ? SelectionPaneSessionOutcome.NotHandled
            : SetName(selectedId, normalized);
    }

    public SelectionPaneSessionOutcome SetVisibility(Guid id, bool isVisible)
    {
        var item = FindItem(id);
        if (item is null || item.IsVisible == isVisible)
            return item is null ? SelectionPaneSessionOutcome.NotHandled : SelectionPaneSessionOutcome.Handled;

        item.IsVisible = isVisible;
        return SelectionPaneSessionOutcome.Changed;
    }

    public SelectionPaneSessionOutcome ToggleSelectedVisibility()
    {
        var selected = SelectedItem;
        return selected is null
            ? SelectionPaneSessionOutcome.NotHandled
            : SetVisibility(selected.Id, !selected.IsVisible);
    }

    public SelectionPaneSessionOutcome SetAllVisibility(bool isVisible)
    {
        var changed = false;
        for (var index = 0; index < _items.Count; index++)
        {
            var item = _items[index];
            changed |= item.IsVisible != isVisible;
            item.IsVisible = isVisible;
        }

        if (!changed)
            return SelectionPaneSessionOutcome.Handled;

        return SelectionPaneSessionOutcome.Changed;
    }

    public SelectionPaneSessionOutcome DeleteSelected()
    {
        var selected = SelectedItem;
        if (selected is null)
            return SelectionPaneSessionOutcome.NotHandled;

        _deleteChanges.Add(new SelectionPaneDeleteChange(selected.Kind, selected.Id));
        _moveChanges.RemoveAll(change => change.Id == selected.Id);
        _items.Remove(selected);
        if (_draggedId == selected.Id)
        {
            _draggedId = null;
            DropVisual = null;
        }

        RefreshProjection(preferredSelection: null);
        return SelectionPaneSessionOutcome.Changed;
    }

    public SelectionPaneSessionOutcome MoveSelected(bool forward)
    {
        if (_selectedId is not { } selectedId)
            return SelectionPaneSessionOutcome.NotHandled;

        var plan = SelectionPanePlanner.PlanMove(CreateItemStates(), selectedId, forward);
        if (plan is null)
            return SelectionPaneSessionOutcome.Handled;

        ApplyReorder(plan, selectedId);
        return SelectionPaneSessionOutcome.Changed;
    }

    public SelectionPaneSessionOutcome HandleKeyboard(
        SelectionPaneKeyboardKey key,
        bool hasControlModifier)
    {
        var action = SelectionPanePlanner.PlanKeyboardAction(key, hasControlModifier);
        return action switch
        {
            SelectionPaneKeyboardAction.MoveUp => MoveSelected(forward: true) with { IsHandled = true },
            SelectionPaneKeyboardAction.MoveDown => MoveSelected(forward: false) with { IsHandled = true },
            SelectionPaneKeyboardAction.FocusRename when CanRename => SelectionPaneSessionOutcome.RenameFocusRequested,
            SelectionPaneKeyboardAction.FocusRename => SelectionPaneSessionOutcome.Handled,
            SelectionPaneKeyboardAction.ToggleVisibility => ToggleSelectedVisibility() with { IsHandled = true },
            SelectionPaneKeyboardAction.Delete => DeleteSelected() with { IsHandled = true },
            _ => SelectionPaneSessionOutcome.NotHandled
        };
    }

    public SelectionPaneSessionOutcome BeginDrag(Guid? draggedId)
    {
        DropVisual = null;
        _draggedId = FindItem(draggedId) is null ? null : draggedId;
        return _draggedId is null
            ? SelectionPaneSessionOutcome.NotHandled
            : SelectionPaneSessionOutcome.Handled;
    }

    public SelectionPaneSessionOutcome UpdateDrag(
        Guid targetId,
        SelectionPaneDropPlacement placement)
    {
        if (_draggedId is not { } draggedId)
            return SelectionPaneSessionOutcome.NotHandled;

        var next = SelectionPanePlanner.PlanDropVisual(
            CreateItemStates(),
            draggedId,
            targetId,
            placement);
        var changed = !Equals(DropVisual, next);
        DropVisual = next;
        return changed
            ? SelectionPaneSessionOutcome.Changed
            : SelectionPaneSessionOutcome.Handled;
    }

    public SelectionPaneSessionOutcome Drop(Guid targetId, SelectionPaneDropPlacement placement)
    {
        if (_draggedId is not { } draggedId)
            return SelectionPaneSessionOutcome.NotHandled;

        var plan = SelectionPanePlanner.PlanDragReorder(
            CreateItemStates(),
            draggedId,
            targetId,
            placement);
        _draggedId = null;
        DropVisual = null;
        if (plan is null)
            return SelectionPaneSessionOutcome.Handled;

        ApplyReorder(plan, draggedId);
        return SelectionPaneSessionOutcome.Changed;
    }

    public SelectionPaneSessionOutcome ClearDropVisual()
    {
        if (DropVisual is null)
            return SelectionPaneSessionOutcome.Handled;

        DropVisual = null;
        return SelectionPaneSessionOutcome.Changed;
    }

    public SelectionPaneSessionOutcome CancelDrag()
    {
        var changed = _draggedId is not null || DropVisual is not null;
        _draggedId = null;
        DropVisual = null;
        return changed
            ? SelectionPaneSessionOutcome.Changed
            : SelectionPaneSessionOutcome.Handled;
    }

    public SelectionPaneDialogResult CreateResult(
        SelectionPaneDialogAction action = SelectionPaneDialogAction.ApplyVisibility,
        SelectionPaneItem? target = null) =>
        SelectionPanePlanner.CreateResult(
            action,
            target,
            _originalItems,
            CreateItemStates(),
            _moveChanges,
            _deleteChanges);

    public IWorkbookCommand? CreateCommand(SheetId sheetId)
    {
        var result = CreateResult();
        return SelectionPanePlanner.CreateCommand(
            sheetId,
            result.VisibilityChanges,
            result.RenameChanges,
            result.MoveChanges,
            result.DeleteChanges);
    }

    private bool CanMoveSelected(bool forward)
    {
        if (_selectedId is not { } selectedId)
            return false;

        var states = CreateItemStates();
        for (var index = 0; index < states.Count; index++)
        {
            if (states[index].Id == selectedId)
                return SelectionPanePlanner.FindMoveTargetIndex(states, index, forward) >= 0;
        }

        return false;
    }

    private bool ContainsFilteredItem(Guid? id)
    {
        if (id is null)
            return false;

        for (var index = 0; index < _filteredItems.Count; index++)
        {
            if (_filteredItems[index].Id == id.Value)
                return true;
        }

        return false;
    }

    private void RefreshProjection(Guid? preferredSelection)
    {
        var states = CreateItemStates();
        var filteredStates = SelectionPanePlanner.FilterItems(states, Search, Filter);
        if (ReferenceEquals(filteredStates, states))
        {
            _filteredItems = _items.ToArray();
        }
        else
        {
            var byId = _items.ToDictionary(item => item.Id);
            var filtered = new List<SelectionPaneSessionItem>(filteredStates.Count);
            for (var index = 0; index < filteredStates.Count; index++)
            {
                if (byId.TryGetValue(filteredStates[index].Id, out var item))
                    filtered.Add(item);
            }

            _filteredItems = filtered;
        }

        _selectedId = ContainsFilteredItem(preferredSelection)
            ? preferredSelection
            : _filteredItems.FirstOrDefault()?.Id;
    }

    private IReadOnlyList<SelectionPaneItemState> CreateItemStates()
    {
        var states = new List<SelectionPaneItemState>(_items.Count);
        for (var index = 0; index < _items.Count; index++)
            states.Add(_items[index].ToState());

        return states;
    }

    private void ApplyReorder(SelectionPaneReorderPlan plan, Guid preferredSelection)
    {
        _moveChanges.AddRange(plan.MoveChanges);
        var byId = _items.ToDictionary(item => item.Id);
        _items.Clear();
        for (var index = 0; index < plan.OrderedIds.Count; index++)
        {
            if (byId.TryGetValue(plan.OrderedIds[index], out var item))
                _items.Add(item);
        }

        RefreshProjection(preferredSelection);
    }
}
