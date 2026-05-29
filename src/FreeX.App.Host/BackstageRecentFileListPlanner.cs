namespace FreeX.App.Host;

public sealed record BackstageRecentFileListPlan(
    IReadOnlyList<RecentFileViewModel> AllItems,
    IReadOnlyList<RecentFileViewModel> RecentItems,
    IReadOnlyList<RecentFileViewModel> PinnedItems);

public static class BackstageRecentFileListPlanner
{
    public static BackstageRecentFileListPlan Build(
        IEnumerable<RecentFileEntry> entries,
        string? filter,
        Func<string, bool>? pathExists = null)
    {
        pathExists ??= _ => true;
        var normalizedFilter = NormalizeFilter(filter);
        var eligibleEntries = new List<RecentFileEntry>();
        foreach (var entry in entries)
        {
            if (pathExists(entry.Path))
            {
                eligibleEntries.Add(entry);
            }
        }

        eligibleEntries.Sort(static (left, right) => right.LastOpened.CompareTo(left.LastOpened));

        var allItems = new List<RecentFileViewModel>(eligibleEntries.Count);
        var recentItems = new List<RecentFileViewModel>(eligibleEntries.Count);
        var pinnedItems = new List<RecentFileViewModel>(eligibleEntries.Count);
        foreach (var entry in eligibleEntries)
        {
            var item = new RecentFileViewModel(entry);
            if (!MatchesFilter(item, normalizedFilter))
            {
                continue;
            }

            allItems.Add(item);
            if (item.IsPinned)
            {
                pinnedItems.Add(item);
            }
            else
            {
                recentItems.Add(item);
            }
        }

        return new BackstageRecentFileListPlan(
            allItems,
            recentItems,
            pinnedItems);
    }

    private static string? NormalizeFilter(string? filter) =>
        string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();

    private static bool MatchesFilter(RecentFileViewModel item, string? filter) =>
        filter is null ||
        item.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        item.Directory.Contains(filter, StringComparison.OrdinalIgnoreCase);
}

public sealed class RecentFileViewModel
{
    public string Path { get; }
    public string FileName { get; }
    public string Directory { get; }
    public string LastOpenedText { get; }
    public bool IsPinned { get; }
    public string OpenAutomationName { get; }
    public string OpenAutomationHelpText { get; }
    public string PinAutomationName { get; }
    public string PinAutomationHelpText { get; }
    public string RemoveAutomationName { get; }
    public string RemoveAutomationHelpText { get; }

    public RecentFileViewModel(RecentFileEntry entry)
    {
        Path = entry.Path;
        FileName = System.IO.Path.GetFileName(entry.Path);
        Directory = System.IO.Path.GetDirectoryName(entry.Path) ?? "";
        LastOpenedText = FormatDate(entry.LastOpened);
        IsPinned = entry.IsPinned;
        OpenAutomationName = IsPinned ? $"Open pinned file {FileName}" : $"Open recent file {FileName}";
        OpenAutomationHelpText = $"Open {Path}";
        PinAutomationName = IsPinned ? $"Unpin {FileName}" : $"Pin {FileName}";
        PinAutomationHelpText = IsPinned
            ? "Remove this workbook from the pinned files list."
            : "Keep this workbook in the pinned files list.";
        RemoveAutomationName = $"Remove {FileName} from recent files";
        RemoveAutomationHelpText = "Remove this workbook from the recent files list without deleting it.";
    }

    private static string FormatDate(DateTimeOffset timestamp)
    {
        var localTimestamp = timestamp.ToLocalTime();
        var now = DateTimeOffset.Now;
        var diff = now - localTimestamp;
        if (diff.TotalHours < 1) return "Just now";
        if (diff.TotalDays < 1) return "Today at " + localTimestamp.ToString("h:mm tt");
        if (diff.TotalDays < 2) return "Yesterday at " + localTimestamp.ToString("h:mm tt");
        if (diff.TotalDays < 7) return localTimestamp.DayOfWeek + " at " + localTimestamp.ToString("h:mm tt");
        return localTimestamp.Year == now.Year ? localTimestamp.ToString("MMM d") : localTimestamp.ToString("MMM d, yyyy");
    }
}
