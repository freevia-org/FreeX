using System.Globalization;

namespace FreeX.App.Host;

public sealed record WorkbookWindowSelectionTarget(
    IWorkbookWindow Window,
    string DisplayName,
    bool IsCurrent,
    string KeyTip)
{
    public override string ToString() => DisplayName;
}

public static class WorkbookWindowSelectionPlanner
{
    public static IReadOnlyList<WorkbookWindowSelectionTarget> BuildSwitchWindowTargets(
        WorkbookWindowRegistry registry,
        IWorkbookWindow currentWindow,
        string workbookName)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(currentWindow);

        return registry.VisibleWindows
            .Select((window, index) => CreateTarget(
                registry,
                window,
                workbookName,
                isCurrent: ReferenceEquals(window, currentWindow),
                keyTipIndex: index + 1))
            .ToList();
    }

    public static IReadOnlyList<WorkbookWindowSelectionTarget> BuildUnhideWindowTargets(
        WorkbookWindowRegistry registry,
        string workbookName)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return registry.HiddenWindows
            .Select((window, index) => CreateTarget(
                registry,
                window,
                workbookName,
                isCurrent: false,
                keyTipIndex: index + 1))
            .ToList();
    }

    private static WorkbookWindowSelectionTarget CreateTarget(
        WorkbookWindowRegistry registry,
        IWorkbookWindow window,
        string workbookName,
        bool isCurrent,
        int keyTipIndex) =>
        new(
            window,
            FormatDisplayName(workbookName, registry.IndexOf(window), registry.Count),
            isCurrent,
            keyTipIndex.ToString(CultureInfo.InvariantCulture));

    public static string FormatDisplayName(string workbookName, int zeroBasedWindowIndex, int totalWindowCount)
    {
        var name = string.IsNullOrWhiteSpace(workbookName)
            ? "Workbook"
            : workbookName.Trim();
        var suffix = WorkbookWindowOrdering.FormatWindowTitleSuffix(zeroBasedWindowIndex + 1, totalWindowCount);
        return $"{name}{suffix}";
    }
}
