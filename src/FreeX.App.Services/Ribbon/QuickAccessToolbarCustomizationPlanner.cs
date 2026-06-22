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
}
