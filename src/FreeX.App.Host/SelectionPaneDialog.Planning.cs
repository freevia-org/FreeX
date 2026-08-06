using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

using SharedSelectionPanePlanner = FreeX.App.Presentation.DrawingUI.SelectionPanePlanner;

public sealed partial class SelectionPaneDialog
{
    public static IReadOnlyList<SelectionPaneItem> BuildItems(Sheet sheet) =>
        SharedSelectionPanePlanner.BuildItems(sheet, CreateLocalizedPlannerText());

    public static IReadOnlyList<SelectionPaneVisibilityChange> CreateVisibilityChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible, string Name)> currentStates) =>
        CreateSession(originalItems, currentStates).CreateResult().VisibilityChanges;

    public static IReadOnlyList<SelectionPaneVisibilityChange> CreateVisibilityChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible)> currentStates) =>
        CreateVisibilityChanges(
            originalItems,
            ToNamedCurrentStates(originalItems, currentStates));

    public static IReadOnlyList<SelectionPaneRenameChange> CreateRenameChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible, string Name)> currentStates) =>
        CreateSession(originalItems, currentStates).CreateResult().RenameChanges;

    public static SelectionPaneDialogResult CreateResult(
        SelectionPaneDialogAction action,
        SelectionPaneItem? target,
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible, string Name)> currentStates) =>
        CreateSession(originalItems, currentStates).CreateResult(action, target);

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
        CreateDragSession(currentOrder, draggedId, targetId, placement).MoveChanges;

    private static SelectionPanePlannerText CreateLocalizedPlannerText() =>
        new(
            UiText.Get("SelectionPane_DefaultChartName"),
            UiText.Get("SelectionPane_DefaultPictureName"),
            UiText.Get("SelectionPane_DefaultTextBoxName"),
            UiText.Get("SelectionPane_DefaultShapeNameFormat"),
            UiText.Get("SelectionPane_DefaultEllipseName"),
            UiText.Get("SelectionPane_DefaultLineName"),
            UiText.Get("SelectionPane_DefaultRectangleName"));

    private static IReadOnlyList<SelectionPaneItemState> ToItemStates(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible, string Name)> currentStates)
    {
        var itemsById = originalItems.ToDictionary(item => item.Id);
        var states = new List<SelectionPaneItemState>(currentStates.Count);
        foreach (var state in currentStates)
        {
            if (itemsById.TryGetValue(state.Id, out var item))
                states.Add(new SelectionPaneItemState(item.Kind, state.Id, state.Name, state.IsVisible));
        }

        return states;
    }

    private static SelectionPaneSession CreateSession(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible, string Name)> currentStates)
    {
        var session = new SelectionPaneSession(originalItems);
        session.ApplyStates(ToItemStates(originalItems, currentStates));
        return session;
    }

    private static SelectionPaneSession CreateDragSession(
        IReadOnlyList<(SelectionPaneObjectKind Kind, Guid Id)> currentOrder,
        Guid draggedId,
        Guid targetId,
        SelectionPaneDropPlacement placement)
    {
        var items = currentOrder
            .Select(item => new SelectionPaneItem(
                item.Kind,
                item.Id,
                item.Kind.ToString(),
                IsVisible: true,
                CanMoveUp: true,
                CanMoveDown: true))
            .ToList();
        var session = new SelectionPaneSession(items);
        session.BeginDrag(draggedId);
        session.Drop(targetId, placement);
        return session;
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
