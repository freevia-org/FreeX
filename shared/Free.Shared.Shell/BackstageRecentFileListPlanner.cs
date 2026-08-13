using System.Globalization;
using Free.Shared.AppServices;
using Free.Shared.IO;

namespace Free.Shared.Shell;

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

    public static IReadOnlyList<RecentFileViewModel> SelectPinnedFirst(
        BackstageRecentFileListPlan plan,
        int maximumCount)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (maximumCount < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        if (maximumCount == 0)
            return [];

        var items = new List<RecentFileViewModel>(Math.Min(maximumCount, plan.AllItems.Count));
        AddUpToMaximum(items, plan.PinnedItems, maximumCount);
        AddUpToMaximum(items, plan.RecentItems, maximumCount);
        return items;
    }

    private static void AddUpToMaximum(
        List<RecentFileViewModel> target,
        IReadOnlyList<RecentFileViewModel> source,
        int maximumCount)
    {
        for (var index = 0; index < source.Count && target.Count < maximumCount; index++)
            target.Add(source[index]);
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
    public WorkbookFileAccessIdentity? FileAccessIdentity { get; }

    public RecentFileViewModel(RecentFileEntry entry)
    {
        Path = entry.Path;
        FileAccessIdentity = entry.FileAccessIdentity;
        FileName = FilePathPolicy.FileNameOrPath(entry.Path);
        Directory = System.IO.Path.GetDirectoryName(entry.Path) ?? "";
        LastOpenedText = FormatDate(entry.LastOpened);
        IsPinned = entry.IsPinned;
        OpenAutomationName = IsPinned
            ? BackstageStrings.Current.Format("Backstage_Recent_OpenPinnedFileAutomationName", FileName)
            : BackstageStrings.Current.Format("Backstage_Recent_OpenRecentFileAutomationName", FileName);
        OpenAutomationHelpText = BackstageStrings.Current.Format("Backstage_Recent_OpenAutomationHelpText", Path);
        PinAutomationName = IsPinned
            ? BackstageStrings.Current.Format("Backstage_Recent_UnpinAutomationName", FileName)
            : BackstageStrings.Current.Format("Backstage_Recent_PinAutomationName", FileName);
        PinAutomationHelpText = IsPinned
            ? BackstageStrings.Current.Get("Backstage_Recent_UnpinHelpText")
            : BackstageStrings.Current.Get("Backstage_Recent_PinHelpText");
        RemoveAutomationName = BackstageStrings.Current.Format("Backstage_Recent_RemoveAutomationName", FileName);
        RemoveAutomationHelpText = BackstageStrings.Current.Get("Backstage_Recent_RemoveAutomationHelpText");
    }

    private static string FormatDate(DateTimeOffset timestamp)
    {
        var localTimestamp = timestamp.ToLocalTime();
        var now = DateTimeOffset.Now;
        var diff = now - localTimestamp;
        if (diff.TotalHours < 1)
            return BackstageStrings.Current.Get("Backstage_Recent_LastOpenedJustNow");

        var time = localTimestamp.ToString(BackstageStrings.Current.Get("Backstage_Recent_LastOpenedTimeFormat"), CultureInfo.CurrentCulture);
        if (diff.TotalDays < 1)
            return BackstageStrings.Current.Format("Backstage_Recent_LastOpenedTodayAt", time);

        if (diff.TotalDays < 2)
            return BackstageStrings.Current.Format("Backstage_Recent_LastOpenedYesterdayAt", time);

        if (diff.TotalDays < 7)
        {
            var dayName = localTimestamp.ToString("dddd", CultureInfo.CurrentCulture);
            return BackstageStrings.Current.Format("Backstage_Recent_LastOpenedWeekdayAt", dayName, time);
        }

        var formatKey = localTimestamp.Year == now.Year
            ? "Backstage_Recent_LastOpenedDateFormat"
            : "Backstage_Recent_LastOpenedDateWithYearFormat";
        return localTimestamp.ToString(BackstageStrings.Current.Get(formatKey), CultureInfo.CurrentCulture);
    }
}
