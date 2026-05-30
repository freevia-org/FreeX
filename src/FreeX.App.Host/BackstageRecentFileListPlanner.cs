using System.Globalization;

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
        OpenAutomationName = IsPinned
            ? UiText.Format("Backstage_Recent_OpenPinnedFileAutomationName", FileName)
            : UiText.Format("Backstage_Recent_OpenRecentFileAutomationName", FileName);
        OpenAutomationHelpText = UiText.Format("Backstage_Recent_OpenAutomationHelpText", Path);
        PinAutomationName = IsPinned
            ? UiText.Format("Backstage_Recent_UnpinAutomationName", FileName)
            : UiText.Format("Backstage_Recent_PinAutomationName", FileName);
        PinAutomationHelpText = IsPinned
            ? UiText.Get("Backstage_Recent_UnpinHelpText")
            : UiText.Get("Backstage_Recent_PinHelpText");
        RemoveAutomationName = UiText.Format("Backstage_Recent_RemoveAutomationName", FileName);
        RemoveAutomationHelpText = UiText.Get("Backstage_Recent_RemoveAutomationHelpText");
    }

    private static string FormatDate(DateTimeOffset timestamp)
    {
        var localTimestamp = timestamp.ToLocalTime();
        var now = DateTimeOffset.Now;
        var diff = now - localTimestamp;
        if (diff.TotalHours < 1)
            return UiText.Get("Backstage_Recent_LastOpenedJustNow");

        var time = localTimestamp.ToString(UiText.Get("Backstage_Recent_LastOpenedTimeFormat"), CultureInfo.CurrentCulture);
        if (diff.TotalDays < 1)
            return UiText.Format("Backstage_Recent_LastOpenedTodayAt", time);

        if (diff.TotalDays < 2)
            return UiText.Format("Backstage_Recent_LastOpenedYesterdayAt", time);

        if (diff.TotalDays < 7)
        {
            var dayName = localTimestamp.ToString("dddd", CultureInfo.CurrentCulture);
            return UiText.Format("Backstage_Recent_LastOpenedWeekdayAt", dayName, time);
        }

        var formatKey = localTimestamp.Year == now.Year
            ? "Backstage_Recent_LastOpenedDateFormat"
            : "Backstage_Recent_LastOpenedDateWithYearFormat";
        return localTimestamp.ToString(UiText.Get(formatKey), CultureInfo.CurrentCulture);
    }
}
