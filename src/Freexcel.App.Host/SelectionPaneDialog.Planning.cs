using Freexcel.Core.Model;

namespace Freexcel.App.Host;

internal sealed record SelectionPaneDialogItemState(
    SelectionPaneObjectKind Kind,
    Guid Id,
    string Name,
    bool IsVisible);

internal sealed record SelectionPaneDialogReorderPlan(
    IReadOnlyList<Guid> OrderedIds,
    IReadOnlyList<SelectionPaneMoveChange> MoveChanges);

internal static class SelectionPaneDialogStatePlanner
{
    public static IReadOnlyList<SelectionPaneDialogItemState> FilterItems(
        IReadOnlyList<SelectionPaneDialogItemState> items,
        string search,
        string filter)
    {
        var normalizedSearch = search.Trim();
        var normalizedFilter = string.IsNullOrWhiteSpace(filter) ? "All" : filter;
        return items
            .Where(item => MatchesSearch(item, normalizedSearch) && MatchesFilter(item, normalizedFilter))
            .ToList();
    }

    public static SelectionPaneDialogReorderPlan? PlanMove(
        IReadOnlyList<SelectionPaneDialogItemState> items,
        Guid selectedId,
        bool forward)
    {
        var currentIndex = FindIndex(items, selectedId);
        var targetIndex = FindSameKindMoveTargetIndex(items, currentIndex, forward);
        if (targetIndex < 0)
            return null;

        var orderedIds = items.Select(item => item.Id).ToList();
        (orderedIds[currentIndex], orderedIds[targetIndex]) = (orderedIds[targetIndex], orderedIds[currentIndex]);
        var selected = items[currentIndex];
        return new SelectionPaneDialogReorderPlan(
            orderedIds,
            [new SelectionPaneMoveChange(selected.Kind, selected.Id, forward)]);
    }

    public static SelectionPaneDialogReorderPlan? PlanDragReorder(
        IReadOnlyList<SelectionPaneDialogItemState> items,
        Guid draggedId,
        Guid targetId)
    {
        var moves = CreateDragMoveChanges(
            items.Select(item => (item.Kind, item.Id)).ToList(),
            draggedId,
            targetId);
        if (moves.Count == 0)
            return null;

        var draggedIndex = FindIndex(items, draggedId);
        var targetIndex = FindIndex(items, targetId);
        if (draggedIndex < 0 || targetIndex < 0)
            return null;

        var orderedIds = items.Select(item => item.Id).ToList();
        var dragged = orderedIds[draggedIndex];
        orderedIds.RemoveAt(draggedIndex);
        if (draggedIndex < targetIndex)
            targetIndex--;
        orderedIds.Insert(targetIndex, dragged);
        return new SelectionPaneDialogReorderPlan(orderedIds, moves);
    }

    public static int FindSameKindMoveTargetIndex(
        IReadOnlyList<SelectionPaneDialogItemState> items,
        int currentIndex,
        bool forward)
    {
        if (currentIndex < 0 || currentIndex >= items.Count)
            return -1;

        var step = forward ? -1 : 1;
        for (var index = currentIndex + step; index >= 0 && index < items.Count; index += step)
        {
            if (items[index].Kind == items[currentIndex].Kind)
                return index;
        }

        return -1;
    }

