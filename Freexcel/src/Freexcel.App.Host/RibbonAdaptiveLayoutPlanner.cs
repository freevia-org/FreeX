namespace Freexcel.App.Host;

public static class RibbonAdaptiveLayoutPlanner
{
    public static IReadOnlyList<RibbonAdaptiveGroupState> Plan(
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double fixedChromeWidth = 0)
    {
        availableWidth = Math.Max(0, availableWidth - Math.Max(0, fixedChromeWidth));
        var states = Enumerable
            .Repeat(RibbonAdaptiveGroupState.Full, groups.Count)
            .ToArray();

        if (groups.Count == 0 || Fits(availableWidth, groups, states))
            return states;

        foreach (var index in RightToLeft(groups.Count))
        {
            states[index] = RibbonAdaptiveGroupState.SmallWithLabels;
            if (Fits(availableWidth, groups, states))
                return states;

            states[index] = RibbonAdaptiveGroupState.IconOnly;
            if (Fits(availableWidth, groups, states))
                return states;

            if (index == 0 && groups.Count > 1)
                continue;

            states[index] = RibbonAdaptiveGroupState.Collapsed;
            if (Fits(availableWidth, groups, states))
                return states;
        }

        return states;
    }

    public static IReadOnlyList<RibbonAdaptiveGroupState> ApplyBreakpointOverrides(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates)
    {
        var states = plannedStates.ToArray();
        if (availableWidth <= 700)
        {
            for (var i = 0; i < states.Length; i++)
                states[i] = RibbonAdaptiveGroupState.Collapsed;
            return states;
        }

        if (IsHomeRibbonGroupSet(groupNames))
        {
            ApplyHomeBreakpointOverrides(availableWidth, groupNames, states);
            return states;
        }

        if (IsInsertRibbonGroupSet(groupNames))
        {
            ApplyInsertBreakpointOverrides(availableWidth, states);
            return states;
        }

        if (IsFormulasRibbonGroupSet(groupNames))
        {
            ApplyFormulasBreakpointOverrides(availableWidth, states);
            return states;
        }

        if (TryApplyTabSpecificBreakpointOverrides(availableWidth, groupNames, states))
            return states;

        var collapseFrom = availableWidth switch
        {
            <= 760 => 0,
            <= 1120 => 1,
            <= 1320 => 2,
            _ => -1
        };

        if (collapseFrom < 0 || collapseFrom >= states.Length)
            return states;

        for (var i = collapseFrom; i < states.Length; i++)
            states[i] = RibbonAdaptiveGroupState.Collapsed;

        return states;
    }

    private static bool TryApplyTabSpecificBreakpointOverrides(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        RibbonAdaptiveGroupState[] states)
    {
        if (IsTinyRibbonGroupSet(groupNames))
            return true;

        var firstCollapsedIndex = GetFirstCollapsedIndexForKnownTab(availableWidth, groupNames);
        if (firstCollapsedIndex is null)
            return false;

        CollapseFrom(states, firstCollapsedIndex.Value);
        return true;
    }

    private static int? GetFirstCollapsedIndexForKnownTab(double availableWidth, IReadOnlyList<string> groupNames)
    {
        if (availableWidth <= 760)
            return 0;

        if (IsDataRibbonGroupSet(groupNames) || IsViewRibbonGroupSet(groupNames) || IsReviewRibbonGroupSet(groupNames))
            return availableWidth <= 1120 ? 2 : availableWidth <= 1320 ? 3 : null;

        if (IsPageLayoutRibbonGroupSet(groupNames) || IsDrawRibbonGroupSet(groupNames))
            return availableWidth <= 1120 ? 1 : availableWidth <= 1320 ? 2 : null;

        return null;
    }

    private static void CollapseFrom(RibbonAdaptiveGroupState[] states, int firstCollapsedIndex)
    {
        for (var i = Math.Clamp(firstCollapsedIndex, 0, states.Length); i < states.Length; i++)
            states[i] = RibbonAdaptiveGroupState.Collapsed;
    }

    private static bool Fits(
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        IReadOnlyList<RibbonAdaptiveGroupState> states) =>
        groups.Select((group, index) => WidthFor(group, states[index])).Sum() <= availableWidth;

    private static double WidthFor(RibbonAdaptiveGroup group, RibbonAdaptiveGroupState state) =>
        state switch
        {
            RibbonAdaptiveGroupState.Full => group.FullWidth,
            RibbonAdaptiveGroupState.SmallWithLabels => group.SmallWithLabelsWidth,
            RibbonAdaptiveGroupState.IconOnly => group.IconOnlyWidth,
            RibbonAdaptiveGroupState.Collapsed => group.CollapsedWidth,
            _ => group.FullWidth
        };

    private static IEnumerable<int> RightToLeft(int count)
    {
        for (var i = count - 1; i >= 0; i--)
            yield return i;
    }

    private static bool IsHomeRibbonGroupSet(IReadOnlyList<string> groupNames) =>
        groupNames.Count >= 7 &&
        TryFindGroupIndex(groupNames, "Clipboard", out _) &&
        TryFindGroupIndex(groupNames, "Font", out _) &&
        TryFindGroupIndex(groupNames, "Alignment", out _) &&
        TryFindGroupIndex(groupNames, "Number", out _);

    private static bool IsInsertRibbonGroupSet(IReadOnlyList<string> groupNames) =>
        groupNames.Count >= 4 && TryFindGroupIndex(groupNames, "Tables", out _);

    private static bool IsFormulasRibbonGroupSet(IReadOnlyList<string> groupNames) =>
        groupNames.Count >= 4 &&
        TryFindGroupIndex(groupNames, "Function Library", out _) &&
        TryFindGroupIndex(groupNames, "Formula Auditing", out _);

