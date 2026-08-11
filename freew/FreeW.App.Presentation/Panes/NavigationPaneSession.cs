using Free.Shared.AppServices;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.ContextMenus;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Panes;

public sealed record NavigationPaneMutationActions(
    Func<int, bool, int> MoveHeading,
    Action<int> PromoteHeading,
    Action<int> DemoteHeading,
    Action<int> CollapseHeading,
    Action<int> ExpandHeading,
    Func<int, bool> IsHeadingCollapsed);

public sealed record NavigationHeadingProjection(
    int BlockIndex,
    int Level,
    string Text);

public sealed record NavigationPaneViewState(
    string Query,
    IReadOnlyList<NavigationHeadingProjection> Headings,
    IReadOnlyList<int> SearchHits,
    int SearchHitIndex,
    int? SelectedHeadingBlockIndex,
    string SearchStatusText)
{
    public bool HasSearchQuery => Query.Length > 0;
    public bool CanStepSearch => SearchHits.Count > 0;
    public int? ActiveSearchBlockIndex =>
        SearchHitIndex >= 0 && SearchHitIndex < SearchHits.Count
            ? SearchHits[SearchHitIndex]
            : null;
}

public sealed record NavigationPaneText(
    string Title,
    string SearchDocument,
    string PreviousMatch,
    string NextMatch,
    string NoMatches,
    string MatchCountFormat);

public static class NavigationPaneTextCatalog
{
    private static readonly ResourceTextDescriptor[] Texts =
    [
        new("Navigation_Title", "Navigation"),
        new("Navigation_SearchDocument", "Search document"),
        new("Navigation_PreviousMatch", "Previous match"),
        new("Navigation_NextMatch", "Next match"),
        new("Navigation_NoMatches", "No matches"),
        new("Navigation_MatchCountFormat", "{0} of {1}"),
    ];

    public static IReadOnlyList<string> RequiredResourceKeys =>
        Texts.Select(text => text.ResourceKey).ToArray();

    public static NavigationPaneText Resolve(Func<string, string?>? getText = null) =>
        new(
            Texts[0].Resolve(getText),
            Texts[1].Resolve(getText),
            Texts[2].Resolve(getText),
            Texts[3].Resolve(getText),
            Texts[4].Resolve(getText),
            Texts[5].Resolve(getText));
}

public sealed record NavigationPaneOutcome(
    NavigationPaneViewState State,
    int? NavigateToBlockIndex = null,
    bool MutationApplied = false);

/// <summary>
/// Owns document-search, outline projection, selection, and outline-command transitions for both
/// FreeW renderers. Hosts retain model commit adapters, scrolling, focus, context-menu controls, and redraw.
/// </summary>
public sealed class NavigationPaneSession
{
    private readonly Func<TextDocument> _document;
    private readonly NavigationPaneMutationActions _mutations;
    private readonly NavigationPaneText _text;

    public NavigationPaneSession(
        Func<TextDocument> document,
        NavigationPaneMutationActions mutations,
        NavigationPaneText? text = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _text = text ?? NavigationPaneTextCatalog.Resolve();
        State = EmptyState();
    }

    public NavigationPaneViewState State { get; private set; }

    public NavigationPaneOutcome Refresh()
    {
        State = BuildState(
            _document(),
            State.Query,
            State.SearchHitIndex,
            State.SelectedHeadingBlockIndex);
        return new NavigationPaneOutcome(State);
    }

    public NavigationPaneOutcome SetQuery(string? query)
    {
        State = BuildState(_document(), query ?? string.Empty, searchHitIndex: 0, State.SelectedHeadingBlockIndex);
        return new NavigationPaneOutcome(State, State.ActiveSearchBlockIndex);
    }

    public NavigationPaneOutcome StepSearch(int direction)
    {
        if (direction == 0)
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (State.SearchHits.Count == 0)
            return new NavigationPaneOutcome(State);

        var next = State.SearchHitIndex < 0
            ? (direction < 0 ? State.SearchHits.Count - 1 : 0)
            : (State.SearchHitIndex + Math.Sign(direction) + State.SearchHits.Count) % State.SearchHits.Count;
        State = State with { SearchHitIndex = next };
        return new NavigationPaneOutcome(State, State.ActiveSearchBlockIndex);
    }

    public NavigationPaneOutcome SelectHeading(int blockIndex)
    {
        if (!State.Headings.Any(heading => heading.BlockIndex == blockIndex))
            return new NavigationPaneOutcome(State);

        State = State with { SelectedHeadingBlockIndex = blockIndex };
        return new NavigationPaneOutcome(State, blockIndex);
    }

    public RibbonMenu BuildOutlineMenu()
    {
        var document = _document();
        var blockIndex = State.SelectedHeadingBlockIndex ?? -1;
        return FreeWContextMenuPlanner.BuildOutline(
            document.Blocks,
            blockIndex,
            blockIndex >= 0 && _mutations.IsHeadingCollapsed(blockIndex));
    }

