namespace Free.Shared.Shell;

public enum BackstageRailNavigationKey
{
    Other,
    Escape,
    Home,
    End,
    Up,
    Down,
}

public readonly record struct BackstageRailNavigationPlan(
    bool IsHandled,
    bool DismissFrame,
    int? TargetIndex)
{
    public static BackstageRailNavigationPlan Unhandled { get; } = new(false, false, null);

    public static BackstageRailNavigationPlan Dismiss { get; } = new(true, true, null);

    public static BackstageRailNavigationPlan Focus(int targetIndex) => new(true, false, targetIndex);
}

/// <summary>
/// Owns portable Backstage rail key semantics. Renderers retain native key translation,
/// rail membership detection, and focus realization.
/// </summary>
public static class BackstageRailNavigationPlanner
{
    public static BackstageRailNavigationPlan Plan(
        BackstageRailNavigationKey key,
        bool hasModifiers,
        int focusedIndex,
        int focusableCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(focusableCount);

        if (key == BackstageRailNavigationKey.Escape)
            return BackstageRailNavigationPlan.Dismiss;

        if (hasModifiers ||
            focusableCount == 0 ||
            focusedIndex < 0 ||
            focusedIndex >= focusableCount)
        {
            return BackstageRailNavigationPlan.Unhandled;
        }

        return key switch
        {
            BackstageRailNavigationKey.Home => BackstageRailNavigationPlan.Focus(0),
            BackstageRailNavigationKey.End => BackstageRailNavigationPlan.Focus(focusableCount - 1),
            BackstageRailNavigationKey.Up => BackstageRailNavigationPlan.Focus(Math.Max(0, focusedIndex - 1)),
            BackstageRailNavigationKey.Down => BackstageRailNavigationPlan.Focus(
                Math.Min(focusableCount - 1, focusedIndex + 1)),
            _ => BackstageRailNavigationPlan.Unhandled,
        };
    }
}
