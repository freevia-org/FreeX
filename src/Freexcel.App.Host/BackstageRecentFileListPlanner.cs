namespace Freexcel.App.Host;

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
        var normalizedFilter = string.IsNullOrWhiteSpace(filter)
            ? null
            : filter.Trim();

        var allItems = entries
            .Where(entry => pathExists(entry.Path))
            .Select(entry => new RecentFileViewModel(entry))
            .Where(item => MatchesFilter(item, normalizedFilter))
            .ToList();

        return new BackstageRecentFileListPlan(
            allItems,
            allItems.Where(item => !item.IsPinned).ToList(),
            allItems.Where(item => item.IsPinned).ToList());
    }

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

    public RecentFileViewModel(RecentFileEntry entry)
    {
        Path = entry.Path;
        FileName = System.IO.Path.GetFileName(entry.Path);
        Directory = System.IO.Path.GetDirectoryName(entry.Path) ?? "";
        LastOpenedText = FormatDate(entry.LastOpened);
        IsPinned = entry.IsPinned;
    }

    private static string FormatDate(DateTime dt)
    {
        var diff = DateTime.Now - dt;
        if (diff.TotalHours < 1) return "Just now";
        if (diff.TotalDays < 1) return "Today at " + dt.ToString("h:mm tt");
        if (diff.TotalDays < 2) return "Yesterday at " + dt.ToString("h:mm tt");
        if (diff.TotalDays < 7) return dt.DayOfWeek + " at " + dt.ToString("h:mm tt");
        return dt.Year == DateTime.Now.Year ? dt.ToString("MMM d") : dt.ToString("MMM d, yyyy");
    }
}
