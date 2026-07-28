using Free.Shared.AppServices;

namespace Free.Shared.Shell;

public sealed record BackstageRecentActionRowText(string PinnedDescriptionSuffix);

/// <summary>
/// Builds portable Backstage action rows from recent-file metadata while apps keep their own headings and commands.
/// </summary>
public static class BackstageRecentActionRowsPlanner
{
    private static readonly char[] DirectorySeparators = ['/', '\\'];

    public static IReadOnlyList<BackstageActionRow> BuildDocumentRows(
        IEnumerable<RecentFileEntry> entries,
        int maxRows,
        BackstageRecentActionRowText text,
        Action<string> openPath,
        string? filter = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(openPath);

        var rows = new List<BackstageActionRow>();
        foreach (var entry in entries)
        {
            if (rows.Count >= maxRows)
                break;

            if (string.IsNullOrWhiteSpace(entry.Path))
                continue;

            var path = entry.Path;
            var label = FileNameOrPath(path);
            if (!Matches(label, path, filter))
                continue;

            rows.Add(new BackstageActionRow(
                label,
                entry.IsPinned ? path + text.PinnedDescriptionSuffix : path,
                () => openPath(path)));
        }

        return rows;
    }

    public static IReadOnlyList<BackstageActionRow> BuildFolderRows(
        IEnumerable<RecentFileEntry> entries,
        int maxRows,
        Action<string> openFolder,
        string? filter = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(openFolder);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<BackstageActionRow>();
        foreach (var entry in entries)
        {
            if (rows.Count >= maxRows)
                break;

            if (string.IsNullOrWhiteSpace(entry.Path))
                continue;

            var documentLabel = FileNameOrPath(entry.Path);
            if (!Matches(documentLabel, entry.Path, filter))
                continue;

            var path = DirectoryNameOrNull(entry.Path);
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
                continue;

            rows.Add(new BackstageActionRow(
                FolderNameOrPath(path),
                path,
                () => openFolder(path)));
        }

        return rows;
    }

    public static string FileNameOrPath(string path)
    {
        var trimmed = TrimTrailingDirectorySeparators(path);
        var separatorIndex = trimmed.LastIndexOfAny(DirectorySeparators);
        var fileName = separatorIndex >= 0 ? trimmed[(separatorIndex + 1)..] : trimmed;
        return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
    }

    public static string FolderNameOrPath(string path)
    {
        var trimmed = TrimTrailingDirectorySeparators(path);
        var separatorIndex = trimmed.LastIndexOfAny(DirectorySeparators);
        var name = separatorIndex >= 0 ? trimmed[(separatorIndex + 1)..] : trimmed;
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private static string? DirectoryNameOrNull(string path)
    {
        var trimmed = TrimTrailingDirectorySeparators(path);
        var separatorIndex = trimmed.LastIndexOfAny(DirectorySeparators);
        if (separatorIndex < 0)
            return null;

        if (separatorIndex == 0)
            return trimmed[..1];

        if (separatorIndex == 2 && trimmed.Length > 2 && trimmed[1] == ':')
            return trimmed[..(separatorIndex + 1)];

        return trimmed[..separatorIndex];
    }

    private static string TrimTrailingDirectorySeparators(string path) =>
        path.TrimEnd(DirectorySeparators);

    private static bool Matches(string label, string description, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return label.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            description.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
