namespace FreeX.App.Services;

public sealed record OpenRecentWorkbookMenuItemPlan(
    string Path,
    string Header,
    DateTimeOffset LastOpened);

public sealed record OpenRecentWorkbookMenuPlan(IReadOnlyList<OpenRecentWorkbookMenuItemPlan> Items)
{
    public int ItemCount => Items.Count;
}

public static class OpenRecentWorkbookMenuPlanner
{
    public const int DefaultMaximumItems = 10;

    public static OpenRecentWorkbookMenuPlan Create(
        IEnumerable<RecentFileEntry> entries,
        Func<string, bool> fileExists,
        Func<string, bool> canOpenWorkbook,
        int maximumItems = DefaultMaximumItems)
    {
        ArgumentNullException.ThrowIfNull(canOpenWorkbook);

        return Create(
            entries,
            fileExists,
            path => canOpenWorkbook(path) ? path : null,
            maximumItems);
    }

    public static OpenRecentWorkbookMenuPlan Create(
        IEnumerable<RecentFileEntry> entries,
        Func<string, bool> fileExists,
        Func<string, string?> resolveOpenWorkbookPath,
        int maximumItems = DefaultMaximumItems)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(resolveOpenWorkbookPath);

        if (maximumItems < 1)
            return new OpenRecentWorkbookMenuPlan([]);

        var seenPaths = new HashSet<string>(PlatformPathIdentityComparer.Current);
        var items = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .OrderByDescending(entry => entry.LastOpened)
            .Select(entry => (Entry: entry, Path: resolveOpenWorkbookPath(entry.Path)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Path) && fileExists(item.Path))
            .Where(item => seenPaths.Add(item.Path!))
            .Take(maximumItems)
            .Select(item => new OpenRecentWorkbookMenuItemPlan(
                item.Path!,
                FormatHeader(item.Path!),
                item.Entry.LastOpened))
            .ToList();

        return new OpenRecentWorkbookMenuPlan(items);
    }

    public static string FormatHeader(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
            return path;

        var directory = Path.GetDirectoryName(path);
        return string.IsNullOrWhiteSpace(directory)
            ? fileName
            : $"{fileName} - {directory}";
    }
}
