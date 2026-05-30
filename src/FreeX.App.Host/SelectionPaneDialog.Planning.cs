using FreeX.Core.Model;

namespace FreeX.App.Host;

internal sealed record SelectionPaneDialogItemState(
    SelectionPaneObjectKind Kind,
    Guid Id,
    string Name,
    bool IsVisible);

internal sealed record SelectionPaneDialogReorderPlan(
    IReadOnlyList<Guid> OrderedIds,
    IReadOnlyList<SelectionPaneMoveChange> MoveChanges);

internal sealed record SelectionPaneDragMovePlan(
    int DraggedIndex,
    int InsertIndex,
    IReadOnlyList<SelectionPaneMoveChange> MoveChanges);

internal sealed record SelectionPaneDropVisualPlan(
    Guid TargetId,
    SelectionPaneDropPlacement Placement,
    bool IsAllowed);

public enum SelectionPaneDropPlacement
{
    Before,
    After
}

internal static class SelectionPaneFilterValues
{
    public const string All = "All";
    public const string Visible = "Visible";
    public const string Hidden = "Hidden";
    public const string Charts = "Charts";
    public const string Pictures = "Pictures";
    public const string Shapes = "Shapes";
    public const string TextBoxes = "Text Boxes";
}

internal static class SelectionPaneDialogStatePlanner
{
    public static IReadOnlyList<SelectionPaneDialogItemState> FilterItems(
        IReadOnlyList<SelectionPaneDialogItemState> items,
        string search,
        string filter)
    {
        var normalizedSearch = search.Trim();
        var normalizedFilter = string.IsNullOrWhiteSpace(filter) ? SelectionPaneFilterValues.All : filter;
        if (normalizedSearch.Length == 0 && string.Equals(normalizedFilter, SelectionPaneFilterValues.All, StringComparison.Ordinal))
            return items;

        var filtered = new List<SelectionPaneDialogItemState>();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (MatchesSearch(item, normalizedSearch) && MatchesFilter(item, normalizedFilter))
                filtered.Add(item);
        }

        return filtered;
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

