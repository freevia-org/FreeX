using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

using SharedSelectionPanePlanner = FreeX.App.Services.SelectionPanePlanner;

internal sealed record SelectionPaneDialogItemState(
    SelectionPaneObjectKind Kind,
    Guid Id,
    string Name,
    bool IsVisible);

internal sealed record SelectionPaneDialogReorderPlan(
    IReadOnlyList<Guid> OrderedIds,
    IReadOnlyList<SelectionPaneMoveChange> MoveChanges);

internal sealed record SelectionPaneDropVisualPlan(
    Guid TargetId,
    SelectionPaneDropPlacement Placement,
    bool IsAllowed);

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
        bool forward) =>
        ToDialogReorderPlan(
            SharedSelectionPanePlanner.PlanMove(ToPlannerItemStates(items), selectedId, forward));

    public static SelectionPaneDialogReorderPlan? PlanDragReorder(
        IReadOnlyList<SelectionPaneDialogItemState> items,
        Guid draggedId,
        Guid targetId,
        SelectionPaneDropPlacement placement = SelectionPaneDropPlacement.Before) =>
        ToDialogReorderPlan(
            SharedSelectionPanePlanner.PlanDragReorder(ToPlannerItemStates(items), draggedId, targetId, placement));

    public static SelectionPaneDropVisualPlan PlanDropVisual(
        IReadOnlyList<SelectionPaneDialogItemState> items,
        Guid draggedId,
        Guid targetId,
        SelectionPaneDropPlacement placement = SelectionPaneDropPlacement.Before)
    {
        var plan = SharedSelectionPanePlanner.PlanDragReorder(
            ToPlannerItemStates(items),
            draggedId,
            targetId,
            placement);
        return new SelectionPaneDropVisualPlan(targetId, placement, plan is not null);
    }

    public static int FindMoveTargetIndex(
        IReadOnlyList<SelectionPaneDialogItemState> items,
        int currentIndex,
        bool forward) =>
        SharedSelectionPanePlanner.FindMoveTargetIndex(ToPlannerItemStates(items), currentIndex, forward);

    public static bool CanReorderKinds(SelectionPaneObjectKind draggedKind, SelectionPaneObjectKind targetKind) =>
        SharedSelectionPanePlanner.CanReorderKinds(draggedKind, targetKind);

    public static IReadOnlyList<SelectionPaneVisibilityChange> CreateVisibilityChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<SelectionPaneDialogItemState> currentStates) =>
        SharedSelectionPanePlanner.CreateVisibilityChanges(originalItems, ToPlannerItemStates(currentStates));

    public static IReadOnlyList<SelectionPaneRenameChange> CreateRenameChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<SelectionPaneDialogItemState> currentStates) =>
        SharedSelectionPanePlanner.CreateRenameChanges(originalItems, ToPlannerItemStates(currentStates));

    public static SelectionPaneDialogResult CreateResult(
        SelectionPaneDialogAction action,
        SelectionPaneItem? target,
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<SelectionPaneDialogItemState> currentStates,
        IReadOnlyList<SelectionPaneMoveChange> moveChanges) =>
        SharedSelectionPanePlanner.CreateResult(
            action,
            target,
            originalItems,
            ToPlannerItemStates(currentStates),
            moveChanges);

    public static IReadOnlyList<SelectionPaneMoveChange> CreateDragMoveChanges(
        IReadOnlyList<(SelectionPaneObjectKind Kind, Guid Id)> currentOrder,
        Guid draggedId,
        Guid targetId,
        SelectionPaneDropPlacement placement = SelectionPaneDropPlacement.Before) =>
        SharedSelectionPanePlanner.CreateDragMoveChanges(currentOrder, draggedId, targetId, placement);

    private static SelectionPaneDialogReorderPlan? ToDialogReorderPlan(SelectionPaneReorderPlan? plan) =>
        plan is null
            ? null
            : new SelectionPaneDialogReorderPlan(plan.OrderedIds, plan.MoveChanges);

    private static IReadOnlyList<SelectionPaneItemState> ToPlannerItemStates(
        IReadOnlyList<SelectionPaneDialogItemState> items)
    {
        var states = new List<SelectionPaneItemState>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            states.Add(new SelectionPaneItemState(item.Kind, item.Id, item.Name, item.IsVisible));
        }

        return states;
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
