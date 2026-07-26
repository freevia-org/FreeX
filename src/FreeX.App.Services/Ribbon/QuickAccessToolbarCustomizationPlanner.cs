namespace FreeX.App.Services.Ribbon;

public enum QuickAccessToolbarCustomizationAction
{
    Add,
    Remove
}

public sealed record QuickAccessToolbarCustomizationPlan(
    string CommandId,
    QuickAccessToolbarCustomizationAction Action,
    bool IsEnabled,
    string HeaderResourceKey,
    string AutomationId);

public static class QuickAccessToolbarCustomizationPlanner
{
    public const string AddHeaderResourceKey = QuickAccessToolbarContextMenuPlanner.AddHeaderResourceKey;
    public const string RemoveHeaderResourceKey = QuickAccessToolbarContextMenuPlanner.RemoveHeaderResourceKey;
    public const string AddAutomationId = QuickAccessToolbarContextMenuPlanner.AddAutomationId;
    public const string RemoveAutomationId = QuickAccessToolbarContextMenuPlanner.RemoveAutomationId;

    public static QuickAccessToolbarCustomizationPlan CreatePlan(
        string commandId,
        IEnumerable<string>? currentCommandIds)
    {
        var normalizedCommandIds = QuickAccessToolbarCatalog.NormalizeCommandIds(currentCommandIds);
        var containsCommand = normalizedCommandIds.Contains(commandId, StringComparer.OrdinalIgnoreCase);
        return containsCommand
            ? new(
                commandId,
                QuickAccessToolbarCustomizationAction.Remove,
                normalizedCommandIds.Count > 1,
                RemoveHeaderResourceKey,
                RemoveAutomationId)
            : new(
                commandId,
                QuickAccessToolbarCustomizationAction.Add,
                true,
                AddHeaderResourceKey,
                AddAutomationId);
    }

    public static IReadOnlyList<string> Apply(
        IEnumerable<string>? currentCommandIds,
        string commandId,
        QuickAccessToolbarCustomizationAction action)
    {
        var normalizedCommandIds = QuickAccessToolbarCatalog.NormalizeCommandIds(currentCommandIds).ToList();
        if (!QuickAccessToolbarCatalog.TryGet(commandId, out var command))
            return normalizedCommandIds;

        var index = -1;
        for (var i = 0; i < normalizedCommandIds.Count; i++)
        {
            if (!string.Equals(normalizedCommandIds[i], command.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            index = i;
            break;
        }

        if (action == QuickAccessToolbarCustomizationAction.Add)
        {
            if (index < 0)
                normalizedCommandIds.Add(command.Id);
        }
        else if (index >= 0 && normalizedCommandIds.Count > 1)
        {
            normalizedCommandIds.RemoveAt(index);
        }

        return QuickAccessToolbarCatalog.NormalizeCommandIds(normalizedCommandIds);
    }

    public static IReadOnlyList<QuickAccessToolbarCommandDefinition> FilterAvailable(
        IEnumerable<string>? selectedCommandIds,
        string? searchText,
        Func<QuickAccessToolbarCommandDefinition, IEnumerable<string>>? localizedSearchText = null)
    {
        var selected = new HashSet<string>(
            QuickAccessToolbarCatalog.NormalizeCommandIds(selectedCommandIds),
            StringComparer.OrdinalIgnoreCase);
        var filter = searchText?.Trim() ?? string.Empty;

        return QuickAccessToolbarCatalog.Commands
            .Where(command => !selected.Contains(command.Id))
            .Where(command => string.IsNullOrEmpty(filter) ||
                command.Id.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                command.CommandName.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                (localizedSearchText?.Invoke(command) ?? Array.Empty<string>())
                    .Any(value => value.Contains(filter, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();
    }

    public static IReadOnlyList<string> Move(
        IEnumerable<string>? currentCommandIds,
        string commandId,
        int delta)
    {
        var normalizedCommandIds = QuickAccessToolbarCatalog.NormalizeCommandIds(currentCommandIds).ToList();
        var index = normalizedCommandIds.FindIndex(
            id => string.Equals(id, commandId, StringComparison.OrdinalIgnoreCase));
        var nextIndex = index + delta;
        if (index < 0 || nextIndex < 0 || nextIndex >= normalizedCommandIds.Count)
            return normalizedCommandIds;

        (normalizedCommandIds[index], normalizedCommandIds[nextIndex]) =
            (normalizedCommandIds[nextIndex], normalizedCommandIds[index]);
        return normalizedCommandIds;
    }

    public static IReadOnlyList<string> Reset() =>
        QuickAccessToolbarCatalog.DefaultCommandIds.ToList();
}
