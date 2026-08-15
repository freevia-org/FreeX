namespace Free.Shared.Ribbon;

/// <summary>
/// Renderer-neutral presentation state for a ribbon menu command. The declaration decides whether
/// an item is checkable and may permanently disable it; the live command supplies current
/// availability and checked state.
/// </summary>
public sealed record RibbonMenuCommandState(
    bool IsEnabled,
    bool? IsChecked);

public static class RibbonMenuCommandStatePlanner
{
    public static RibbonMenuCommandState Plan(
        RibbonMenuItem definition,
        bool commandAvailable,
        RibbonCommandState? commandState)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new RibbonMenuCommandState(
            definition.IsEnabled && commandAvailable && (commandState?.IsEnabled ?? true),
            definition.IsChecked is null
                ? null
                : commandState?.IsChecked ?? definition.IsChecked);
    }
}
