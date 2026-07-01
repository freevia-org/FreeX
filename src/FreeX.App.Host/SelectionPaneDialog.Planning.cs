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
        SharedSelectionPanePlanner.CreateVisibilityChanges(originalItems, ToItemStates(originalItems, currentStates));

    public static IReadOnlyList<SelectionPaneVisibilityChange> CreateVisibilityChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible)> currentStates) =>
        CreateVisibilityChanges(
            originalItems,
            ToNamedCurrentStates(originalItems, currentStates));

    public static IReadOnlyList<SelectionPaneRenameChange> CreateRenameChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible, string Name)> currentStates) =>
        SharedSelectionPanePlanner.CreateRenameChanges(originalItems, ToItemStates(originalItems, currentStates));

    public static SelectionPaneDialogResult CreateResult(
        SelectionPaneDialogAction action,
        SelectionPaneItem? target,
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible, string Name)> currentStates) =>
        SharedSelectionPanePlanner.CreateResult(
            action,
            target,
            originalItems,
            ToItemStates(originalItems, currentStates),
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
        SharedSelectionPanePlanner.CreateDragMoveChanges(currentOrder, draggedId, targetId, placement);

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
