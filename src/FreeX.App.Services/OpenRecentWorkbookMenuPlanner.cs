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
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(canOpenWorkbook);

        if (maximumItems < 1)
            return new OpenRecentWorkbookMenuPlan([]);

        var items = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .Where(entry => fileExists(entry.Path) && canOpenWorkbook(entry.Path))
            .OrderByDescending(entry => entry.LastOpened)
            .Take(maximumItems)
            .Select(entry => new OpenRecentWorkbookMenuItemPlan(
                entry.Path,
                FormatHeader(entry.Path),
                entry.LastOpened))
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
