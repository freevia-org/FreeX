namespace Free.Shared.Ribbon;

/// <summary>
/// Resolves the platform-neutral presentation values for one menu item before a native menu
/// renderer applies its toolkit-specific shortcut property.
/// </summary>
public static class RibbonMenuItemPresentationPlanner
{
    public static RibbonMenuItemPresentation Plan(RibbonMenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new(
            item.Header,
            item.InputGesture ?? string.Empty,
            item.KeyTip ?? string.Empty);
    }
}

public readonly record struct RibbonMenuItemPresentation(
    string Header,
    string InputGestureText,
    string KeyTip);
