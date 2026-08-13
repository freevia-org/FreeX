using Free.Shared.Ribbon;

namespace FreeX.Ribbon.Definitions;

/// <summary>
/// Canonical command identities derived from the ribbon definition. Consumers retain their endpoint
/// bindings, while the definition remains the only inventory of valid control and menu command ids.
/// </summary>
public static class FreeXRibbonCommandCatalog
{
    private static readonly IReadOnlyDictionary<string, RibbonCommandId> CommandsByValue = BuildCatalog();

    public static IReadOnlyCollection<RibbonCommandId> All { get; } =
        CommandsByValue.Values.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();

    public static RibbonCommandId GetRequired(string value)
    {
        if (TryGet(value, out var commandId))
            return commandId;

        throw new ArgumentException($"'{value}' is not a command id emitted by FreeXRibbon.Build().", nameof(value));
    }

    public static bool TryGet(string value, out RibbonCommandId commandId) =>
        CommandsByValue.TryGetValue(value, out commandId);

    public static IEnumerable<RibbonCommandId> Enumerate(RibbonDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        foreach (var tab in definition.Tabs)
        foreach (var group in tab.Groups)
        foreach (var control in group.Controls)
        {
            if (!string.IsNullOrEmpty(control.CommandId.Value))
                yield return control.CommandId;

            var menu = control switch
            {
                RibbonSplitButton split => split.Menu,
                RibbonDropdown dropdown => dropdown.Menu,
                _ => null,
            };
            if (menu is null)
                continue;

            foreach (var commandId in Enumerate(menu.Items))
                yield return commandId;
        }
    }

    private static IReadOnlyDictionary<string, RibbonCommandId> BuildCatalog()
    {
        var result = new Dictionary<string, RibbonCommandId>(StringComparer.Ordinal);
        foreach (var commandId in Enumerate(FreeXRibbon.Build()))
            result.TryAdd(commandId.Value, commandId);
        return result;
    }

    private static IEnumerable<RibbonCommandId> Enumerate(IReadOnlyList<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.CommandId is { } commandId && !string.IsNullOrEmpty(commandId.Value))
                yield return commandId;

            foreach (var childId in Enumerate(item.Children))
                yield return childId;
        }
    }
}
