namespace FreeX.App.Services;

public enum StatusBarFocusTarget
{
    ZoomOutButton,
    ZoomSlider,
    ZoomInButton,
    ZoomText,
    NormalViewButton,
    PageLayoutViewButton,
    PageBreakPreviewButton
}

public enum StatusBarKeyboardNavigationKey
{
    Other,
    Tab,
    Escape
}

public enum StatusBarKeyboardNavigationAction
{
    Ignore,
    MoveFocus,
    ReturnToWorksheet
}

public readonly record struct StatusBarFocusCandidate(
    StatusBarFocusTarget Target,
    bool IsAvailable);

public readonly record struct StatusBarKeyboardNavigationPlan(
    StatusBarKeyboardNavigationAction Action,
    StatusBarFocusTarget? Target);

public static class StatusBarFocusNavigationPlanner
{
    public static IReadOnlyList<StatusBarFocusTarget> FocusOrder { get; } = Array.AsReadOnly(
    [
        StatusBarFocusTarget.ZoomOutButton,
        StatusBarFocusTarget.ZoomSlider,
        StatusBarFocusTarget.ZoomInButton,
        StatusBarFocusTarget.ZoomText,
        StatusBarFocusTarget.NormalViewButton,
        StatusBarFocusTarget.PageLayoutViewButton,
        StatusBarFocusTarget.PageBreakPreviewButton
    ]);

    public static IReadOnlyList<StatusBarFocusTarget> BuildInitialFocusOrder(
        IReadOnlyCollection<StatusBarFocusCandidate> candidates) =>
        BuildAvailableFocusOrder(candidates);

    public static StatusBarKeyboardNavigationPlan BuildKeyboardNavigationPlan(
        StatusBarKeyboardNavigationKey key,
        bool reverse,
        StatusBarFocusTarget? currentTarget,
        IReadOnlyCollection<StatusBarFocusCandidate> candidates)
    {
        if (key == StatusBarKeyboardNavigationKey.Escape)
            return new StatusBarKeyboardNavigationPlan(StatusBarKeyboardNavigationAction.ReturnToWorksheet, null);

        if (key != StatusBarKeyboardNavigationKey.Tab)
            return new StatusBarKeyboardNavigationPlan(StatusBarKeyboardNavigationAction.Ignore, null);

        var availableTargets = BuildAvailableFocusOrder(candidates);
        if (availableTargets.Count == 0)
            return new StatusBarKeyboardNavigationPlan(StatusBarKeyboardNavigationAction.Ignore, null);

        var currentIndex = currentTarget is { } target
            ? IndexOf(availableTargets, target)
            : -1;
        var nextIndex = currentIndex < 0
            ? 0
            : (currentIndex + (reverse ? -1 : 1) + availableTargets.Count) % availableTargets.Count;

        return new StatusBarKeyboardNavigationPlan(
            StatusBarKeyboardNavigationAction.MoveFocus,
            availableTargets[nextIndex]);
    }

    private static IReadOnlyList<StatusBarFocusTarget> BuildAvailableFocusOrder(
        IReadOnlyCollection<StatusBarFocusCandidate> candidates)
    {
        if (candidates.Count == 0)
            return [];

        var result = new List<StatusBarFocusTarget>(Math.Min(candidates.Count, FocusOrder.Count));
        foreach (var target in FocusOrder)
        {
            if (IsAvailable(candidates, target))
                result.Add(target);
        }

        return result;
    }

    private static bool IsAvailable(
        IReadOnlyCollection<StatusBarFocusCandidate> candidates,
        StatusBarFocusTarget target)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Target == target)
                return candidate.IsAvailable;
        }

        return false;
    }

    private static int IndexOf(
        IReadOnlyList<StatusBarFocusTarget> targets,
        StatusBarFocusTarget target)
    {
        for (var i = 0; i < targets.Count; i++)
        {
            if (targets[i] == target)
                return i;
        }

        return -1;
    }
}
