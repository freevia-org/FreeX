namespace FreeX.App.Host;

internal enum QuickAccessToolbarCustomizationAction
{
    Add,
    Remove
}

internal sealed record QuickAccessToolbarCustomizationPlan(
    string CommandId,
    QuickAccessToolbarCustomizationAction Action,
    bool IsEnabled,
    string HeaderResourceKey,
    string AutomationId);

internal static class QuickAccessToolbarCustomizationPlanner
{
    public const string AddHeaderResourceKey = "MainWindow_QatContext_AddToQuickAccessToolbar";
    public const string RemoveHeaderResourceKey = "MainWindow_QatContext_RemoveFromQuickAccessToolbar";
    public const string AddAutomationId = "AddToQuickAccessToolbarMenuItem";
    public const string RemoveAutomationId = "RemoveFromQuickAccessToolbarMenuItem";

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

        var index = normalizedCommandIds.FindIndex(id => string.Equals(id, command.Id, StringComparison.OrdinalIgnoreCase));
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
