namespace FreeW.App.Presentation.Ribbon;

/// <summary>Plans the observable part of the Review &gt; Track Changes toggle.</summary>
public sealed record TrackChangesTogglePlan(bool Enabled, bool MarkSelectionAsInsertion);

public static class TrackChangesTogglePlanner
{
    /// <summary>
    /// The WPF/FreeW command marks a non-empty selection as an insertion when Track Changes is
    /// enabled over it. The hosts perform the model mutation; this shared policy keeps their
    /// transition identical.
    /// </summary>
    public static TrackChangesTogglePlan Build(bool currentlyEnabled, bool hasSelection) =>
        new(
            Enabled: !currentlyEnabled,
            MarkSelectionAsInsertion: !currentlyEnabled && hasSelection);
}
