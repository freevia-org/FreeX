namespace Free.Shared.Ribbon;

/// <summary>A measured renderer group that can collapse to one representative group button.</summary>
public sealed record RibbonAdaptiveCollapseGroup(
    string Id,
    double FullWidth,
    double CollapsedWidth,
    int Priority);

/// <summary>The shared full/collapsed decision for a measured renderer group.</summary>
public sealed record RibbonAdaptiveCollapseDecision(
    string Id,
    RibbonAdaptiveGroupState State)
{
    public bool IsCollapsed => State == RibbonAdaptiveGroupState.Collapsed;
}

/// <summary>
/// Shared collapse policy for renderers that currently support only full group content or a collapsed
/// representative button. More granular states still flow through <see cref="RibbonAdaptiveLayoutPlanner"/>.
/// </summary>
public static class RibbonAdaptiveCollapsePolicy
{
    private const double MinimumUsefulWidthDelta = 0.5;

    public static RibbonAdaptiveCollapseDecision[] Plan(
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveCollapseGroup> groups,
        double fixedChromeWidth = 0)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var decisions = groups
            .Select(group => new RibbonAdaptiveCollapseDecision(group.Id, RibbonAdaptiveGroupState.Full))
            .ToArray();
        if (groups.Count == 0)
            return decisions;

        var fitAvailable = NormalizeAvailableWidth(availableWidth, fixedChromeWidth);
        if (double.IsPositiveInfinity(fitAvailable))
            return decisions;

        var totalWidth = groups.Sum(group => NormalizeWidth(group.FullWidth));
        if (totalWidth <= fitAvailable)
            return decisions;

        foreach (var entry in groups
                     .Select((Group, Index) => new { Group, Index })
                     .OrderBy(entry => entry.Group.Priority)
                     .ThenBy(entry => entry.Index))
        {
            if (totalWidth <= fitAvailable)
                break;

            var fullWidth = NormalizeWidth(entry.Group.FullWidth);
            var collapsedWidth = NormalizeWidth(entry.Group.CollapsedWidth);
            if (collapsedWidth >= fullWidth - MinimumUsefulWidthDelta)
                continue;

            decisions[entry.Index] = decisions[entry.Index] with { State = RibbonAdaptiveGroupState.Collapsed };
            totalWidth = totalWidth - fullWidth + collapsedWidth;
        }

        return decisions;
    }

    private static double NormalizeAvailableWidth(double availableWidth, double fixedChromeWidth)
    {
        if (double.IsPositiveInfinity(availableWidth))
            return double.PositiveInfinity;

        if (double.IsNaN(availableWidth) || availableWidth <= 0)
            return 0;

        return Math.Max(0, availableWidth - Math.Max(0, NormalizeWidth(fixedChromeWidth)));
    }

    private static double NormalizeWidth(double width) =>
        double.IsNaN(width) || double.IsNegativeInfinity(width)
            ? 0
            : Math.Max(0, width);
}
