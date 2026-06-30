using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace FreeW.App.Presentation.Backstage;

public static class BackstageHomePanePlanner
{
    private const int MaxRecentDocuments = 6;
    private static readonly BackstageRecentActionRowText RecentText = new(BackstageViewTextResources.PinnedRecentSuffix);

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

        var recentRows = BackstageRecentActionRowsPlanner.BuildDocumentRows(
            recentEntries,
            MaxRecentDocuments,
            RecentText,
            openRecent);

        if (recentRows.Count > 0)
            groups.Add(new BackstageActionGroup("Recent Documents", recentRows));

        groups.Add(new BackstageActionGroup("Open",
        [
            new("Browse", "Open the Windows file picker.", browse),
            new("Open More Documents", "Show recent search, folders, and recovery options.", openMore),
        ]));

        return groups;
    }
}
