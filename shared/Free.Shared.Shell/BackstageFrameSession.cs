namespace Free.Shared.Shell;

/// <summary>
/// Language-neutral identity for a Backstage entry. Stable ids take precedence over automation ids;
/// localized labels are the final compatibility lookup key.
/// </summary>
public sealed record BackstageFrameEntryIdentity(
    string Label,
    string? StableId,
    string? AutomationId)
{
    public string PreferredId =>
        NonBlank(StableId)
        ?? NonBlank(AutomationId)
        ?? Label;

    public static BackstageFrameEntryIdentity From<TContent>(
        SisterBackstageEntryPlan<TContent> entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new BackstageFrameEntryIdentity(entry.Label, entry.StableId, entry.AutomationId);
    }

    public string ResolveAutomationId(string fallbackPrefix = "BackstageNav_")
    {
        ArgumentNullException.ThrowIfNull(fallbackPrefix);
        return NonBlank(AutomationId)
            ?? fallbackPrefix + AutomationIdToken.KeepLettersAndDigits(Label);
    }

    private static string? NonBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
/// Portable result of activating a Backstage entry. Renderers apply selection/content or dismissal,
/// then invoke <see cref="Command"/> after a dismissing command has left the visual tree.
/// </summary>
public sealed record BackstageFrameActivation<TContent>(
    SisterBackstageEntryPlan<TContent> Entry,
    BackstageFrameEntryIdentity Identity,
    TContent? PaneContent,
    Action? Command,
    bool DismissFrame)
{
    public bool IsPane => Entry.Kind == SisterBackstageEntryKind.Pane;

    public void Dispatch(
        Action<TContent> presentPane,
        Action dismissFrame)
    {
        ArgumentNullException.ThrowIfNull(presentPane);
        ArgumentNullException.ThrowIfNull(dismissFrame);

        if (IsPane)
        {
            presentPane(PaneContent!);
            return;
        }

        if (DismissFrame)
            dismissFrame();
        (Command ?? throw new InvalidOperationException(
            $"Command '{Entry.Label}' has no action."))();
    }
}

/// <summary>
/// Owns renderer-neutral Backstage identity, lookup, selection, activation, and open/close semantics.
/// Native frames retain only control realization, availability checks, focus, and visual state updates.
/// </summary>
public sealed class BackstageFrameSession<TContent>
{
    private IReadOnlyList<SisterBackstageEntryPlan<TContent>> _entries = [];

    public IReadOnlyList<SisterBackstageEntryPlan<TContent>> Entries => _entries;

    public bool IsOpen { get; private set; }

    public string? CurrentEntryId { get; private set; }

    public string? CurrentPaneLabel { get; private set; }

    public void SetEntries(IEnumerable<SisterBackstageEntryPlan<TContent>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _entries = entries.ToArray();
        IsOpen = false;
        CurrentEntryId = null;
        CurrentPaneLabel = null;
    }

    public BackstageFrameActivation<TContent>? Show(string? paneIdOrLabel = null)
    {
        IsOpen = true;

        var entry = string.IsNullOrWhiteSpace(paneIdOrLabel)
            ? _entries.FirstOrDefault(candidate =>
                candidate.Kind == SisterBackstageEntryKind.Pane)
            : FindEntry(paneIdOrLabel!);
        return entry?.Kind == SisterBackstageEntryKind.Pane
            ? Activate(entry)
            : null;
    }

    public bool Hide()
    {
        if (!IsOpen)
            return false;

        IsOpen = false;
        return true;
    }

    public SisterBackstageEntryPlan<TContent>? FindEntry(string idOrLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrLabel);

        return _entries.FirstOrDefault(entry =>
                   string.Equals(entry.StableId, idOrLabel, StringComparison.Ordinal))
            ?? _entries.FirstOrDefault(entry =>
                   string.Equals(entry.AutomationId, idOrLabel, StringComparison.Ordinal))
            ?? _entries.FirstOrDefault(entry =>
                   string.Equals(entry.Label, idOrLabel, StringComparison.OrdinalIgnoreCase));
    }

    public BackstageFrameActivation<TContent>? TryActivate(string idOrLabel)
    {
        var entry = FindEntry(idOrLabel);
        return entry is null || entry.Kind == SisterBackstageEntryKind.Divider
            ? null
            : Activate(entry);
    }

    public BackstageFrameActivation<TContent> Activate(
        SisterBackstageEntryPlan<TContent> entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var identity = BackstageFrameEntryIdentity.From(entry);
        return entry.Kind switch
        {
            SisterBackstageEntryKind.Pane => ActivatePane(entry, identity),
            SisterBackstageEntryKind.Command => ActivateCommand(entry, identity),
            SisterBackstageEntryKind.Divider => throw new InvalidOperationException(
                "Backstage dividers cannot be activated."),
            _ => throw new ArgumentOutOfRangeException(nameof(entry), entry.Kind, null),
        };
    }

    private BackstageFrameActivation<TContent> ActivatePane(
        SisterBackstageEntryPlan<TContent> entry,
        BackstageFrameEntryIdentity identity)
    {
        var contentFactory = entry.ContentFactory
            ?? throw new InvalidOperationException($"Pane '{entry.Label}' has no content factory.");
        var paneContent = contentFactory();
        if (paneContent is null)
            throw new InvalidOperationException($"Pane '{entry.Label}' produced no content.");

        CurrentEntryId = identity.PreferredId;
        CurrentPaneLabel = entry.Label;
        return new BackstageFrameActivation<TContent>(
            entry,
            identity,
            paneContent,
            null,
            DismissFrame: false);
    }

    private BackstageFrameActivation<TContent> ActivateCommand(
        SisterBackstageEntryPlan<TContent> entry,
        BackstageFrameEntryIdentity identity)
    {
        var command = entry.Action
            ?? throw new InvalidOperationException($"Command '{entry.Label}' has no action.");

        if (entry.DismissOnActivate)
            IsOpen = false;

        return new BackstageFrameActivation<TContent>(
            entry,
            identity,
            default,
            command,
            entry.DismissOnActivate);
    }
}
