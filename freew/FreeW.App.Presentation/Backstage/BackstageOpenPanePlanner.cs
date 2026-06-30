using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace FreeW.App.Presentation.Backstage;

public static class BackstageOpenPanePlanner
{
    private const int MaxRecentDocuments = 8;
    private const int MaxRecentFolders = 8;
    private static readonly BackstageRecentActionRowText RecentText = new(BackstageViewTextResources.PinnedRecentSuffix);

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
        var recentRows = BackstageRecentActionRowsPlanner.BuildDocumentRows(
            recentEntries,
            MaxRecentDocuments,
            RecentText,
            openRecent);

        if (recentRows.Count > 0)
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

        var documentRows = BackstageRecentActionRowsPlanner.BuildDocumentRows(
            recentEntries,
            MaxRecentDocuments,
            RecentText,
            openRecent,
            filter);
        var folderRows = BackstageRecentActionRowsPlanner.BuildFolderRows(
            recentEntries,
            MaxRecentFolders,
            openFolder,
            filter);

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
}

public sealed record BackstageOpenPanePlan(
    IReadOnlyList<BackstageActionRow> DocumentRows,
    IReadOnlyList<BackstageActionRow> FolderRows,
    IReadOnlyList<BackstageActionRow> PlaceRows,
    IReadOnlyList<BackstageActionRow> RecoveryRows);
