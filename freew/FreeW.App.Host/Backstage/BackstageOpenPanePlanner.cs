using System.IO;
using Free.Shared.AppServices;
using Free.Shared.Shell.Wpf;

namespace FreeW.App.Host.Backstage;

internal static class BackstageOpenPanePlanner
{
    private const int MaxRecentDocuments = 8;

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

    private static string FileNameOrPath(string path)
    {
        var fileName = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
    }
}
