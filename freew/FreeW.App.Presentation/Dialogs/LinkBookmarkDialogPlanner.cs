namespace FreeW.App.Presentation.Dialogs;

public sealed record LinkBookmarkDialogPresentation(
    string Title,
    string BookmarkLabel,
    string AcceptLabel,
    string CancelLabel,
    string EmptyMessage,
    string EmptyTitle,
    IReadOnlyList<string> BookmarkNames,
    int SelectedIndex)
{
    public bool IsEmpty => BookmarkNames.Count == 0;
}

/// <summary>Owns the shared Link-to-Bookmark choices, empty state, and acceptance policy.</summary>
public static class LinkBookmarkDialogPlanner
{
    public static LinkBookmarkDialogPresentation Build(IEnumerable<string>? bookmarkNames)
    {
        var text = InsertDialogTextResources.LinkBookmark;
        var names = (bookmarkNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new LinkBookmarkDialogPresentation(
            text.Title,
            text.BookmarkLabel,
            InsertDialogTextResources.OkButton,
            InsertDialogTextResources.CancelButton,
            text.EmptyMessage,
            text.EmptyTitle,
            names,
            names.Length == 0 ? -1 : 0);
    }

    public static string? PlanAcceptance(LinkBookmarkDialogPresentation presentation, int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        return selectedIndex >= 0 && selectedIndex < presentation.BookmarkNames.Count
            ? presentation.BookmarkNames[selectedIndex]
            : null;
    }
}