    private static bool IsDataRibbonGroupSet(IReadOnlyList<string> groupNames) =>
        groupNames.Count >= 5 &&
        TryFindGroupIndex(groupNames, "Get & Transform", out _) &&
        TryFindGroupIndex(groupNames, "Sort & Filter", out _) &&
        TryFindGroupIndex(groupNames, "Data Tools", out _);

    private static bool IsViewRibbonGroupSet(IReadOnlyList<string> groupNames) =>
        groupNames.Count >= 5 &&
        TryFindGroupIndex(groupNames, "Workbook Views", out _) &&
        TryFindGroupIndex(groupNames, "Show", out _) &&
        TryFindGroupIndex(groupNames, "Freeze Panes", out _);

    private static bool IsReviewRibbonGroupSet(IReadOnlyList<string> groupNames) =>
        groupNames.Count >= 4 &&
        TryFindGroupIndex(groupNames, "Proofing", out _) &&
        TryFindGroupIndex(groupNames, "Accessibility", out _) &&
        TryFindGroupIndex(groupNames, "Comments", out _);

    private static bool IsPageLayoutRibbonGroupSet(IReadOnlyList<string> groupNames) =>
        groupNames.Count >= 3 &&
        TryFindGroupIndex(groupNames, "Themes", out _) &&
        TryFindGroupIndex(groupNames, "Page Setup", out _) &&
        TryFindGroupIndex(groupNames, "Sheet Options", out _);

    private static bool IsDrawRibbonGroupSet(IReadOnlyList<string> groupNames) =>
        groupNames.Count >= 3 &&
        TryFindGroupIndex(groupNames, "Draw", out _) &&
        TryFindGroupIndex(groupNames, "Arrange", out _) &&
        TryFindGroupIndex(groupNames, "Format", out _);

    private static bool IsTinyRibbonGroupSet(IReadOnlyList<string> groupNames) =>
        groupNames.Count <= 2 &&
        (TryFindGroupIndex(groupNames, "Help", out _) ||
         TryFindGroupIndex(groupNames, "PivotTable", out _) ||
         TryFindGroupIndex(groupNames, "Layout", out _));

    private static void ApplyInsertBreakpointOverrides(
        double availableWidth,
        RibbonAdaptiveGroupState[] states)
    {
        if (availableWidth <= 760)
        {
            for (var i = 0; i < states.Length; i++)
                states[i] = RibbonAdaptiveGroupState.Collapsed;
            return;
        }

        if (states.Length > 0)
            states[0] = RibbonAdaptiveGroupState.Full;

        if (availableWidth <= 1120)
        {
            for (var i = 1; i < states.Length; i++)
                states[i] = RibbonAdaptiveGroupState.Collapsed;
            return;
        }

        if (availableWidth <= 1320)
        {
            for (var i = 3; i < states.Length; i++)
                states[i] = RibbonAdaptiveGroupState.Collapsed;
        }
    }

    private static void ApplyFormulasBreakpointOverrides(
        double availableWidth,
        RibbonAdaptiveGroupState[] states)
    {
        if (availableWidth <= 760)
        {
            for (var i = 0; i < states.Length; i++)
                states[i] = RibbonAdaptiveGroupState.Collapsed;
            return;
        }

        if (states.Length > 0)
            states[0] = RibbonAdaptiveGroupState.Full;

        if (availableWidth <= 1120)
        {
            for (var i = 1; i < states.Length; i++)
                states[i] = RibbonAdaptiveGroupState.Collapsed;
        }
        else if (availableWidth <= 1320)
        {
            for (var i = 2; i < states.Length; i++)
                states[i] = RibbonAdaptiveGroupState.Collapsed;
        }
    }

    private static void ApplyHomeBreakpointOverrides(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        RibbonAdaptiveGroupState[] states)
    {
        if (availableWidth <= 900 &&
            TryFindGroupIndex(groupNames, "Font", out var fontIndex))
        {
            for (var i = fontIndex; i < states.Length; i++)
                states[i] = RibbonAdaptiveGroupState.Collapsed;
            return;
        }

        if (availableWidth <= 1120 &&
            TryFindGroupIndex(groupNames, "Alignment", out var alignmentIndex))
        {
            for (var i = alignmentIndex; i < states.Length; i++)
                states[i] = RibbonAdaptiveGroupState.Collapsed;
            return;
        }

        if (availableWidth <= 1300 &&
            TryFindGroupIndex(groupNames, "Styles", out var stylesIndex))
        {
            for (var i = stylesIndex; i < states.Length; i++)
                states[i] = RibbonAdaptiveGroupState.Collapsed;
            return;
        }

        if (availableWidth <= 1500 &&
            TryFindGroupIndex(groupNames, "Cells", out var cellsIndex))
        {
            for (var i = cellsIndex; i < states.Length; i++)
                states[i] = RibbonAdaptiveGroupState.Collapsed;
            return;
        }

        if (availableWidth <= 1500 &&
            TryFindGroupIndex(groupNames, "Editing", out var editingIndex))
        {
            for (var i = editingIndex; i < states.Length; i++)
                states[i] = RibbonAdaptiveGroupState.Collapsed;
        }
    }

    private static bool TryFindGroupIndex(
        IReadOnlyList<string> groupNames,
        string groupName,
        out int index)
    {
        for (var i = 0; i < groupNames.Count; i++)
        {
            if (string.Equals(groupNames[i], groupName, StringComparison.Ordinal))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }
}

public sealed record RibbonAdaptiveGroup(
    string Name,
    double FullWidth,
    double SmallWithLabelsWidth,
    double IconOnlyWidth,
    double CollapsedWidth);

public enum RibbonAdaptiveGroupState
{
    Full,
    SmallWithLabels,
    IconOnly,
    Collapsed
}
