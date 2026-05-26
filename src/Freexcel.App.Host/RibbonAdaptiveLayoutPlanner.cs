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
            ApplyInsertBreakpointOverrides(availableWidth, groupNames, states);
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

        if (IsDataRibbonGroupSet(groupNames))
        {
            ApplyPriorityState(
                states,
                groupNames,
                ["Get & Transform Data", "Sort & Filter"],
                RibbonAdaptiveGroupState.Full);
            ApplyPriorityCollapse(
                states,
                groupNames,
                availableWidth <= 1120
                    ? ["Queries & Connections", "Data Types", "Data Tools", "Forecast", "Outline"]
                    : availableWidth <= 1320
                        ? ["Data Types", "Forecast", "Outline"]
                        : []);
            return true;
        }

        if (IsPageLayoutRibbonGroupSet(groupNames))
        {
            ApplyPriorityCollapse(
                states,
                groupNames,
                availableWidth <= 1120
                    ? ["Themes", "Arrange"]
                    : availableWidth <= 1320
                        ? ["Arrange"]
                        : []);
            return true;
        }

        if (IsReviewRibbonGroupSet(groupNames))
        {
            ApplyPriorityCollapse(
                states,
                groupNames,
                availableWidth <= 1120
                    ? ["Accessibility", "Notes", "Protect"]
                    : availableWidth <= 1320
                        ? ["Notes", "Protect"]
                        : []);
            return true;
        }

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

        if (IsViewRibbonGroupSet(groupNames))
            return availableWidth <= 1120 ? 2 : availableWidth <= 1320 ? 3 : null;

        if (IsDrawRibbonGroupSet(groupNames))
            return availableWidth <= 1120 ? 1 : availableWidth <= 1320 ? 2 : null;

        return null;
    }

    private static void CollapseFrom(RibbonAdaptiveGroupState[] states, int firstCollapsedIndex)
    {
        for (var i = Math.Clamp(firstCollapsedIndex, 0, states.Length); i < states.Length; i++)
            states[i] = RibbonAdaptiveGroupState.Collapsed;
    }

    private static void ApplyPriorityCollapse(
        RibbonAdaptiveGroupState[] states,
        IReadOnlyList<string> groupNames,
        IReadOnlyList<string> collapsedGroups)
    {
        ApplyPriorityState(states, groupNames, collapsedGroups, RibbonAdaptiveGroupState.Collapsed);
    }

    private static void ApplyPriorityState(
        RibbonAdaptiveGroupState[] states,
        IReadOnlyList<string> groupNames,
        IReadOnlyList<string> groupNamesToUpdate,
        RibbonAdaptiveGroupState state)
    {
        foreach (var groupName in groupNamesToUpdate)
        {
            if (TryFindGroupIndex(groupNames, groupName, out var index))
                states[index] = state;
        }
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
            TryFindGroupIndex(groupNames, "Get & Transform Data", out _) &&
            TryFindGroupIndex(groupNames, "Queries & Connections", out _) &&
            TryFindGroupIndex(groupNames, "Data Types", out _) &&
            TryFindGroupIndex(groupNames, "Sort & Filter", out _) &&
            TryFindGroupIndex(groupNames, "Data Tools", out _);

    private static bool IsViewRibbonGroupSet(IReadOnlyList<string> groupNames) =>
        groupNames.Count >= 5 &&
        TryFindGroupIndex(groupNames, "Workbook Views", out _) &&
        TryFindGroupIndex(groupNames, "Show", out _) &&
        TryFindGroupIndex(groupNames, "Window", out _);

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
        IReadOnlyList<string> groupNames,
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
            if (states.Length > 0)
                states[0] = RibbonAdaptiveGroupState.SmallWithLabels;

            ApplyPriorityCollapse(
                states,
                groupNames,
                ["Add-ins", "Tours", "Sparklines", "Filters", "Links", "Text", "Symbols", "Comments"]);
            return;
        }

        if (availableWidth <= 1320)
        {
            ApplyPriorityCollapse(
                states,
                groupNames,
                ["Add-ins", "Tours", "Sparklines", "Filters", "Links", "Text", "Symbols", "Comments"]);
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
            for (var i = 2; i < states.Length; i++)
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
            TryFindGroupIndex(groupNames, "Alignment", out var alignmentIndexAtNarrow))
        {
            for (var i = alignmentIndexAtNarrow; i < states.Length; i++)
                states[i] = RibbonAdaptiveGroupState.Collapsed;
            return;
        }

        if (availableWidth <= 1120 &&
            TryFindGroupIndex(groupNames, "Styles", out var stylesIndexAtMedium))
        {
            for (var i = stylesIndexAtMedium; i < states.Length; i++)
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