        var orderedIds = CreateOrderedIds(items);
        (orderedIds[currentIndex], orderedIds[targetIndex]) = (orderedIds[targetIndex], orderedIds[currentIndex]);
        var selected = items[currentIndex];
        return new SelectionPaneDialogReorderPlan(
            orderedIds,
            [new SelectionPaneMoveChange(selected.Kind, selected.Id, forward)]);
    }

    public static SelectionPaneDialogReorderPlan? PlanDragReorder(
        IReadOnlyList<SelectionPaneDialogItemState> items,
        Guid draggedId,
        Guid targetId,
        SelectionPaneDropPlacement placement = SelectionPaneDropPlacement.Before)
    {
        var dragPlan = CreateDragMovePlan(items, draggedId, targetId, placement);
        if (dragPlan is null)
            return null;

        var orderedIds = CreateOrderedIds(items);
        var dragged = orderedIds[dragPlan.DraggedIndex];
        orderedIds.RemoveAt(dragPlan.DraggedIndex);
        orderedIds.Insert(dragPlan.InsertIndex, dragged);
        return new SelectionPaneDialogReorderPlan(orderedIds, dragPlan.MoveChanges);
    }

    public static SelectionPaneDropVisualPlan PlanDropVisual(
        IReadOnlyList<SelectionPaneDialogItemState> items,
        Guid draggedId,
        Guid targetId,
        SelectionPaneDropPlacement placement = SelectionPaneDropPlacement.Before)
    {
        var dragPlan = CreateDragMovePlan(items, draggedId, targetId, placement);
        return new SelectionPaneDropVisualPlan(targetId, placement, dragPlan is not null);
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
        var changes = new List<SelectionPaneVisibilityChange>();
        for (var index = 0; index < originalItems.Count; index++)
        {
            var item = originalItems[index];
            if (states.TryGetValue(item.Id, out var isVisible) && isVisible != item.IsVisible)
                changes.Add(new SelectionPaneVisibilityChange(item.Kind, item.Id, isVisible));
        }

        return changes;
    }

    public static IReadOnlyList<SelectionPaneRenameChange> CreateRenameChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<SelectionPaneDialogItemState> currentStates)
    {
        var names = currentStates.ToDictionary(state => state.Id, state => NormalizeName(state.Name));
        var changes = new List<SelectionPaneRenameChange>();
        for (var index = 0; index < originalItems.Count; index++)
        {
            var item = originalItems[index];
            if (names.TryGetValue(item.Id, out var name) && !string.Equals(name, item.Name, StringComparison.Ordinal))
                changes.Add(new SelectionPaneRenameChange(item.Kind, item.Id, name));
        }

        return changes;
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
        Guid targetId,
        SelectionPaneDropPlacement placement = SelectionPaneDropPlacement.Before) =>
        CreateDragMovePlan(currentOrder, draggedId, targetId, placement)?.MoveChanges ?? [];

    private static SelectionPaneDragMovePlan? CreateDragMovePlan(
        IReadOnlyList<SelectionPaneDialogItemState> items,
        Guid draggedId,
        Guid targetId,
        SelectionPaneDropPlacement placement)
    {
        var (draggedIndex, targetIndex) = FindDragIndexes(items, draggedId, targetId);
        if (draggedIndex < 0 || targetIndex < 0 || draggedIndex == targetIndex)
            return null;

        var dragged = items[draggedIndex];
        var target = items[targetIndex];
        if (dragged.Kind != target.Kind)
            return null;

        return CreateDragMovePlan(dragged.Kind, dragged.Id, draggedIndex, targetIndex, placement);
    }

    private static SelectionPaneDragMovePlan? CreateDragMovePlan(
        IReadOnlyList<(SelectionPaneObjectKind Kind, Guid Id)> currentOrder,
        Guid draggedId,
        Guid targetId,
        SelectionPaneDropPlacement placement)
    {
        var (draggedIndex, targetIndex) = FindDragIndexes(currentOrder, draggedId, targetId);
        if (draggedIndex < 0 || targetIndex < 0 || draggedIndex == targetIndex)
            return null;

        var dragged = currentOrder[draggedIndex];
        var target = currentOrder[targetIndex];
        if (dragged.Kind != target.Kind)
            return null;

        return CreateDragMovePlan(dragged.Kind, dragged.Id, draggedIndex, targetIndex, placement);
    }

    private static SelectionPaneDragMovePlan? CreateDragMovePlan(
        SelectionPaneObjectKind kind,
        Guid draggedId,
        int draggedIndex,
        int targetIndex,
        SelectionPaneDropPlacement placement)
    {
        var insertIndex = placement == SelectionPaneDropPlacement.After ? targetIndex + 1 : targetIndex;
        if (draggedIndex < insertIndex)
            insertIndex--;

        if (insertIndex == draggedIndex)
            return null;

        var moves = new List<SelectionPaneMoveChange>(Math.Abs(draggedIndex - insertIndex));
        var forward = draggedIndex > insertIndex;
        var step = forward ? -1 : 1;
        for (var index = draggedIndex; index != insertIndex; index += step)
            moves.Add(new SelectionPaneMoveChange(kind, draggedId, forward));

        return new SelectionPaneDragMovePlan(draggedIndex, insertIndex, moves);
    }

    private static bool MatchesSearch(SelectionPaneDialogItemState item, string search) =>
        string.IsNullOrWhiteSpace(search) ||
        item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        item.Kind.ToString().Contains(search, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesFilter(SelectionPaneDialogItemState item, string filter) =>
        filter switch
        {
            SelectionPaneFilterValues.Visible => item.IsVisible,
            SelectionPaneFilterValues.Hidden => !item.IsVisible,
            SelectionPaneFilterValues.Charts => item.Kind == SelectionPaneObjectKind.Chart,
            SelectionPaneFilterValues.Pictures => item.Kind == SelectionPaneObjectKind.Picture,
            SelectionPaneFilterValues.Shapes => item.Kind == SelectionPaneObjectKind.Shape,
            SelectionPaneFilterValues.TextBoxes => item.Kind == SelectionPaneObjectKind.TextBox,
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

    private static List<Guid> CreateOrderedIds(IReadOnlyList<SelectionPaneDialogItemState> items)
    {
        var orderedIds = new List<Guid>(items.Count);
        for (var index = 0; index < items.Count; index++)
            orderedIds.Add(items[index].Id);

        return orderedIds;
    }

    private static (int DraggedIndex, int TargetIndex) FindDragIndexes(
        IReadOnlyList<SelectionPaneDialogItemState> items,
        Guid draggedId,
        Guid targetId)
    {
        var draggedIndex = -1;
        var targetIndex = -1;
        for (var index = 0; index < items.Count; index++)
        {
            var id = items[index].Id;
            if (id == draggedId)
                draggedIndex = index;
            else if (id == targetId)
                targetIndex = index;

            if (draggedIndex >= 0 && targetIndex >= 0)
                break;
        }

        return (draggedIndex, targetIndex);
    }

    private static (int DraggedIndex, int TargetIndex) FindDragIndexes(
        IReadOnlyList<(SelectionPaneObjectKind Kind, Guid Id)> items,
        Guid draggedId,
        Guid targetId)
    {
        var draggedIndex = -1;
        var targetIndex = -1;
        for (var index = 0; index < items.Count; index++)
        {
            var id = items[index].Id;
            if (id == draggedId)
                draggedIndex = index;
            else if (id == targetId)
                targetIndex = index;

            if (draggedIndex >= 0 && targetIndex >= 0)
                break;
        }

        return (draggedIndex, targetIndex);
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
        Guid targetId,
        SelectionPaneDropPlacement placement = SelectionPaneDropPlacement.Before) =>
        SelectionPaneDialogStatePlanner.CreateDragMoveChanges(currentOrder, draggedId, targetId, placement);

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