    public static IReadOnlyList<SelectionPaneVisibilityChange> CreateVisibilityChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<SelectionPaneDialogItemState> currentStates)
    {
        var states = currentStates.ToDictionary(state => state.Id, state => state.IsVisible);
        return originalItems
            .Where(item => states.TryGetValue(item.Id, out var isVisible) && isVisible != item.IsVisible)
            .Select(item => new SelectionPaneVisibilityChange(item.Kind, item.Id, states[item.Id]))
            .ToList();
    }

    public static IReadOnlyList<SelectionPaneRenameChange> CreateRenameChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<SelectionPaneDialogItemState> currentStates)
    {
        var names = currentStates.ToDictionary(state => state.Id, state => NormalizeName(state.Name));
        return originalItems
            .Where(item => names.TryGetValue(item.Id, out var name) && !string.Equals(name, item.Name, StringComparison.Ordinal))
            .Select(item => new SelectionPaneRenameChange(item.Kind, item.Id, names[item.Id]))
            .ToList();
    }

    public static SelectionPaneDialogResult CreateResult(
        SelectionPaneDialogAction action,
        SelectionPaneItem? target,
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<SelectionPaneDialogItemState> currentStates,
        IReadOnlyList<SelectionPaneMoveChange> moveChanges) =>
        new(
            action,
            target,
            CreateVisibilityChanges(originalItems, currentStates),
            CreateRenameChanges(originalItems, currentStates),
            moveChanges);

    public static IReadOnlyList<SelectionPaneMoveChange> CreateDragMoveChanges(
        IReadOnlyList<(SelectionPaneObjectKind Kind, Guid Id)> currentOrder,
        Guid draggedId,
        Guid targetId)
    {
        var draggedIndex = FindIndex(currentOrder, draggedId);
        var targetIndex = FindIndex(currentOrder, targetId);
        if (draggedIndex < 0 || targetIndex < 0 || draggedIndex == targetIndex)
            return [];

        var dragged = currentOrder[draggedIndex];
        var target = currentOrder[targetIndex];
        if (dragged.Kind != target.Kind)
            return [];

        var moves = new List<SelectionPaneMoveChange>();
        var forward = draggedIndex > targetIndex;
        var step = forward ? -1 : 1;
        for (var index = draggedIndex; index != targetIndex; index += step)
            moves.Add(new SelectionPaneMoveChange(dragged.Kind, dragged.Id, forward));

        return moves;
    }

    private static bool MatchesSearch(SelectionPaneDialogItemState item, string search) =>
        string.IsNullOrWhiteSpace(search) ||
        item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        item.Kind.ToString().Contains(search, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesFilter(SelectionPaneDialogItemState item, string filter) =>
        filter switch
        {
            "Visible" => item.IsVisible,
            "Hidden" => !item.IsVisible,
            "Charts" => item.Kind == SelectionPaneObjectKind.Chart,
            "Pictures" => item.Kind == SelectionPaneObjectKind.Picture,
            "Shapes" => item.Kind == SelectionPaneObjectKind.Shape,
            "Text Boxes" => item.Kind == SelectionPaneObjectKind.TextBox,
            _ => true
        };

    private static int FindIndex(IReadOnlyList<SelectionPaneDialogItemState> items, Guid id)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Id == id)
                return index;
        }

        return -1;
    }

    private static int FindIndex(IReadOnlyList<(SelectionPaneObjectKind Kind, Guid Id)> items, Guid id)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Id == id)
                return index;
        }

        return -1;
    }

    private static string NormalizeName(string name) => name.Trim();
}

public sealed partial class SelectionPaneDialog
{
    public static IReadOnlyList<SelectionPaneVisibilityChange> CreateVisibilityChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible, string Name)> currentStates) =>
        SelectionPaneDialogStatePlanner.CreateVisibilityChanges(
            originalItems,
            ToDialogItemStates(originalItems, currentStates));

    public static IReadOnlyList<SelectionPaneVisibilityChange> CreateVisibilityChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible)> currentStates) =>
        CreateVisibilityChanges(
            originalItems,
            ToNamedCurrentStates(originalItems, currentStates));

    public static IReadOnlyList<SelectionPaneRenameChange> CreateRenameChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible, string Name)> currentStates) =>
        SelectionPaneDialogStatePlanner.CreateRenameChanges(
            originalItems,
            ToDialogItemStates(originalItems, currentStates));

    public static SelectionPaneDialogResult CreateResult(
        SelectionPaneDialogAction action,
        SelectionPaneItem? target,
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible, string Name)> currentStates) =>
        SelectionPaneDialogStatePlanner.CreateResult(
            action,
            target,
            originalItems,
            ToDialogItemStates(originalItems, currentStates),
            []);

    public static SelectionPaneDialogResult CreateResult(
        SelectionPaneDialogAction action,
        SelectionPaneItem? target,
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible)> currentStates) =>
        CreateResult(
            action,
            target,
            originalItems,
            ToNamedCurrentStates(originalItems, currentStates));

    public static IReadOnlyList<SelectionPaneMoveChange> CreateDragMoveChanges(
        IReadOnlyList<(SelectionPaneObjectKind Kind, Guid Id)> currentOrder,
        Guid draggedId,
        Guid targetId) =>
        SelectionPaneDialogStatePlanner.CreateDragMoveChanges(currentOrder, draggedId, targetId);

    private static IReadOnlyList<SelectionPaneDialogItemState> ToDialogItemStates(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible, string Name)> currentStates)
    {
        var itemsById = originalItems.ToDictionary(item => item.Id);
        var states = new List<SelectionPaneDialogItemState>(currentStates.Count);
        foreach (var state in currentStates)
        {
            if (itemsById.TryGetValue(state.Id, out var item))
                states.Add(new SelectionPaneDialogItemState(item.Kind, state.Id, state.Name, state.IsVisible));
        }

        return states;
    }

    private static IReadOnlyList<(Guid Id, bool IsVisible, string Name)> ToNamedCurrentStates(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible)> currentStates)
    {
        var namesById = originalItems.ToDictionary(item => item.Id, item => item.Name);
        var states = new List<(Guid Id, bool IsVisible, string Name)>(currentStates.Count);
        foreach (var state in currentStates)
        {
            namesById.TryGetValue(state.Id, out var name);
            states.Add((state.Id, state.IsVisible, name ?? ""));
        }

        return states;
    }
}
