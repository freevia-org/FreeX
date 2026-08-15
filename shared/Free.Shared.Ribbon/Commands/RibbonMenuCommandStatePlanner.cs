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

    /// <summary>
    /// Projects a normal ribbon control into the menu-item state used when its group is adaptively
    /// collapsed. Toggle and check-box controls remain checkable in that projection; ordinary
    /// commands expose only availability.
    /// </summary>
    public static RibbonMenuCommandState PlanCollapsedControl(
        RibbonControl control,
        bool commandAvailable,
        RibbonCommandState? commandState)
    {
        ArgumentNullException.ThrowIfNull(control);

        var isCheckable = control is RibbonToggleButton or RibbonCheckBox;
        return new RibbonMenuCommandState(
            commandAvailable && (commandState?.IsEnabled ?? true),
            isCheckable ? commandState?.IsChecked ?? false : null);
    }
}
