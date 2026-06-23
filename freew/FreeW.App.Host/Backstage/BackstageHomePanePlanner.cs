using System.IO;
using Free.Shared.AppServices;
using Free.Shared.Shell.Wpf;

namespace FreeW.App.Host.Backstage;

internal static class BackstageHomePanePlanner
{
    private const int MaxRecentDocuments = 6;

    public static IReadOnlyList<BackstageActionGroup> Build(
        IEnumerable<RecentFileEntry> recentEntries,
        Action newDocument,
        Action<string> openRecent,
        Action browse,
        Action openMore)
    {
        ArgumentNullException.ThrowIfNull(recentEntries);
        ArgumentNullException.ThrowIfNull(newDocument);
        ArgumentNullException.ThrowIfNull(openRecent);
        ArgumentNullException.ThrowIfNull(browse);
        ArgumentNullException.ThrowIfNull(openMore);

        var groups = new List<BackstageActionGroup>
        {
            new("New",
            [
                new("Blank document", "Create a new document.", newDocument),
            ]),
        };

        var recentRows = recentEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .Select(entry => new BackstageActionRow(
                FileNameOrPath(entry.Path),
                entry.IsPinned ? entry.Path + "  (pinned)" : entry.Path,
                () => openRecent(entry.Path)))
            .Take(MaxRecentDocuments)
            .ToArray();

        if (recentRows.Length > 0)
            groups.Add(new BackstageActionGroup("Recent Documents", recentRows));

        groups.Add(new BackstageActionGroup("Open",
        [
            new("Browse", "Open the Windows file picker.", browse),
            new("Open More Documents", "Show recent search, folders, and recovery options.", openMore),
        ]));

        return groups;
    }

    private static string FileNameOrPath(string path)
    {
        var fileName = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
    }
}
