namespace Free.Shared.Ribbon;

/// <summary>Payload of <see cref="IRibbonStateStore.StateChanged"/>: the command whose state changed
/// and its full new <see cref="RibbonCommandState"/>.</summary>
public sealed class RibbonStateChangedEventArgs : EventArgs
{
    public RibbonStateChangedEventArgs(RibbonCommandId id, RibbonCommandState state)
    {
        Id = id;
        State = state;
    }

    public RibbonCommandId Id { get; }
    public RibbonCommandState State { get; }
}

/// <summary>
/// Platform-neutral source of truth for per-command ribbon state (enablement, checked-ness, value,
/// dynamic content). A renderer subscribes to <see cref="StateChanged"/> and binds its controls to the
/// store, so the host updates state by writing the store rather than poking hidden WPF stub controls.
/// </summary>
public interface IRibbonStateStore
{
    /// <summary>Raised whenever a command's state changes via any setter. Carries the full new state.</summary>
    event EventHandler<RibbonStateChangedEventArgs>? StateChanged;

    /// <summary>Returns the current state for <paramref name="id"/>, or <see cref="RibbonCommandState.Default"/>
    /// if it has never been set.</summary>
    RibbonCommandState GetState(RibbonCommandId id);

    /// <summary>Returns whether an explicit state has been written for <paramref name="id"/>.</summary>
    bool TryGetState(RibbonCommandId id, out RibbonCommandState state);

    /// <summary>Replaces the whole state for <paramref name="id"/>. Raises <see cref="StateChanged"/> only when
    /// the value actually changes.</summary>
    void SetState(RibbonCommandId id, RibbonCommandState state);

    /// <summary>Merges <see cref="RibbonCommandState.IsChecked"/> into the existing state for the command.</summary>
    void SetChecked(RibbonCommandId id, bool isChecked);

    /// <summary>Merges <see cref="RibbonCommandState.IsEnabled"/> into the existing state for the command.</summary>
    void SetEnabled(RibbonCommandId id, bool isEnabled);

    /// <summary>Merges <see cref="RibbonCommandState.Value"/> (e.g. a combo's text) into the existing state.</summary>
    void SetValue(RibbonCommandId id, string? value);

    /// <summary>Merges <see cref="RibbonCommandState.DynamicContent"/> into the existing state.</summary>
    void SetDynamicContent(RibbonCommandId id, object? content);
}

/// <summary>
/// Default <see cref="IRibbonStateStore"/>. Pure BCL (no WPF/Avalonia) so any renderer can bind to it.
/// State changes are deduplicated against the previous value, so binding subscribers are not churned by
/// no-op writes (e.g. a selection refresh re-asserting the same Bold state).
/// </summary>
public sealed class RibbonStateStore : IRibbonStateStore
{
    private readonly Dictionary<RibbonCommandId, RibbonCommandState> _states = new();

    public event EventHandler<RibbonStateChangedEventArgs>? StateChanged;

    public RibbonCommandState GetState(RibbonCommandId id) =>
        _states.TryGetValue(id, out var state) ? state : RibbonCommandState.Default;

    public bool TryGetState(RibbonCommandId id, out RibbonCommandState state) =>
        _states.TryGetValue(id, out state!);

    public void SetState(RibbonCommandId id, RibbonCommandState state)
    {
        if (_states.TryGetValue(id, out var existing) && existing == state)
            return;

        _states[id] = state;
        StateChanged?.Invoke(this, new RibbonStateChangedEventArgs(id, state));
    }

    public void SetChecked(RibbonCommandId id, bool isChecked) =>
        SetState(id, GetState(id) with { IsChecked = isChecked });

    public void SetEnabled(RibbonCommandId id, bool isEnabled) =>
        SetState(id, GetState(id) with { IsEnabled = isEnabled });

    public void SetValue(RibbonCommandId id, string? value) =>
        SetState(id, GetState(id) with { Value = value });

    public void SetDynamicContent(RibbonCommandId id, object? content) =>
        SetState(id, GetState(id) with { DynamicContent = content });
}
