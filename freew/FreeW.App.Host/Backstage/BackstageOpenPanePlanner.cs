using System.IO;
using Free.Shared.AppServices;
using Free.Shared.Shell.Wpf;

namespace FreeW.App.Host.Backstage;

internal static class BackstageOpenPanePlanner
{
    private const int MaxRecentDocuments = 8;
    private const int MaxRecentFolders = 8;

    public static IReadOnlyList<BackstageActionGroup> Build(
        IEnumerable<RecentFileEntry> recentEntries,
        Action<string> openRecent,
        Action browse,
        Action recoverUnsaved)
    {
        ArgumentNullException.ThrowIfNull(recentEntries);
        ArgumentNullException.ThrowIfNull(openRecent);
        ArgumentNullException.ThrowIfNull(browse);
        ArgumentNullException.ThrowIfNull(recoverUnsaved);

        var groups = new List<BackstageActionGroup>();
        var recentRows = recentEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .Take(MaxRecentDocuments)
            .Select(entry => new BackstageActionRow(
                FileNameOrPath(entry.Path),
                entry.IsPinned ? entry.Path + "  (pinned)" : entry.Path,
                () => openRecent(entry.Path)))
            .ToArray();

        if (recentRows.Length > 0)
            groups.Add(new BackstageActionGroup("Recent Documents", recentRows));

        groups.Add(new BackstageActionGroup("Places",
        [
            new("This PC", "Browse local folders and connected drives.", browse),
            new("Browse", "Open the Windows file picker.", browse),
        ]));

        groups.Add(new BackstageActionGroup("Recovery",
        [
            new("Recover Unsaved Documents", "Open the latest autosave recovery snapshot saved by FreeW.", recoverUnsaved),
        ]));

        return groups;
    }

    public static BackstageOpenPanePlan BuildPlan(
        IEnumerable<RecentFileEntry> recentEntries,
        string? filter,
        Action<string> openRecent,
        Action<string> openFolder,
        Action browse,
        Action recoverUnsaved)
    {
        ArgumentNullException.ThrowIfNull(recentEntries);
        ArgumentNullException.ThrowIfNull(openRecent);
        ArgumentNullException.ThrowIfNull(openFolder);
        ArgumentNullException.ThrowIfNull(browse);
        ArgumentNullException.ThrowIfNull(recoverUnsaved);

        var eligible = recentEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .ToArray();
        var matchingEntries = eligible
            .Where(entry => Matches(FileNameOrPath(entry.Path), entry.Path, filter))
            .ToArray();

        var documentRows = matchingEntries
            .Select(entry => new BackstageActionRow(
                FileNameOrPath(entry.Path),
                entry.IsPinned ? entry.Path + "  (pinned)" : entry.Path,
                () => openRecent(entry.Path)))
            .Take(MaxRecentDocuments)
            .ToArray();

        var folderRows = matchingEntries
            .Select(entry => Path.GetDirectoryName(entry.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new BackstageActionRow(
                FolderNameOrPath(path!),
                path!,
                () => openFolder(path!)))
            .Take(MaxRecentFolders)
            .ToArray();

        return new BackstageOpenPanePlan(
            documentRows,
            folderRows,
            [
                new("This PC", "Browse local folders and connected drives.", browse),
                new("Browse", "Open the Windows file picker.", browse),
            ],
            [
                new("Recover Unsaved Documents", "Open the latest autosave recovery snapshot saved by FreeW.", recoverUnsaved),
            ]);
    }

    private static string FileNameOrPath(string path)
    {
        var fileName = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
    }

    private static string FolderNameOrPath(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private static bool Matches(string label, string description, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return label.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            description.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record BackstageOpenPanePlan(
    IReadOnlyList<BackstageActionRow> DocumentRows,
    IReadOnlyList<BackstageActionRow> FolderRows,
    IReadOnlyList<BackstageActionRow> PlaceRows,
    IReadOnlyList<BackstageActionRow> RecoveryRows);
