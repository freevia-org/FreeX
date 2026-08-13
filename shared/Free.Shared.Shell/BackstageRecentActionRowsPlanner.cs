using System.IO;
using Free.Shared.AppServices;
using Free.Shared.IO;

namespace Free.Shared.Shell;

public sealed record BackstageRecentActionRowText(string PinnedDescriptionSuffix);

/// <summary>
/// Builds portable Backstage action rows from recent-file metadata while apps keep their own headings and commands.
/// </summary>
public static class BackstageRecentActionRowsPlanner
{
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

        var seen = new HashSet<string>(PlatformPathIdentityComparer.Current);
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

            var path = Path.GetDirectoryName(entry.Path);
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
        => FilePathPolicy.FileNameOrPath(path);

    public static string FolderNameOrPath(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return FilePathPolicy.TryGetFileName(trimmed, out var name) ? name : path;
    }

    private static bool Matches(string label, string description, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return label.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            description.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
