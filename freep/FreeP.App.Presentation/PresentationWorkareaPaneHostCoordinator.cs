namespace FreeP.App.Compositor;

/// <summary>
/// Owns the common show/hide sequence for a portable workarea pane while native hosts retain
/// control visibility, projection rendering, and accessibility realization.
/// </summary>
public sealed class PresentationWorkareaPaneHostCoordinator<TProjection>
{
    private readonly PresentationWorkareaPaneSession _panes;
    private readonly PresentationWorkareaPane _pane;
    private readonly Func<TProjection> _refreshProjection;
    private readonly Action<bool> _setNativeVisibility;
    private readonly Action _refreshAccessibility;

    public PresentationWorkareaPaneHostCoordinator(
        PresentationWorkareaPaneSession panes,
        PresentationWorkareaPane pane,
        Func<TProjection> refreshProjection,
        Action<bool> setNativeVisibility,
        Action refreshAccessibility)
    {
        _panes = panes ?? throw new ArgumentNullException(nameof(panes));
        _pane = pane;
        _refreshProjection = refreshProjection ?? throw new ArgumentNullException(nameof(refreshProjection));
        _setNativeVisibility = setNativeVisibility ?? throw new ArgumentNullException(nameof(setNativeVisibility));
        _refreshAccessibility = refreshAccessibility ?? throw new ArgumentNullException(nameof(refreshAccessibility));
    }

    public TProjection Show()
    {
        _panes.Show(_pane);
        var projection = _refreshProjection();
        _setNativeVisibility(true);
        _refreshAccessibility();
        return projection;
    }

    public void Hide()
    {
        _panes.Hide(_pane);
        _setNativeVisibility(false);
        _refreshAccessibility();
    }
}