    public NavigationPaneOutcome ExecuteOutlineCommand(RibbonCommandId commandId)
    {
        if (State.SelectedHeadingBlockIndex is not { } blockIndex)
            return new NavigationPaneOutcome(State);

        var enabled = BuildOutlineMenu().Items.Any(item =>
            item.CommandId == commandId && item.IsEnabled);
        if (!enabled)
            return new NavigationPaneOutcome(State);

        var selectedBlockIndex = blockIndex;
        var applied = true;
        switch (commandId.Value)
        {
            case FreeWContextMenuPlanner.OutlineMoveUp:
                selectedBlockIndex = _mutations.MoveHeading(blockIndex, true);
                break;
            case FreeWContextMenuPlanner.OutlineMoveDown:
                selectedBlockIndex = _mutations.MoveHeading(blockIndex, false);
                break;
            case FreeWContextMenuPlanner.OutlinePromote:
                _mutations.PromoteHeading(blockIndex);
                break;
            case FreeWContextMenuPlanner.OutlineDemote:
                _mutations.DemoteHeading(blockIndex);
                break;
            case FreeWContextMenuPlanner.OutlineCollapse:
                _mutations.CollapseHeading(blockIndex);
                break;
            case FreeWContextMenuPlanner.OutlineExpand:
                _mutations.ExpandHeading(blockIndex);
                break;
            default:
                applied = false;
                break;
        }

        if (!applied)
            return new NavigationPaneOutcome(State);

        State = BuildState(_document(), State.Query, State.SearchHitIndex, selectedBlockIndex);
        var selected = State.Headings.Any(heading => heading.BlockIndex == selectedBlockIndex)
            ? selectedBlockIndex
            : (int?)null;
        State = State with { SelectedHeadingBlockIndex = selected };
        return new NavigationPaneOutcome(State, selected, MutationApplied: true);
    }

    public static IReadOnlyList<NavigationHeadingProjection> ProjectHeadings(
        TextDocument document,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(document);
        return BuildHeadings(document, query ?? string.Empty);
    }

    private NavigationPaneViewState EmptyState() =>
        new(string.Empty, [], [], -1, null, string.Empty);

    private NavigationPaneViewState BuildState(
        TextDocument document,
        string query,
        int searchHitIndex,
        int? selectedHeadingBlockIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalizedQuery = query;
        var hits = normalizedQuery.Length == 0 ? [] : FindSearchHits(document, normalizedQuery);
        var nextHitIndex = hits.Count == 0
            ? -1
            : Math.Clamp(searchHitIndex < 0 ? 0 : searchHitIndex, 0, hits.Count - 1);
        var headings = BuildHeadings(document, normalizedQuery);
        var selected = selectedHeadingBlockIndex is { } blockIndex
            && headings.Any(heading => heading.BlockIndex == blockIndex)
                ? blockIndex
                : (int?)null;
        var status = normalizedQuery.Length == 0
            ? string.Empty
            : hits.Count == 0
                ? _text.NoMatches
                : string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    _text.MatchCountFormat,
                    nextHitIndex + 1,
                    hits.Count);
        return new NavigationPaneViewState(normalizedQuery, headings, hits, nextHitIndex, selected, status);
    }

    private static IReadOnlyList<int> FindSearchHits(TextDocument document, string query)
    {
        var hits = new List<int>();
        for (var index = 0; index < document.Blocks.Count; index++)
        {
            if (BlockMatches(document.Blocks[index], query))
                hits.Add(index);
        }
        return hits;
    }

    private static IReadOnlyList<NavigationHeadingProjection> BuildHeadings(
        TextDocument document,
        string query)
    {
        var outline = DocumentOutline.Of(document);
        var projected = new List<NavigationHeadingProjection>(outline.Count);
        foreach (var entry in outline)
        {
            if (query.Length > 0)
            {
                var (start, end) = OutlineTools.SubtreeRange(document.Blocks, entry.BlockIndex);
                var matched = false;
                for (var index = start; index < end && !matched; index++)
                    matched = BlockMatches(document.Blocks[index], query);
                if (!matched)
                    continue;
            }

            projected.Add(new NavigationHeadingProjection(entry.BlockIndex, entry.Level, entry.Text));
        }
        return projected;
    }

    private static bool BlockMatches(Block block, string query)
    {
        var text = block switch
        {
            Paragraph paragraph => paragraph.PlainText,
            Table table => string.Join(
                " ",
                table.Rows.SelectMany(row => row.Cells).Select(cell => cell.PlainText)),
            _ => string.Empty,
        };
        return TextSearch.FindAll(text, query, matchCase: false, wholeWord: false).Any();
    }
}
