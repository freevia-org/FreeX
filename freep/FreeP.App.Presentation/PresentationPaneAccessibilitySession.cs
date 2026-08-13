namespace FreeP.App.Compositor;

/// <summary>
/// Owns the renderer-neutral live accessibility state for presentation panes.
/// Native hosts apply the returned projections to their accessibility trees.
/// </summary>
public sealed class PresentationPaneAccessibilitySession
{
    private readonly Dictionary<string, PresentationPaneAccessibilityState> _states = new(StringComparer.Ordinal);

    public PresentationPaneAccessibilityPaneProjection UpdatePane(
        string paneId,
        bool isVisible,
        int itemCount = 0,
        int selectedIndex = -1)
    {
        var projection = PresentationPaneAccessibilityPlanner.ProjectPane(
            paneId,
            isVisible,
            itemCount,
            selectedIndex);
        _states[paneId] = projection.State;
        return projection;
    }

    public IReadOnlyList<PresentationPaneAccessibilitySnapshotEntry> BuildSnapshot() =>
        PresentationPaneAccessibilityPlanner.BuildSnapshot(_states.Values);

    public string SerializeSnapshot() =>
        PresentationPaneAccessibilityPlanner.SerializeSnapshot(_states.Values);
}
