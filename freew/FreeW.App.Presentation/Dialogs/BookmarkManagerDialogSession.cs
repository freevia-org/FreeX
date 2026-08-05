using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record BookmarkManagerItem(string Name, int BlockIndex)
{
    public override string ToString() => Name;
}

public sealed record BookmarkManagerDialogState(
    IReadOnlyList<BookmarkManagerItem> Items,
    int SelectedIndex,
    string? SelectedName,
    string StatusText,
    bool CanGoTo,
    bool CanDelete);

public sealed record BookmarkManagerDeleteRefreshPlan(string Name);

public sealed record BookmarkManagerGoToIntent(string Name, int BlockIndex);

/// <summary>
/// Owns the renderer-neutral state and action planning for the paired bookmark manager dialogs.
/// Renderers retain document synchronization, native editing commands, focus, and window lifecycle.
/// </summary>
public sealed class BookmarkManagerDialogSession
{
    private const string EmptyStatusText = "This document has no bookmarks.";

    public BookmarkManagerDialogState State { get; private set; } = new(
        [],
        SelectedIndex: -1,
        SelectedName: null,
        StatusText: string.Empty,
        CanGoTo: false,
        CanDelete: false);

    public BookmarkManagerDialogState Refresh(IEnumerable<BookmarkLocation> locations) =>
        Project(locations, State.SelectedName, statusText: null);

    public BookmarkManagerDialogState SelectIndex(int index)
    {
        var selectedIndex = index >= 0 && index < State.Items.Count ? index : -1;
        var selectedName = selectedIndex >= 0 ? State.Items[selectedIndex].Name : null;
        var hasSelection = selectedIndex >= 0;

        State = State with
        {
            SelectedIndex = selectedIndex,
            SelectedName = selectedName,
            CanGoTo = hasSelection,
            CanDelete = hasSelection,
        };
        return State;
    }

    public BookmarkManagerGoToIntent? PlanGoTo()
    {
        if (State.SelectedIndex < 0 || State.SelectedIndex >= State.Items.Count)
            return null;

        var item = State.Items[State.SelectedIndex];
        return new BookmarkManagerGoToIntent(item.Name, item.BlockIndex);
    }

    public BookmarkManagerDeleteRefreshPlan? PlanDelete()
    {
        if (State.SelectedIndex < 0 || State.SelectedIndex >= State.Items.Count)
            return null;

        return new BookmarkManagerDeleteRefreshPlan(State.Items[State.SelectedIndex].Name);
    }

    public BookmarkManagerDialogState CompleteDelete(
        BookmarkManagerDeleteRefreshPlan plan,
        IEnumerable<BookmarkLocation> locations)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Project(locations, plan.Name, $"Removed bookmark \"{plan.Name}\".");
    }

    private BookmarkManagerDialogState Project(
        IEnumerable<BookmarkLocation> locations,
        string? preferredName,
        string? statusText)
    {
        ArgumentNullException.ThrowIfNull(locations);

        var items = locations
            .Select(location => new BookmarkManagerItem(location.Name, location.BlockIndex))
            .ToArray();
        var selectedIndex = FindSelectedIndex(items, preferredName);
        var hasSelection = selectedIndex >= 0;

        State = new BookmarkManagerDialogState(
            items,
            selectedIndex,
            hasSelection ? items[selectedIndex].Name : null,
            statusText ?? (items.Length == 0 ? EmptyStatusText : string.Empty),
            CanGoTo: hasSelection,
            CanDelete: hasSelection);
        return State;
    }

    private static int FindSelectedIndex(IReadOnlyList<BookmarkManagerItem> items, string? preferredName)
    {
        if (items.Count == 0)
            return -1;

        if (preferredName is not null)
        {
            for (var index = 0; index < items.Count; index++)
            {
                if (string.Equals(items[index].Name, preferredName, StringComparison.Ordinal))
                    return index;
            }
        }

        return 0;
    }
}
