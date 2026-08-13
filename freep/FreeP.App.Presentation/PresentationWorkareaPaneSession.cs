namespace FreeP.App.Compositor;

public enum PresentationWorkareaPaneVisibilityPolicy
{
    RequestedOnly,
    RequestedOrContent,
}

public sealed record PresentationWorkareaPaneState(
    PresentationWorkareaPane Pane,
    bool IsRequested,
    bool IsVisible);

public sealed record PresentationWorkareaPaneTransitionPlan(
    PresentationWorkareaPaneState Previous,
    PresentationWorkareaPaneState Current)
{
    public bool Changed => Previous != Current;
}

/// <summary>
/// Owns portable workarea pane visibility and open-request state. Native hosts apply the
/// resulting state to their controls.
/// </summary>
public sealed class PresentationWorkareaPaneSession
{
    private readonly Dictionary<PresentationWorkareaPane, PresentationWorkareaPaneState> _states = new();

    public bool IsVisible(PresentationWorkareaPane pane) => State(pane).IsVisible;

    public bool IsRequested(PresentationWorkareaPane pane) => State(pane).IsRequested;

    public PresentationWorkareaPaneTransitionPlan Show(PresentationWorkareaPane pane) =>
        Transition(pane, isRequested: true, isVisible: true);

    public PresentationWorkareaPaneTransitionPlan Hide(PresentationWorkareaPane pane) =>
        Transition(pane, isRequested: false, isVisible: false);

    public PresentationWorkareaPaneTransitionPlan ResolveVisibility(
        PresentationWorkareaPane pane,
        bool hasContent,
        PresentationWorkareaPaneVisibilityPolicy policy =
            PresentationWorkareaPaneVisibilityPolicy.RequestedOnly)
    {
        var current = State(pane);
        var isVisible = current.IsRequested ||
            (policy == PresentationWorkareaPaneVisibilityPolicy.RequestedOrContent && hasContent);
        return Transition(pane, current.IsRequested, isVisible);
    }

    public IReadOnlyList<PresentationWorkareaPaneState> BuildSnapshot() =>
        Enum.GetValues<PresentationWorkareaPane>()
            .Select(State)
            .ToArray();

    private PresentationWorkareaPaneTransitionPlan Transition(
        PresentationWorkareaPane pane,
        bool isRequested,
        bool isVisible)
    {
        var previous = State(pane);
        var current = new PresentationWorkareaPaneState(pane, isRequested, isVisible);
        _states[pane] = current;
        return new(previous, current);
    }

    private PresentationWorkareaPaneState State(PresentationWorkareaPane pane)
    {
        Validate(pane);
        return _states.GetValueOrDefault(pane) ?? new(pane, false, false);
    }

    private static void Validate(PresentationWorkareaPane pane)
    {
        if (!Enum.IsDefined(pane))
            throw new ArgumentOutOfRangeException(nameof(pane), pane, null);
    }
}
