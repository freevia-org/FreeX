using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed partial class SelectionPaneDialog
{
    public static IReadOnlyList<SelectionPaneVisibilityChange> CreateVisibilityChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible, string Name)> currentStates)
    {
        var states = currentStates.ToDictionary(state => state.Id, state => state.IsVisible);
        return originalItems
            .Where(item => states.TryGetValue(item.Id, out var isVisible) && isVisible != item.IsVisible)
            .Select(item => new SelectionPaneVisibilityChange(item.Kind, item.Id, states[item.Id]))
            .ToList();
    }

    public static IReadOnlyList<SelectionPaneVisibilityChange> CreateVisibilityChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible)> currentStates) =>
        CreateVisibilityChanges(
            originalItems,
            currentStates
                .Select(state => (state.Id, state.IsVisible, Name: originalItems.FirstOrDefault(item => item.Id == state.Id)?.Name ?? ""))
                .ToList());

    public static IReadOnlyList<SelectionPaneRenameChange> CreateRenameChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible, string Name)> currentStates)
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
        IReadOnlyList<(Guid Id, bool IsVisible, string Name)> currentStates) =>
        new(
            action,
            target,
            CreateVisibilityChanges(originalItems, currentStates),
            CreateRenameChanges(originalItems, currentStates),
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
            currentStates
                .Select(state => (state.Id, state.IsVisible, Name: originalItems.FirstOrDefault(item => item.Id == state.Id)?.Name ?? ""))
                .ToList());

    private static string NormalizeName(string name) => name.Trim();
}
