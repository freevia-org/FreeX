using System.Globalization;

namespace FreeX.App.Presentation.Shell;

/// <param name="DisplayName">
/// Optional pre-computed entry text ("{workbook name}{title suffix}"). Supplied when the windows
/// span multiple documents, where each entry must show its own workbook's name; when null the
/// planner falls back to numbering every entry under the single shared workbook name.
/// </param>
public sealed record WorkbookWindowSelectionEntry<TWindow>(
    TWindow Window,
    int ZeroBasedWindowIndex,
    string? DisplayName = null);

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
            entry.DisplayName ?? FormatDisplayName(workbookName, entry.ZeroBasedWindowIndex, totalWindowCount),
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

    /// <summary>
    /// Entry text for a window whose per-document title suffix is already known
    /// ("{workbook name}{suffix}"). Used to build <see cref="WorkbookWindowSelectionEntry{TWindow}.DisplayName"/>
    /// overrides so multi-document window lists label each entry with its own workbook.
    /// </summary>
    public static string FormatDisplayName(string workbookName, string titleSuffix)
    {
        var name = string.IsNullOrWhiteSpace(workbookName)
            ? "Workbook"
            : workbookName.Trim();
        return $"{name}{titleSuffix}";
    }
}
