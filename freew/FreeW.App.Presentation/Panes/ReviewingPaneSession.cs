using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Panes;

public sealed record ReviewingPaneMutationActions(
    Func<RevisionEntry, bool> Accept,
    Func<RevisionEntry, bool> Reject,
    Func<bool> AcceptAll,
    Func<bool> RejectAll);

public sealed record ReviewingPaneViewState(
    IReadOnlyList<RevisionEntry> Entries,
    int SelectedIndex,
    ReviewRevisionSortOrder SortOrder)
{
    public bool HasRevisions => Entries.Count > 0;
    public bool CanResolveSelected => SelectedIndex >= 0 && SelectedIndex < Entries.Count;
    public RevisionEntry? SelectedRevision => CanResolveSelected ? Entries[SelectedIndex] : null;
}

public sealed record ReviewingPaneOutcome(
    ReviewingPaneViewState State,
    RevisionEntry? NavigateToRevision = null,
    bool MutationApplied = false);

/// <summary>
/// Owns reviewing-pane ordering, selection, wrapping navigation, enablement, and mutation targeting.
/// Renderers retain native list controls, caret navigation, dirty tracking, focus, and redraw.
/// </summary>
public sealed class ReviewingPaneSession
{
    private readonly Func<IReadOnlyList<RevisionEntry>> _enumerate;
    private readonly ReviewingPaneMutationActions _mutations;

    public ReviewingPaneSession(
        Func<IReadOnlyList<RevisionEntry>> enumerate,
        ReviewingPaneMutationActions mutations)
    {
        _enumerate = enumerate ?? throw new ArgumentNullException(nameof(enumerate));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        State = new ReviewingPaneViewState([], -1, ReviewRevisionSortOrder.Sequence);
    }

    public ReviewingPaneViewState State { get; private set; }

    public ReviewingPaneOutcome Refresh()
    {
        var entries = ReviewRevisionSortPlanner.Sort(_enumerate(), State.SortOrder);
        var refresh = ReviewingPaneStatePlanner.BuildRefreshState(entries.Count, State.SelectedIndex);
        State = new ReviewingPaneViewState(entries, refresh.SelectedIndex, State.SortOrder);
        return new ReviewingPaneOutcome(State);
    }

    public ReviewingPaneOutcome SetSortOrder(ReviewRevisionSortOrder order)
    {
        State = State with { SortOrder = order };
        return Refresh();
    }

    public ReviewingPaneOutcome SelectIndex(int index)
    {
        var selectedIndex = index >= 0 && index < State.Entries.Count ? index : -1;
        State = State with { SelectedIndex = selectedIndex };
        return new ReviewingPaneOutcome(State, State.SelectedRevision);
    }

    public ReviewingPaneOutcome Step(int direction, bool refresh = true)
    {
        if (direction == 0)
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (refresh)
            Refresh();
        if (State.Entries.Count == 0)
            return new ReviewingPaneOutcome(State);

        var next = ReviewingPaneStatePlanner.ResolveStep(
            State.Entries.Count,
            State.SelectedIndex,
            direction);
        return SelectIndex(next);
    }

    public ReviewingPaneOutcome AcceptSelected() => ResolveSelected(_mutations.Accept);

    public ReviewingPaneOutcome RejectSelected() => ResolveSelected(_mutations.Reject);

    public ReviewingPaneOutcome Accept(RevisionEntry entry) => Resolve(entry, _mutations.Accept);

    public ReviewingPaneOutcome Reject(RevisionEntry entry) => Resolve(entry, _mutations.Reject);

    public ReviewingPaneOutcome AcceptAll() => ResolveAll(_mutations.AcceptAll);

    public ReviewingPaneOutcome RejectAll() => ResolveAll(_mutations.RejectAll);

    public static IReadOnlyList<RevisionEntry> Enumerate(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return RevisionList.Enumerate(document);
    }

    public static bool Accept(TextDocument document, RevisionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entry);
        return RevisionList.Accept(document, entry);
    }

    public static bool Reject(TextDocument document, RevisionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entry);
        return RevisionList.Reject(document, entry);
    }

    private ReviewingPaneOutcome ResolveSelected(Func<RevisionEntry, bool> resolve) =>
        State.SelectedRevision is { } entry
            ? Resolve(entry, resolve)
            : new ReviewingPaneOutcome(State);

    private ReviewingPaneOutcome Resolve(
        RevisionEntry entry,
        Func<RevisionEntry, bool> resolve)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var index = State.Entries.IndexOf(entry);
        if (index < 0)
            return new ReviewingPaneOutcome(State);

        State = State with { SelectedIndex = index };
        var applied = resolve(entry);
        if (!applied)
            return new ReviewingPaneOutcome(State);

        Refresh();
        return new ReviewingPaneOutcome(State, State.SelectedRevision, MutationApplied: true);
    }

    private ReviewingPaneOutcome ResolveAll(Func<bool> resolveAll)
    {
        if (!State.HasRevisions || !resolveAll())
            return new ReviewingPaneOutcome(State);

        Refresh();
        return new ReviewingPaneOutcome(State, State.SelectedRevision, MutationApplied: true);
    }
}

internal static class ReviewingPaneEntryExtensions
{
    public static int IndexOf(this IReadOnlyList<RevisionEntry> entries, RevisionEntry entry)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index] == entry)
                return index;
        }
        return -1;
    }
}
