namespace Free.Shared.Ribbon.KeyTips;

public readonly record struct MenuKeyTipAssignmentCandidate(
    string? Header,
    string? ExistingKeyTip = null);

public static class MenuKeyTipAssignmentPlanner
{
    public static IReadOnlyList<string> AssignUnique(
        IReadOnlyList<MenuKeyTipAssignmentCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var assignments = new string[candidates.Count];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < candidates.Count; index++)
        {
            var existing = RibbonKeyTipText.NormalizeOrEmpty(candidates[index].ExistingKeyTip);
            if (!RibbonKeyTipText.IsTypeableKeyTip(existing) ||
                !RibbonKeyTipText.IsAvailable(existing, used))
            {
                continue;
            }

            assignments[index] = existing;
            used.Add(existing);
        }

        for (var index = 0; index < candidates.Count; index++)
        {
            if (!string.IsNullOrEmpty(assignments[index]))
                continue;

            var keyTip = RibbonKeyTipText.CreateUniqueKeyTip(candidates[index].Header, used);
            assignments[index] = keyTip;
            used.Add(keyTip);
        }

        return assignments;
    }
}
