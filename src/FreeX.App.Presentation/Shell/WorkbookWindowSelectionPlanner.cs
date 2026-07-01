using System.Globalization;

namespace FreeX.App.Presentation.Shell;

public sealed record WorkbookWindowSelectionEntry<TWindow>(
    TWindow Window,
    int ZeroBasedWindowIndex);

public sealed record WorkbookWindowSelectionTarget<TWindow>(
    TWindow Window,
    string DisplayName,
    bool IsCurrent,
    string KeyTip)
{
    public override string ToString() => DisplayName;
}

public static class WorkbookWindowSelectionPlanner
{
    public static IReadOnlyList<WorkbookWindowSelectionTarget<TWindow>> BuildSwitchWindowTargets<TWindow>(
        IEnumerable<WorkbookWindowSelectionEntry<TWindow>> windows,
        TWindow currentWindow,
        string workbookName,
        int totalWindowCount,
        IEqualityComparer<TWindow>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(currentWindow);

        comparer ??= EqualityComparer<TWindow>.Default;

        return windows
            .Select((entry, index) => CreateTarget(
                entry,
                workbookName,
                totalWindowCount,
                isCurrent: comparer.Equals(entry.Window, currentWindow),
                keyTipIndex: index + 1))
            .ToList();
    }

    public static IReadOnlyList<WorkbookWindowSelectionTarget<TWindow>> BuildUnhideWindowTargets<TWindow>(
        IEnumerable<WorkbookWindowSelectionEntry<TWindow>> windows,
        string workbookName,
        int totalWindowCount)
    {
        ArgumentNullException.ThrowIfNull(windows);

        return windows
            .Select((entry, index) => CreateTarget(
                entry,
                workbookName,
                totalWindowCount,
                isCurrent: false,
                keyTipIndex: index + 1))
            .ToList();
    }

    private static WorkbookWindowSelectionTarget<TWindow> CreateTarget<TWindow>(
        WorkbookWindowSelectionEntry<TWindow> entry,
        string workbookName,
        int totalWindowCount,
        bool isCurrent,
        int keyTipIndex) =>
        new(
            entry.Window,
            FormatDisplayName(workbookName, entry.ZeroBasedWindowIndex, totalWindowCount),
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
